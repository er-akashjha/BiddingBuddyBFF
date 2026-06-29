-- 0011_add_alert_cadence
-- WS1 — stop the digest flood.
--
-- Previously the scheduled scan dispatched one digest per matched org on EVERY
-- scan tick (default every 15 min), ignoring any throttle — so a customer could
-- get many single-tender "1 new match" emails per day. Alerts now batch by a
-- per-org minimum send interval (a cooldown): matches accumulate as tender_matches
-- with status='pending' and are flushed as ONE grouped digest only once the
-- cooldown since the last send has elapsed.
--
--   min_send_interval_minutes — smallest gap between two digest emails (default 6h).
--   last_digest_sent_at       — server-managed timestamp of the most recent send.
--
-- Idempotent.

ALTER TABLE org_alert_settings
    ADD COLUMN IF NOT EXISTS min_send_interval_minutes INTEGER NOT NULL DEFAULT 360;

ALTER TABLE org_alert_settings
    ADD COLUMN IF NOT EXISTS last_digest_sent_at TIMESTAMPTZ NULL;

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_org_alert_min_interval') THEN
    ALTER TABLE org_alert_settings
      ADD CONSTRAINT ck_org_alert_min_interval
      CHECK (min_send_interval_minutes BETWEEN 15 AND 10080);   -- 15 min .. 7 days
  END IF;
END $$;
