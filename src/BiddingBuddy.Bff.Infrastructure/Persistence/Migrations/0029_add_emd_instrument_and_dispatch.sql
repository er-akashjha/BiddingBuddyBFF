-- 0029_add_emd_instrument_and_dispatch
-- EMD instrument details + physical courier tracking on a bid.
--
-- The MONEY side of EMD already existed (emd_payments + /api/payments/emd + the EMD_STUCK
-- alert). What was missing is the INSTRUMENT side — a DD / Bank Guarantee / FDR whose
-- ORIGINAL PAPER has to physically reach the buyer's office before technical-bid opening —
-- and the courier leg that gets it there. Missing that cut-off disqualifies the bid no
-- matter how good it is, and nothing in the product tracked it.
--
-- Three parts:
--   1. emd_payments gains instrument columns + a scan document, and its two CHECK
--      constraints are widened (they only knew online payment rails and held/refunded/forfeited).
--   2. bid_dispatches — a new table for the physical leg. Deliberately general
--      (a `purpose` column) so hard-copy bid submission and sample dispatch can reuse it
--      without another migration; v1 only exposes the EMD case in the UI.
--   3. bids gains emd_requirement so "is EMD even needed here?" is an answerable question.
--   4. Templates for three new deadline-scan alerts.
--
-- Idempotent: safe to re-run.

-- ── 1. emd_payments: instrument columns ─────────────────────────────────────

ALTER TABLE emd_payments
  ADD COLUMN IF NOT EXISTS instrument_number TEXT,
  ADD COLUMN IF NOT EXISTS instrument_date   DATE,
  -- BG/FDR expiry. An expired Bank Guarantee is a live financial risk: the buyer can no
  -- longer invoke it, which in most tender conditions is grounds for disqualification,
  -- and the bank releases the lien. This is what EMD_BG_EXPIRING watches.
  ADD COLUMN IF NOT EXISTS valid_until       DATE,
  ADD COLUMN IF NOT EXISTS issuing_branch    TEXT,
  ADD COLUMN IF NOT EXISTS favouring         TEXT,   -- payee, e.g. "Pay & Accounts Officer, ..."
  -- When the EMD must be in place — normally the bid submission deadline.
  ADD COLUMN IF NOT EXISTS due_date          DATE,
  -- Scan of the DD / BG / FDR. A LINK to the org vault, never a copy: documents.folder_id
  -- is a single FK, so filing a doc into a per-bid folder would move it out of the vault
  -- and strand it on one bid. Same reasoning as bid_documents (0026).
  ADD COLUMN IF NOT EXISTS document_id       UUID REFERENCES documents(id) ON DELETE SET NULL;

-- 1a. Widen the payment_mode CHECK.
--
-- The original was ('neft','rtgs','upi','dd','online') — online rails plus DD. It cannot
-- hold a Bank Guarantee, FDR, banker's cheque, surety bond, or an exemption, which is
-- precisely what this feature is about.
--
-- ⚠ emd_payments PREDATES DbMigrator: it was created from database/schema.sql by hand, not
-- by any 0001–0028 script, so its constraint names in a given environment are whatever
-- Postgres generated at the time and are NOT guaranteed to be emd_payments_payment_mode_check.
-- Drop by LOOKUP over pg_constraint rather than by name. The loop also re-drops the
-- constraint this script itself adds, which is what makes the whole block re-runnable.
DO $$
DECLARE c record;
BEGIN
  FOR c IN
    SELECT con.conname
      FROM pg_constraint con
      JOIN pg_class rel ON rel.oid = con.conrelid
     WHERE rel.relname = 'emd_payments'
       AND con.contype = 'c'
       AND pg_get_constraintdef(con.oid) ILIKE '%payment_mode%'
  LOOP
    EXECUTE format('ALTER TABLE emd_payments DROP CONSTRAINT %I', c.conname);
  END LOOP;
END $$;

ALTER TABLE emd_payments ADD CONSTRAINT emd_payments_payment_mode_check
  CHECK (payment_mode IS NULL OR payment_mode IN (
    'neft','rtgs','upi','online',                        -- money moves electronically
    'dd','bg','fdr','bankers_cheque','surety_bond',      -- instrument, physical original
    'exempt'                                             -- MSME / NSIC / Startup
  ));

-- 1b. Widen the status CHECK — 'pending' (EMD identified but not yet arranged) and
-- 'submitted' (instrument dispatched/handed over, buyer has not confirmed receipt) are
-- both states that exist before 'held' and had nowhere to live.
DO $$
DECLARE c record;
BEGIN
  FOR c IN
    SELECT con.conname
      FROM pg_constraint con
      JOIN pg_class rel ON rel.oid = con.conrelid
     WHERE rel.relname = 'emd_payments'
       AND con.contype = 'c'
       AND pg_get_constraintdef(con.oid) ILIKE '%status%'
  LOOP
    EXECUTE format('ALTER TABLE emd_payments DROP CONSTRAINT %I', c.conname);
  END LOOP;
END $$;

ALTER TABLE emd_payments ADD CONSTRAINT emd_payments_status_check
  CHECK (status IN ('pending','submitted','held','refunded','forfeited'));

-- 1c. One EMD per bid.
--
-- GUARDED, not unconditional. A migration that hard-fails on pre-existing data blocks every
-- later script in the chain (DbMigrator stops at the first failure), and whether any org
-- already has two EMD rows against one bid is not knowable from here. So: enforce it where
-- the data allows, skip loudly where it doesn't. EmdService picks the newest row either way,
-- so the feature is correct even in an environment where this index never gets created.
DO $$
BEGIN
  IF EXISTS (
    SELECT 1 FROM emd_payments
     WHERE bid_id IS NOT NULL
     GROUP BY bid_id HAVING COUNT(*) > 1
  ) THEN
    RAISE WARNING 'ux_emd_payments_bid NOT created — duplicate bid_id rows exist in emd_payments. Deduplicate, then re-run this index manually.';
  ELSE
    CREATE UNIQUE INDEX IF NOT EXISTS ux_emd_payments_bid
      ON emd_payments (bid_id) WHERE bid_id IS NOT NULL;
  END IF;
END $$;

CREATE INDEX IF NOT EXISTS idx_emd_payments_valid_until ON emd_payments (valid_until)
  WHERE valid_until IS NOT NULL;

-- ── 2. bid_dispatches — the physical leg ────────────────────────────────────
--
-- Its own table rather than courier_* columns on emd_payments, because one EMD can have
-- MORE THAN ONE dispatch: a courier that returns undelivered has to be re-sent, and the
-- refund instrument travels back inbound. A single column-set cannot hold both legs.
--
-- org_id is denormalised from bids so listings filter org-scoped without a join — same
-- rationale as bid_documents (0026).
CREATE TABLE IF NOT EXISTS bid_dispatches (
  id                   UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  org_id               UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
  bid_id               UUID NOT NULL REFERENCES bids(id)          ON DELETE CASCADE,
  -- Nullable: a hard-copy bid dispatch has no EMD. SET NULL so deleting an EMD record
  -- doesn't destroy the courier history that proves something was sent.
  emd_payment_id       UUID REFERENCES emd_payments(id) ON DELETE SET NULL,

  purpose              TEXT NOT NULL DEFAULT 'emd_instrument'
                         CHECK (purpose IN ('emd_instrument','hard_copy_bid','sample','other')),
  direction            TEXT NOT NULL DEFAULT 'outbound'
                         CHECK (direction IN ('outbound','inbound')),

  courier_name         TEXT,
  tracking_number      TEXT,
  tracking_url         TEXT,

  dispatched_on        DATE,
  dispatched_by        UUID REFERENCES users(id),

  recipient_name        TEXT,
  recipient_designation TEXT,
  recipient_address     TEXT,
  recipient_phone       TEXT,

  -- The hard cut-off from the tender (usually technical-bid opening). This is the date the
  -- alerts key off — NOT expected_delivery_on, which is only the courier's own promise.
  deliver_by           DATE,
  expected_delivery_on DATE,
  delivered_on         DATE,
  received_by          TEXT,

  status               TEXT NOT NULL DEFAULT 'draft'
                         CHECK (status IN ('draft','dispatched','in_transit','delivered','returned','lost')),

  pod_document_id      UUID REFERENCES documents(id) ON DELETE SET NULL,  -- proof of delivery
  notes                TEXT,
  created_at           TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at           TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_bid_dispatches_bid    ON bid_dispatches (bid_id);
CREATE INDEX IF NOT EXISTS idx_bid_dispatches_org    ON bid_dispatches (org_id);
CREATE INDEX IF NOT EXISTS idx_bid_dispatches_emd    ON bid_dispatches (emd_payment_id);
-- Drives the "dispatched but not delivered" scan.
CREATE INDEX IF NOT EXISTS idx_bid_dispatches_open   ON bid_dispatches (status, expected_delivery_on)
  WHERE status IN ('dispatched','in_transit');

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'trg_bid_dispatches_updated_at') THEN
    CREATE TRIGGER trg_bid_dispatches_updated_at
      BEFORE UPDATE ON bid_dispatches
      FOR EACH ROW EXECUTE FUNCTION set_updated_at();
  END IF;
END $$;

-- ── 3. bids: is EMD even needed? ────────────────────────────────────────────
-- 'unknown' is the honest default for every existing row: the tender may or may not demand
-- EMD and nobody has said. The UI seeds it from the tender's emd_amount when a bid is
-- created from a tender, and the user can override (exemption is common for MSME/NSIC).
ALTER TABLE bids
  ADD COLUMN IF NOT EXISTS emd_requirement TEXT NOT NULL DEFAULT 'unknown',
  ADD COLUMN IF NOT EXISTS emd_exemption_basis TEXT;   -- MSME | NSIC | Startup | Other

DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_constraint WHERE conname = 'bids_emd_requirement_check'
  ) THEN
    ALTER TABLE bids ADD CONSTRAINT bids_emd_requirement_check
      CHECK (emd_requirement IN ('unknown','required','exempt','not_required'));
  END IF;
END $$;

-- ── 4. Alert templates ──────────────────────────────────────────────────────
-- Same compact-card layout as 0015. Handlebars.Net (logic-less): {{Var}}, {{#if}}{{/if}}.
-- All three deep-link to the BID, not /payments — the EMD tab lives on the bid page and
-- that is where the user acts.

INSERT INTO notification_templates (code, channel, name, subject, body, body_format, metadata)
VALUES
  -- ░░ EMD instrument needs to be couriered and hasn't been ░░
  ('EMD_DISPATCH_DUE','Email','EMD dispatch due',
   'Courier the EMD for "{{BidTitle}}" — due {{DueText}}',
   '<div style="font-family:-apple-system,Segoe UI,Roboto,Arial,sans-serif;max-width:560px;margin:0 auto;padding:24px;"><div style="font-size:15px;font-weight:700;color:#16263F;margin-bottom:16px;">Tenders<span style="color:#13B07C;">Agent</span></div><h1 style="font-size:18px;color:#854F0B;margin:0 0 8px;">EMD still needs to be sent</h1><p style="font-size:14px;color:#5b6b7b;line-height:21px;margin:0 0 18px;">Hi {{FirstName}}, the {{InstrumentLabel}} for <b>{{BidTitle}}</b> has not been dispatched yet and must reach the buyer by <b>{{DeliverBy}}</b> ({{DueText}}). A physical original that arrives late is treated as no EMD at all.</p><a href="{{Link}}" style="display:inline-block;background:#13B07C;color:#fff;font-size:14px;font-weight:600;text-decoration:none;padding:10px 22px;border-radius:8px;">Open bid</a></div>',
   'Html','{}'::jsonb),
  ('EMD_DISPATCH_DUE','InApp','EMD dispatch due (in-app)','EMD not dispatched','{{InstrumentLabel}} for "{{BidTitle}}" must reach the buyer by {{DeliverBy}}.','Text',
   '{"orgId":"{{OrgId}}","type":"deadline","entityType":"bid","entityId":"{{EntityId}}"}'::jsonb),

  -- ░░ Couriered, promised date passed, still not delivered ░░
  ('EMD_DELIVERY_OVERDUE','Email','EMD delivery overdue',
   'EMD courier for "{{BidTitle}}" has not been delivered',
   '<div style="font-family:-apple-system,Segoe UI,Roboto,Arial,sans-serif;max-width:560px;margin:0 auto;padding:24px;"><div style="font-size:15px;font-weight:700;color:#16263F;margin-bottom:16px;">Tenders<span style="color:#13B07C;">Agent</span></div><h1 style="font-size:18px;color:#854F0B;margin:0 0 8px;">EMD courier is late</h1><p style="font-size:14px;color:#5b6b7b;line-height:21px;margin:0 0 18px;">Hi {{FirstName}}, the EMD for <b>{{BidTitle}}</b> was dispatched via {{CourierName}} on {{DispatchedOn}} (AWB {{TrackingNumber}}) and was expected {{ExpectedText}}, but has not been marked delivered. Chase the courier and confirm receipt with the buyer.</p><a href="{{Link}}" style="display:inline-block;background:#13B07C;color:#fff;font-size:14px;font-weight:600;text-decoration:none;padding:10px 22px;border-radius:8px;">Open bid</a></div>',
   'Html','{}'::jsonb),
  ('EMD_DELIVERY_OVERDUE','InApp','EMD delivery overdue (in-app)','EMD courier is late','{{CourierName}} consignment {{TrackingNumber}} for "{{BidTitle}}" is not delivered yet.','Text',
   '{"orgId":"{{OrgId}}","type":"deadline","entityType":"bid","entityId":"{{EntityId}}"}'::jsonb),

  -- ░░ Bank Guarantee about to expire ░░
  ('EMD_BG_EXPIRING','Email','EMD instrument expiring',
   '{{InstrumentLabel}} for "{{BidTitle}}" expires {{ExpiryText}}',
   '<div style="font-family:-apple-system,Segoe UI,Roboto,Arial,sans-serif;max-width:560px;margin:0 auto;padding:24px;"><div style="font-size:15px;font-weight:700;color:#16263F;margin-bottom:16px;">Tenders<span style="color:#13B07C;">Agent</span></div><h1 style="font-size:18px;color:#854F0B;margin:0 0 8px;">EMD instrument expiring</h1><p style="font-size:14px;color:#5b6b7b;line-height:21px;margin:0 0 18px;">Hi {{FirstName}}, the {{InstrumentLabel}} of &#8377;{{Amount}} for <b>{{BidTitle}}</b> expires on <b>{{ValidUntil}}</b> ({{ExpiryText}}). If the bid is still live, get it extended — an expired instrument is usually treated as a withdrawn EMD.</p><a href="{{Link}}" style="display:inline-block;background:#13B07C;color:#fff;font-size:14px;font-weight:600;text-decoration:none;padding:10px 22px;border-radius:8px;">Open bid</a></div>',
   'Html','{}'::jsonb),
  ('EMD_BG_EXPIRING','InApp','EMD instrument expiring (in-app)','EMD instrument expiring','{{InstrumentLabel}} for "{{BidTitle}}" expires {{ExpiryText}}.','Text',
   '{"orgId":"{{OrgId}}","type":"deadline","entityType":"bid","entityId":"{{EntityId}}"}'::jsonb)
ON CONFLICT ON CONSTRAINT uq_template_code_channel DO NOTHING;
