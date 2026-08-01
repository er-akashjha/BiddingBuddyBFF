-- 0038_add_grant_reviews_submissions
-- The last two proposal-authoring surfaces: internal reviews and the submission record
-- (the detail-page Reviews + Submission tabs). Both cascade with their application. Idempotent.

CREATE TABLE IF NOT EXISTS grant_reviews (
  id             UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  application_id UUID NOT NULL REFERENCES grant_applications(id) ON DELETE CASCADE,
  org_id         UUID NOT NULL,
  reviewer_id    UUID REFERENCES users(id) ON DELETE SET NULL,
  status         TEXT NOT NULL DEFAULT 'pending',
  comments       TEXT,
  reviewed_at    TIMESTAMPTZ,
  created_by     UUID REFERENCES users(id) ON DELETE SET NULL,
  created_at     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT ck_grant_reviews_status CHECK (status IN
    ('pending', 'in_progress', 'approved', 'changes_requested'))
);

CREATE INDEX IF NOT EXISTS idx_grant_reviews_app ON grant_reviews (application_id);

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'trg_grant_reviews_updated_at') THEN
    CREATE TRIGGER trg_grant_reviews_updated_at
      BEFORE UPDATE ON grant_reviews
      FOR EACH ROW EXECUTE FUNCTION set_updated_at();
  END IF;
END $$;

CREATE TABLE IF NOT EXISTS grant_submissions (
  id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  application_id      UUID NOT NULL REFERENCES grant_applications(id) ON DELETE CASCADE,
  org_id              UUID NOT NULL,
  portal              TEXT NOT NULL DEFAULT 'grants_gov',
  status              TEXT NOT NULL DEFAULT 'draft',
  submitted_at        TIMESTAMPTZ,
  confirmation_number TEXT,
  submitted_by        UUID REFERENCES users(id) ON DELETE SET NULL,
  amount_awarded      NUMERIC(15,2),
  notes               TEXT,
  file_manifest       JSONB,
  created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT ck_grant_submissions_portal CHECK (portal IN
    ('grants_gov', 'foundation', 'submittable', 'fluxx', 'other')),
  CONSTRAINT ck_grant_submissions_status CHECK (status IN
    ('draft', 'submitted', 'under_review', 'awarded', 'declined', 'more_info'))
);

CREATE INDEX IF NOT EXISTS idx_grant_submissions_app ON grant_submissions (application_id);

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'trg_grant_submissions_updated_at') THEN
    CREATE TRIGGER trg_grant_submissions_updated_at
      BEFORE UPDATE ON grant_submissions
      FOR EACH ROW EXECUTE FUNCTION set_updated_at();
  END IF;
END $$;
