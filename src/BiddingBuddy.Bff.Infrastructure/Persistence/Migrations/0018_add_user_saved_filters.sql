-- 0018_add_user_saved_filters
-- Per-user, per-org saved tender filters:
--   • kind='last_used' — one auto-upserted snapshot of the filters the user last had
--     applied on the Tenders page (restored on their next visit).
--   • kind='named'     — explicitly saved, named views the user can re-apply.
-- The filter selection is stored as jsonb so its shape can evolve without a migration.
--
-- Idempotent: guarded objects; safe to re-run.

CREATE TABLE IF NOT EXISTS user_saved_filters (
    id         UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id    UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    org_id     UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
    kind       VARCHAR(20) NOT NULL DEFAULT 'named',
    name       VARCHAR(200) NULL,
    filters    JSONB       NOT NULL DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT ck_user_saved_filters_kind CHECK (kind IN ('last_used','named'))
);

-- Exactly one 'last_used' snapshot per (user, org).
CREATE UNIQUE INDEX IF NOT EXISTS uq_user_saved_filters_last_used
    ON user_saved_filters (user_id, org_id)
    WHERE kind = 'last_used';

CREATE INDEX IF NOT EXISTS idx_user_saved_filters_user_org
    ON user_saved_filters (user_id, org_id);

-- updated_at trigger (set_updated_at() created by the base schema)
DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'trg_user_saved_filters_updated_at') THEN
    CREATE TRIGGER trg_user_saved_filters_updated_at
      BEFORE UPDATE ON user_saved_filters
      FOR EACH ROW EXECUTE FUNCTION set_updated_at();
  END IF;
END $$;
