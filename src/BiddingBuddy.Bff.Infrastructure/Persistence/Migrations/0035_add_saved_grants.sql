-- 0035_add_saved_grants
-- A user's org "saved / tracked" grant opportunities — backs the star/plus toggle on the grant
-- discovery list and detail page. Org-scoped, unlike the global `grant_opportunities` corpus.
--
-- SNAPSHOT, NOT A JOIN: the client sends the display fields it already has on screen (title,
-- agency, deadline, …). Storing them here means the saved list renders with no Mongo round-trip
-- and no dependency on `grant_opportunities` being mirrored. `mongo_grant_id` (the grant's Mongo
-- _id) is both the dedup key and the deep-link back to /grants/{id}.
--
-- Idempotent: CREATE TABLE / INDEX ... IF NOT EXISTS throughout.

CREATE TABLE IF NOT EXISTS saved_grants (
  id                 UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  org_id             UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,

  -- BiddingBuddyServices Mongo _id of the grant. Dedup key + deep-link.
  mongo_grant_id     TEXT NOT NULL,

  -- Client-supplied snapshot of the grant's display fields.
  title              TEXT NOT NULL,
  agency_name        TEXT,
  opportunity_number TEXT,
  category           TEXT,
  close_date         DATE,
  award_ceiling      NUMERIC(15,2),
  currency           TEXT NOT NULL DEFAULT 'USD',
  is_forecast        BOOLEAN NOT NULL DEFAULT FALSE,
  source_url         TEXT,

  saved_by           UUID REFERENCES users(id) ON DELETE SET NULL,
  note               TEXT,
  created_at         TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- One save per grant per org — the toggle upserts on this.
CREATE UNIQUE INDEX IF NOT EXISTS ux_saved_grants_org_grant
  ON saved_grants (org_id, mongo_grant_id);

CREATE INDEX IF NOT EXISTS idx_saved_grants_org_created
  ON saved_grants (org_id, created_at DESC);
