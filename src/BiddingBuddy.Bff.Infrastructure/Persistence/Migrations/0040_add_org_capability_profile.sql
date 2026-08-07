-- 0040_add_org_capability_profile
--
-- WHO WE ARE, so the tender analysis can answer "can WE bid this?" instead of "what does this
-- tender say?".
--
-- Until now the enrichment pipeline produced ONE artifact per tender, globally — the same text
-- for every customer — while the UI rendered it under headings like "Eligibility" and "Your
-- odds". There was nothing to be eligible *against*: `organizations` carries a name, a GSTIN and
-- an address, and `tenders.eligibility_score` is written as a literal NULL by the only writer
-- (BidProcessor's BffTenderClient). The gap these two tables fill is the reason the paid tab
-- could only ever have been generic.
--
-- With these rows present, eligibility becomes arithmetic — turnover vs the tender's threshold,
-- held certificates vs its required list, EMD vs our headroom — evaluated by a deterministic,
-- cited rules engine (Core/Fit/TenderFitRules.cs). No model is consulted to decide a verdict, so
-- a finding can be pointed at the field that produced it and cannot be hallucinated.
--
-- TWO TABLES, NOT ONE JSONB BLOB:
--   * credential EXPIRY has to be indexable. "This certificate lapses before the bid deadline"
--     is the single highest-value rule in the engine and it needs a range scan, not a JSON probe.
--   * each credential links to the vault document that PROVES it, so a finding can deep-link to
--     the file rather than asking the user to go hunting.
--
-- Past performance is deliberately NOT stored: `bids` already records won/lost, value and
-- category per org, so it is derived. A second copy would drift.
--
-- Idempotent throughout (IF NOT EXISTS + a guarded trigger create).

CREATE TABLE IF NOT EXISTS org_capability_profile (
  org_id                 UUID PRIMARY KEY REFERENCES organizations(id) ON DELETE CASCADE,

  -- ── Financial standing ────────────────────────────────────────────────────
  -- Three years because that is what Indian tenders ask for ("average annual turnover of the
  -- last three financial years"). fy1 = most recently completed FY. The labels are stored
  -- rather than computed: an org onboarding in April has a different "last completed FY" than
  -- one onboarding in March, and guessing it wrong silently shifts every turnover test.
  turnover_fy1           NUMERIC(18,2),
  turnover_fy2           NUMERIC(18,2),
  turnover_fy3           NUMERIC(18,2),
  turnover_fy1_label     TEXT,                 -- e.g. "FY 2025-26"
  net_worth              NUMERIC(18,2),

  -- Experience is derived from this, not typed, so it cannot go stale.
  incorporation_date     DATE,

  -- ── Statutory registrations that unlock RELAXATIONS ───────────────────────
  -- These do not merely describe the org: an MSE registration lowers turnover and experience
  -- thresholds (PP Policy for MSEs Order 2012) and exempts EMD. Absent them the engine has to
  -- assume the strictest reading, which is the honest default but the wrong answer for most
  -- of our customers.
  udyam_number           TEXT,
  udyam_category         TEXT,                 -- micro | small | medium
  dpiit_startup_number   TEXT,                 -- DIPP/DPIIT recognition — startup relaxations
  nsic_number            TEXT,

  -- ── Reach and capacity ────────────────────────────────────────────────────
  -- Canonical vocabularies ONLY (the 36-state list and the 40-category taxonomy). Matching is
  -- exact, so a free-text value here matches nothing forever — the same failure mode that makes
  -- an off-taxonomy category an ERROR on the buyer side.
  serviceable_states     TEXT[],
  categories_supplied    TEXT[],

  -- Working capital the org can actually block as EMD, and its sanctioned bank-guarantee line.
  -- A tender whose EMD exceeds this is a real blocker and nobody else in the product knows it.
  emd_headroom           NUMERIC(18,2),
  bg_limit               NUMERIC(18,2),
  bg_utilised            NUMERIC(18,2),

  updated_at             TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_by             UUID REFERENCES users(id) ON DELETE SET NULL,
  created_at             TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'trg_org_capability_profile_updated_at') THEN
    CREATE TRIGGER trg_org_capability_profile_updated_at
      BEFORE UPDATE ON org_capability_profile
      FOR EACH ROW EXECUTE FUNCTION set_updated_at();
  END IF;
END $$;

-- ── Credentials: certificates, OEM letters, registrations, empanelments ──────
--
-- One table with a `kind` discriminator rather than three. They differ only in what `code`
-- means, and every rule that reads them asks the same two questions — do we hold it, and is it
-- still valid on the bid date. One table means one expiry scan.
CREATE TABLE IF NOT EXISTS org_credentials (
  id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  org_id        UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,

  -- certification | oem_authorization | registration | empanelment
  kind          TEXT NOT NULL,

  -- The matchable identity. For a certification the standard ("ISO 9001:2015"); for an OEM
  -- authorization the brand ("DELL"); for a registration the scheme ("Udyam"). Normalised
  -- upper-case by the service so matching against a tender's required list is case-stable.
  code          TEXT NOT NULL,
  label         TEXT,                          -- human-facing name, free text
  number        TEXT,                          -- certificate / letter reference
  issued_at     DATE,

  -- NULL = perpetual. The rules engine treats NULL as "does not lapse" rather than as
  -- "expired", because the common case for a registration genuinely has no end date.
  valid_until   DATE,

  -- The vault document that proves it. ON DELETE SET NULL: deleting the file must not delete
  -- the claim — the org still holds the certificate, we just no longer have the PDF.
  document_id   UUID REFERENCES documents(id) ON DELETE SET NULL,

  -- Reserved for a later human/automated verification step. NULL = self-asserted, which is
  -- what every row is today; findings say so rather than implying we checked.
  verified_at   TIMESTAMPTZ,

  notes         TEXT,
  created_by    UUID REFERENCES users(id) ON DELETE SET NULL,
  created_at    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at    TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'trg_org_credentials_updated_at') THEN
    CREATE TRIGGER trg_org_credentials_updated_at
      BEFORE UPDATE ON org_credentials
      FOR EACH ROW EXECUTE FUNCTION set_updated_at();
  END IF;
END $$;

-- One row per (org, kind, code). Re-adding "ISO 9001:2015" updates the expiry rather than
-- creating a second row that a rule would then have to pick between.
CREATE UNIQUE INDEX IF NOT EXISTS ux_org_credentials_org_kind_code
  ON org_credentials (org_id, kind, code);

CREATE INDEX IF NOT EXISTS idx_org_credentials_org
  ON org_credentials (org_id);

-- Drives both the per-tender expiry rule and a future "expiring soon" sweep.
CREATE INDEX IF NOT EXISTS idx_org_credentials_valid_until
  ON org_credentials (valid_until)
  WHERE valid_until IS NOT NULL;
