-- 0013_add_bid_status_category
-- Phase 0 of the Bid Management redesign (BID-001).
--
-- 1. Widen the bids.stage CHECK constraint to allow a new terminal stage 'dropped'
--    (abandoned bids). All existing values stay valid — this is purely additive.
-- 2. Add a generated status_category column (open|closed) derived from stage, so the
--    UI/API can fold terminal stages into a single "Closed" group without re-deriving
--    the mapping and without risk of it drifting from stage.
-- 3. Index (org_id, status_category) for the default "open bids only" list query.
--
-- Idempotent: safe to re-run (the runner also wraps + records this in one transaction).

-- 1 ── widen the stage check constraint -------------------------------------------------
ALTER TABLE bids DROP CONSTRAINT IF EXISTS bids_stage_check;
ALTER TABLE bids ADD CONSTRAINT bids_stage_check
  CHECK (stage IN ('identified','reviewing','preparing','approval','submitted','won','lost','dropped'));

-- 2 ── generated status_category (open|closed) ------------------------------------------
ALTER TABLE bids ADD COLUMN IF NOT EXISTS status_category TEXT
  GENERATED ALWAYS AS (
    CASE WHEN stage IN ('won','lost','dropped') THEN 'closed' ELSE 'open' END
  ) STORED;

-- 3 ── index for the default open-bids list ---------------------------------------------
CREATE INDEX IF NOT EXISTS idx_bids_org_status_cat ON bids (org_id, status_category);
