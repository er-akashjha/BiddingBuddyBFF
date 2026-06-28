-- 0010_add_tender_mongo_id
-- WS2 — portal-agnostic tender links in digest emails.
--
-- The digest email is generated from the BFF's PostgreSQL tender row, but the SPA
-- route /tenders/:id resolves a tender by its MongoDB _id (the BFF proxies detail
-- lookups to BiddingBuddyServices, which looks up by _id). The Postgres PK is a
-- different id-space, so a link built from it opens the wrong tender (or 404s).
--
-- Store the Mongo _id alongside the Postgres row so links are built from the
-- canonical, source-portal-independent identifier. It is the same id the rest of
-- the app links by, so this works for tenders from ANY portal (not just GeM) as
-- long as they flow through BiddingBuddyServices. Populated by the pipeline on the
-- next enrichment of each tender (nullable until then).
--
-- Idempotent.

ALTER TABLE tenders ADD COLUMN IF NOT EXISTS mongo_tender_id TEXT;

-- Partial unique index: one Postgres row per Mongo tender, but many NULLs allowed
-- (rows not yet re-enriched after this migration).
CREATE UNIQUE INDEX IF NOT EXISTS idx_tenders_mongo_tender_id
    ON tenders (mongo_tender_id)
    WHERE mongo_tender_id IS NOT NULL;
