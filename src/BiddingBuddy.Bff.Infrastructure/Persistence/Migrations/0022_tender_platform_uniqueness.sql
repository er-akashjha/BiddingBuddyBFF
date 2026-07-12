-- 0022_tender_platform_uniqueness
-- Extends migration 0021 (which added `tenders.platform` + index) with the
-- multi-portal identity guarantee: two different portals (GeM, eProcure, IREPS, …)
-- can each emit the same `gem_tender_id` without silently overwriting one another.
--
-- Historical column name `gem_tender_id` is kept — it now means "platform tender id".
-- Renaming would ripple through bids/orders joins for zero behavioural gain.
--
-- Prereq: migration 0021 has already added `tenders.platform` (default 'gem').
-- Idempotent (IF NOT EXISTS / IF EXISTS).

-- Belt-and-suspenders: default handles new rows; this handles any pre-0021 row
-- that predates the column entirely (would already be defaulted, but explicit).
UPDATE tenders SET platform = 'gem' WHERE platform IS NULL;

-- Replace single-column uniqueness with the composite identity. Any cross-portal
-- id collision that previously overwrote a row now becomes two distinct rows.
ALTER TABLE tenders DROP CONSTRAINT IF EXISTS tenders_gem_tender_id_key;
DROP INDEX IF EXISTS tenders_gem_tender_id_key;

CREATE UNIQUE INDEX IF NOT EXISTS ux_tenders_platform_tender_id
    ON tenders (platform, gem_tender_id);
