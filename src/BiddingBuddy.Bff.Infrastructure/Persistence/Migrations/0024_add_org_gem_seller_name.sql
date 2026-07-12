-- 0024_add_org_gem_seller_name
-- Adds organizations.gem_seller_name — the company's seller name as it appears on GeM bid result
-- ladders. Lets the tender-results award pipeline (InternalPipelineService.OnTenderAwardedAsync)
-- resolve an org's bid to won/lost by matching the ladder, instead of the value-match heuristic.
-- Optional; resolution falls back to the org name.
--
-- Idempotent: ADD COLUMN IF NOT EXISTS.

ALTER TABLE organizations ADD COLUMN IF NOT EXISTS gem_seller_name text;
