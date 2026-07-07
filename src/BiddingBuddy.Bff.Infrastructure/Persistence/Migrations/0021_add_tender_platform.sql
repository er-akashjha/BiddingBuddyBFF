-- 0021: per-tender source-portal provenance.
--
-- BidProcessor already sends the source portal ("gem" | "eprocure" | "ireps" | …) in the
-- /internal/tenders payload (from the downloader envelope's Platform), but the BFF had no
-- column to store it, so it was silently dropped. This adds the column so the SPA can badge
-- and filter tenders by their originating portal (GeM vs eProcure vs IREPS …).
--
-- Distinct from the coarse `source` tag ("gem_pipeline"): `platform` is per-tender provenance.
-- Existing rows default to 'gem' (the only portal live before eprocure/ireps).

ALTER TABLE tenders ADD COLUMN IF NOT EXISTS platform text DEFAULT 'gem';

CREATE INDEX IF NOT EXISTS idx_tenders_platform ON tenders (platform);
