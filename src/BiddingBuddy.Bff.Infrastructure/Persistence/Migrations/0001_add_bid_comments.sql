-- 0001_add_bid_comments
-- Adds the bid_comments table (user-authored comments on a bid).
-- Must be idempotent: the runner wraps it in a transaction and records it,
-- but guards (IF NOT EXISTS) keep it safe even if applied out-of-band.

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
  IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'trg_bid_comments_updated_at') THEN
    CREATE TRIGGER trg_bid_comments_updated_at
      BEFORE UPDATE ON bid_comments
      FOR EACH ROW EXECUTE FUNCTION set_updated_at();
  END IF;
END $$;
