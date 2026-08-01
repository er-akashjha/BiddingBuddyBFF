-- 0036_add_grant_applications
-- The grant pursuit pipeline: an org's applications plus their activity feed and plan checklist.
-- The grants analog of `bids` (+ bid_activities / bid_checklist_items), with a GRANT stage
-- vocabulary. Org-scoped. `mongo_grant_id` is the (nullable) link back to the source opportunity;
-- a manually-created application has none. Snapshot fields (title/agency/…) are copied from the
-- grant at create time so the pipeline survives the opportunity being archived or re-enriched.
--
-- Idempotent throughout.

CREATE TABLE IF NOT EXISTS grant_applications (
  id                 UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  org_id             UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,

  -- Link to the source opportunity (Mongo _id). NULL for a manually-started application.
  mongo_grant_id     TEXT,

  -- Snapshot of the opportunity at create time.
  title              TEXT NOT NULL,
  agency_name        TEXT,
  opportunity_number TEXT,

  stage              TEXT NOT NULL DEFAULT 'Qualifying',
  -- Generated rollup mirroring bids.status_category — Awarded/Declined/Dropped are terminal.
  status_category    TEXT GENERATED ALWAYS AS (
                       CASE WHEN stage IN ('Awarded', 'Declined', 'Dropped') THEN 'closed' ELSE 'open' END
                     ) STORED,

  amount_requested   NUMERIC(15,2),
  deadline           DATE,
  assigned_to        UUID REFERENCES users(id) ON DELETE SET NULL,

  -- Proposal-readiness 0..100, recomputed by the service from checklist / narrative / budget / reviews.
  readiness          INTEGER NOT NULL DEFAULT 0,
  cost_share_pct     NUMERIC(5,2),
  tribal_set_aside   BOOLEAN NOT NULL DEFAULT FALSE,
  tags               TEXT[],
  source_url         TEXT,
  notes              TEXT,

  created_by         UUID REFERENCES users(id) ON DELETE SET NULL,
  created_at         TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at         TIMESTAMPTZ NOT NULL DEFAULT NOW(),

  CONSTRAINT ck_grant_applications_stage CHECK (stage IN
    ('Qualifying', 'Planning', 'Writing', 'Review', 'Submitted', 'Awarded', 'Declined', 'Dropped')),
  CONSTRAINT ck_grant_applications_readiness CHECK (readiness BETWEEN 0 AND 100)
);

CREATE INDEX IF NOT EXISTS idx_grant_applications_org_created
  ON grant_applications (org_id, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_grant_applications_org_stage
  ON grant_applications (org_id, stage);
CREATE INDEX IF NOT EXISTS idx_grant_applications_assigned
  ON grant_applications (assigned_to);

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'trg_grant_applications_updated_at') THEN
    CREATE TRIGGER trg_grant_applications_updated_at
      BEFORE UPDATE ON grant_applications
      FOR EACH ROW EXECUTE FUNCTION set_updated_at();
  END IF;
END $$;

-- ── Activity feed ─────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS grant_application_activities (
  id             UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  application_id UUID NOT NULL REFERENCES grant_applications(id) ON DELETE CASCADE,
  org_id         UUID NOT NULL,
  actor_id       UUID REFERENCES users(id) ON DELETE SET NULL,
  action         TEXT NOT NULL,        -- created | stage_change | updated | note | review | submission
  from_value     TEXT,
  to_value       TEXT,
  note           TEXT,
  created_at     TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_grant_app_activities_app
  ON grant_application_activities (application_id, created_at DESC);

-- ── Plan checklist (the detail-page "Plan" tab) ───────────────────────────────
CREATE TABLE IF NOT EXISTS grant_application_checklist_items (
  id             UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  application_id UUID NOT NULL REFERENCES grant_applications(id) ON DELETE CASCADE,
  org_id         UUID NOT NULL,
  title          TEXT NOT NULL,
  is_done        BOOLEAN NOT NULL DEFAULT FALSE,
  due_date       DATE,
  assigned_to    UUID REFERENCES users(id) ON DELETE SET NULL,
  sort_order     INTEGER NOT NULL DEFAULT 0,
  done_at        TIMESTAMPTZ,
  done_by        UUID REFERENCES users(id) ON DELETE SET NULL,
  created_at     TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_grant_app_checklist_app
  ON grant_application_checklist_items (application_id, sort_order);
