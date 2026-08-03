-- 0037_add_grant_proposal_artifacts
-- Proposal-authoring artifacts hung off a grant application: narrative sections and budget line
-- items (the detail-page Narrative + Budget tabs). Both cascade-delete with their application and
-- carry their own org_id for scoping. Idempotent.

CREATE TABLE IF NOT EXISTS grant_narrative_sections (
  id             UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  application_id UUID NOT NULL REFERENCES grant_applications(id) ON DELETE CASCADE,
  org_id         UUID NOT NULL,
  section_key    TEXT NOT NULL,          -- stable key, e.g. 'need_statement'
  title          TEXT NOT NULL,
  content        TEXT,
  word_count     INTEGER NOT NULL DEFAULT 0,
  target_words   INTEGER,
  status         TEXT NOT NULL DEFAULT 'not_started',
  sort_order     INTEGER NOT NULL DEFAULT 0,
  updated_by     UUID REFERENCES users(id) ON DELETE SET NULL,
  created_at     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT ck_grant_narrative_status CHECK (status IN ('not_started', 'drafting', 'complete'))
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_grant_narrative_app_section
  ON grant_narrative_sections (application_id, section_key);
CREATE INDEX IF NOT EXISTS idx_grant_narrative_app
  ON grant_narrative_sections (application_id, sort_order);

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'trg_grant_narrative_updated_at') THEN
    CREATE TRIGGER trg_grant_narrative_updated_at
      BEFORE UPDATE ON grant_narrative_sections
      FOR EACH ROW EXECUTE FUNCTION set_updated_at();
  END IF;
END $$;

CREATE TABLE IF NOT EXISTS grant_budget_line_items (
  id             UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  application_id UUID NOT NULL REFERENCES grant_applications(id) ON DELETE CASCADE,
  org_id         UUID NOT NULL,
  category       TEXT NOT NULL,
  description    TEXT NOT NULL,
  amount         NUMERIC(15,2) NOT NULL DEFAULT 0,
  sort_order     INTEGER NOT NULL DEFAULT 0,
  created_at     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT ck_grant_budget_category CHECK (category IN
    ('personnel', 'fringe', 'travel', 'equipment', 'supplies', 'contractual', 'indirect', 'other'))
);

CREATE INDEX IF NOT EXISTS idx_grant_budget_app
  ON grant_budget_line_items (application_id, sort_order);

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'trg_grant_budget_updated_at') THEN
    CREATE TRIGGER trg_grant_budget_updated_at
      BEFORE UPDATE ON grant_budget_line_items
      FOR EACH ROW EXECUTE FUNCTION set_updated_at();
  END IF;
END $$;
