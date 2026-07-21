-- 0028_add_grant_opportunities
-- The Postgres shadow index for the GRANT product line. Mongo (BiddingBuddyServices `grants`)
-- stays authoritative for the full record; this table exists so matching, alerting, deep-links and
-- org-scoped queries have something relational to work against — exactly the role `tenders` plays
-- for the tender line.
--
-- WHY A SEPARATE TABLE, NOT A `product_line` COLUMN ON `tenders`:
-- roughly 40% of `tenders` is procurement-only (emd_amount, tender_value semantics, corrigendum
-- count) and would be permanently NULL for a grant, while everything grant-specific below —
-- applicant-type eligibility, USD award ceilings, tribal set-aside, LOI vs full deadline — has no
-- column there. A discriminator would also force a kind predicate onto every one of the ~105
-- hand-written `.Where(org_id = …)` sites, and a miss is a silent cross-product leak.
--
-- `id UUID` GENERATED, `mongo_grant_id TEXT` UNIQUE-PARTIAL:
-- notification_reminders.entity_id is UUID NOT NULL and user_notifications.entity_id is uuid, so
-- the row's own id must be a real UUID from day one. The Mongo _id is also GUID-shaped (enforced
-- in BiddingBuddyServices), but it arrives as text and is set-once — migration 0010 had to
-- retrofit exactly this for tenders, and this avoids repeating that.
--
-- NO org_id: grant opportunities are a GLOBAL corpus, like tenders. Org-scoped grant tables
-- (matches, alert rules, settings) come with the matching workstream and carry their own tenancy.
--
-- Deadlines are DATE, not TIMESTAMPTZ, on purpose. Grants.gov publishes no cutoff TIME — dates
-- come back as a literal midnight Eastern, and federal deadlines are conventionally 5 PM or
-- 11:59 PM ET without the API ever saying which. A TIMESTAMPTZ here would encode an invented
-- deadline; `close_date_explanation` carries the source's own prose instead.
--
-- Idempotent: CREATE TABLE / INDEX … IF NOT EXISTS throughout, and the trigger is guarded by a
-- pg_trigger existence check.

CREATE TABLE IF NOT EXISTS grant_opportunities (
  id                    UUID PRIMARY KEY DEFAULT gen_random_uuid(),

  -- Natural key from the source. Composite because a grant id is only unique within its portal.
  platform              TEXT NOT NULL DEFAULT 'grants-gov',
  platform_grant_id     TEXT NOT NULL,

  -- BiddingBuddyServices Mongo _id. Set-once; the SPA deep-links by it.
  mongo_grant_id        TEXT,

  opportunity_number    TEXT,
  source_url            TEXT,

  title                 TEXT NOT NULL,
  summary               TEXT,
  agency_name           TEXT,
  agency_code           TEXT,
  category              TEXT,

  -- USD. NULL means "not published" and must never be coerced to 0 — a 0 ceiling asserts the
  -- grant awards nothing, which is a different and usually false claim.
  currency              TEXT NOT NULL DEFAULT 'USD',
  award_ceiling         NUMERIC(15,2),
  award_floor           NUMERIC(15,2),
  estimated_total_program_funding NUMERIC(15,2),
  expected_number_of_awards       INTEGER,
  cost_sharing_required BOOLEAN,

  posted_date           DATE,
  close_date            DATE,
  loi_due_date          DATE,
  archive_date          DATE,
  close_date_explanation TEXT,
  is_rolling            BOOLEAN NOT NULL DEFAULT FALSE,

  -- Verbatim source labels AND the derived codes. Both, deliberately: the labels are the closed
  -- government vocabulary that decides who may legally apply and must survive unparaphrased, while
  -- the codes are what a query can practically filter on.
  applicant_types_raw   TEXT[],
  applicant_type_codes  TEXT[],

  tribal_governments_eligible   BOOLEAN,
  tribal_organizations_eligible BOOLEAN,
  nonprofit_501c3_eligible      BOOLEAN,
  is_tribal_set_aside           BOOLEAN,
  native_led_priority           BOOLEAN,

  assistance_listing_numbers TEXT[],
  funding_instruments        TEXT[],

  -- A forecast is announced intent with provisional dates. Surfaced (it is how an org plans a
  -- year) but never presentable as applyable, hence its own status value rather than a flag alone.
  is_forecast           BOOLEAN NOT NULL DEFAULT FALSE,
  status                TEXT NOT NULL DEFAULT 'posted',

  ai_score              INTEGER NOT NULL DEFAULT 0,
  ai_summary            TEXT,
  ai_tags               TEXT[],

  -- NULL = never scanned for alert matches. The partial index below makes the scan's
  -- "find unscanned" query cheap without indexing the whole table.
  alerts_scanned_at     TIMESTAMPTZ,

  created_at            TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at            TIMESTAMPTZ,

  CONSTRAINT ck_grant_opportunities_status
    CHECK (status IN ('forecasted', 'posted', 'closed', 'archived'))
);

-- The natural key. Composite because platform_grant_id is only unique within a portal —
-- the same lesson migration 0022 had to apply to tenders after a single-column unique proved wrong.
CREATE UNIQUE INDEX IF NOT EXISTS ux_grant_opportunities_platform_grant_id
  ON grant_opportunities (platform, platform_grant_id);

-- Partial: rows without a Mongo id are not yet mirrored, and there may be many of them.
CREATE UNIQUE INDEX IF NOT EXISTS ux_grant_opportunities_mongo_grant_id
  ON grant_opportunities (mongo_grant_id)
  WHERE mongo_grant_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_grant_opportunities_close_date
  ON grant_opportunities (close_date);

CREATE INDEX IF NOT EXISTS idx_grant_opportunities_category
  ON grant_opportunities (category);

-- The eligibility query — "which grants can a federally recognized tribe apply for" is the
-- question this product exists to answer. GIN over the code array so `&&` / `@>` are indexed.
CREATE INDEX IF NOT EXISTS idx_grant_opportunities_applicant_codes
  ON grant_opportunities USING GIN (applicant_type_codes);

-- Drives the alert scan's batch query. Partial so it stays small: once a row is scanned it leaves
-- the index entirely. Mirrors idx_tenders_alerts_unscanned from 0008.
CREATE INDEX IF NOT EXISTS idx_grant_opportunities_alerts_unscanned
  ON grant_opportunities (created_at)
  WHERE alerts_scanned_at IS NULL;

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'trg_grant_opportunities_updated_at') THEN
    CREATE TRIGGER trg_grant_opportunities_updated_at
      BEFORE UPDATE ON grant_opportunities
      FOR EACH ROW EXECUTE FUNCTION set_updated_at();
  END IF;
END $$;
