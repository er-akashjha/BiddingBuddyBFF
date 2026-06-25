-- 0008_add_tender_alert_scan
-- Scheduled tender-alert scan.
--
-- Adds a per-tender "have we evaluated this for alerts yet" marker so a cron job
-- can pick up only newly-added tenders (alerts_scanned_at IS NULL), match them
-- against each org's interest rules, email ONE digest per matched org, then stamp
-- them scanned. Replaces the old inline on-upsert matching.
--
-- Idempotent: safe to re-run.

-- 1. The marker column.
ALTER TABLE tenders ADD COLUMN IF NOT EXISTS alerts_scanned_at TIMESTAMPTZ NULL;

-- 2. Partial index for the hot scan query: WHERE alerts_scanned_at IS NULL ORDER BY created_at.
CREATE INDEX IF NOT EXISTS idx_tenders_alerts_unscanned
    ON tenders (created_at) WHERE alerts_scanned_at IS NULL;

-- 3. Clean go-live: treat every tender that already exists at apply time as
--    "already evaluated" so the first scan does NOT blast the whole backlog to
--    customers. Only genuinely-new tenders (inserted after this migration) start
--    NULL and get picked up.
--
--    To intentionally backfill alerts for existing tenders, either re-arm them
--      UPDATE tenders SET alerts_scanned_at = NULL WHERE <criteria>;
--    and let the next scan run, or call POST /internal/matching/scan?backfill=true.
UPDATE tenders SET alerts_scanned_at = now() WHERE alerts_scanned_at IS NULL;
