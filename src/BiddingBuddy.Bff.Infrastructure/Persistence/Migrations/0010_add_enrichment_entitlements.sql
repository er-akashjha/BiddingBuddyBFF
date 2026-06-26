-- 0010_add_enrichment_entitlements
-- Pay-gated, on-demand AI enrichment.
--
-- (1) Per-org entitlement to VIEW a tender's AI analysis. Enrichment data is global
--     (computed once, stored in Mongo) but access is sold per org. The UNIQUE(org_id,
--     gem_tender_id) constraint makes an unlock idempotent and is the anti-double-charge
--     guard (INSERT ... ON CONFLICT DO NOTHING).
-- (2) A global enrichment lifecycle column on the tenders projection so the pipeline can
--     report progress, the entitlement flip (pending -> unlocked) can fire when enrichment
--     completes, and the "AI ready" email can be sent once (enrichment_notified_at).
--
-- Idempotent: safe to re-run.

CREATE TABLE IF NOT EXISTS tender_enrichment_entitlements (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    org_id              UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
    gem_tender_id       TEXT NOT NULL,
    status              TEXT NOT NULL DEFAULT 'pending',   -- pending | unlocked | failed
    source              TEXT NOT NULL DEFAULT 'grant',     -- grant | purchase | credit | plan
    payment_ref         TEXT NULL,
    unlocked_by_user_id UUID NULL,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
    unlocked_at         TIMESTAMPTZ NULL,
    CONSTRAINT uq_enrichment_entitlement_org_tender UNIQUE (org_id, gem_tender_id)
);

CREATE INDEX IF NOT EXISTS idx_enrichment_entitlements_org ON tender_enrichment_entitlements (org_id);
CREATE INDEX IF NOT EXISTS idx_enrichment_entitlements_gem ON tender_enrichment_entitlements (gem_tender_id);

-- Global enrichment lifecycle on the BFF tender projection.
--   none | extracted | queued | processing | enriched | failed
ALTER TABLE tenders ADD COLUMN IF NOT EXISTS enrichment_status      TEXT NOT NULL DEFAULT 'none';
ALTER TABLE tenders ADD COLUMN IF NOT EXISTS enrichment_notified_at TIMESTAMPTZ NULL;
CREATE INDEX IF NOT EXISTS idx_tenders_enrichment_status ON tenders (enrichment_status);

-- Backfill: any tender that already carries AI data was enriched under the old
-- always-on pipeline — mark it so the read-path/masking treats it correctly.
UPDATE tenders
   SET enrichment_status = 'enriched'
 WHERE enrichment_status = 'none'
   AND (ai_summary IS NOT NULL OR ai_score IS NOT NULL);
