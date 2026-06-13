-- Migration: add bid_comments table
-- Apply against the existing biddingbuddy database.
-- Idempotent: safe to run more than once.

CREATE TABLE IF NOT EXISTS bid_comments (
  id           UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  bid_id       UUID NOT NULL REFERENCES bids(id) ON DELETE CASCADE,
  author_id    UUID NOT NULL REFERENCES users(id),
  body         TEXT NOT NULL,
  created_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at   TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_bid_comments_bid_id ON bid_comments (bid_id);

DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_trigger WHERE tgname = 'trg_bid_comments_updated_at'
  ) THEN
    CREATE TRIGGER trg_bid_comments_updated_at
      BEFORE UPDATE ON bid_comments
      FOR EACH ROW EXECUTE FUNCTION set_updated_at();
  END IF;
END $$;
