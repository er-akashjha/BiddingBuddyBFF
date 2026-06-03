-- BiddingBuddy SaaS — PostgreSQL Schema
-- Run: psql -U postgres -d bidding_buddy -f schema.sql
-- Requires PostgreSQL 14+

CREATE EXTENSION IF NOT EXISTS "pgcrypto";

-- ─────────────────────────────────────────────────────────────────
-- updated_at trigger (reused by all tables)
-- ─────────────────────────────────────────────────────────────────
CREATE OR REPLACE FUNCTION set_updated_at()
RETURNS TRIGGER AS $$
BEGIN
  NEW.updated_at = NOW();
  RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- ─────────────────────────────────────────────────────────────────
-- 1. AUTH & IDENTITY
-- ─────────────────────────────────────────────────────────────────

CREATE TABLE users (
  id             UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  email          TEXT NOT NULL UNIQUE,
  name           TEXT NOT NULL,
  avatar_url     TEXT,
  phone          TEXT,
  password_hash  TEXT,
  is_active      BOOLEAN NOT NULL DEFAULT true,
  last_login_at  TIMESTAMPTZ,
  created_at     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at     TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TRIGGER trg_users_updated_at
  BEFORE UPDATE ON users
  FOR EACH ROW EXECUTE FUNCTION set_updated_at();

-- one row per connected social provider per user
CREATE TABLE oauth_accounts (
  id                UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id           UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  provider          TEXT NOT NULL,            -- 'google' | 'github' | 'microsoft' | 'linkedin'
  provider_user_id  TEXT NOT NULL,
  email             TEXT,
  access_token      TEXT,
  refresh_token     TEXT,
  token_expires_at  TIMESTAMPTZ,
  raw_profile       JSONB,
  created_at        TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at        TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE (provider, provider_user_id)
);

CREATE INDEX idx_oauth_accounts_user_id ON oauth_accounts (user_id);

CREATE TRIGGER trg_oauth_accounts_updated_at
  BEFORE UPDATE ON oauth_accounts
  FOR EACH ROW EXECUTE FUNCTION set_updated_at();

-- hashed refresh tokens; server-side revocation support
CREATE TABLE refresh_tokens (
  id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id     UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  token_hash  TEXT NOT NULL UNIQUE,
  expires_at  TIMESTAMPTZ NOT NULL,
  revoked_at  TIMESTAMPTZ,
  created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_refresh_tokens_user_id ON refresh_tokens (user_id);
CREATE INDEX idx_refresh_tokens_hash    ON refresh_tokens (token_hash);

-- ─────────────────────────────────────────────────────────────────
-- 2. ORGANIZATIONS & MEMBERS
-- ─────────────────────────────────────────────────────────────────

CREATE TABLE organizations (
  id                   UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  owned_by             UUID NOT NULL REFERENCES users(id),
  name                 TEXT NOT NULL,
  slug                 TEXT UNIQUE,
  gstin                TEXT,
  pan                  TEXT,
  industry             TEXT,
  company_size         TEXT CHECK (company_size IN ('solo','small','medium','large')),
  registered_address   TEXT,
  city                 TEXT,
  state                TEXT,
  pincode              TEXT,
  website              TEXT,
  gem_seller_id        TEXT,
  primary_category     TEXT,
  logo_url             TEXT,
  is_active            BOOLEAN NOT NULL DEFAULT true,
  created_at           TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at           TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_organizations_owned_by ON organizations (owned_by);

CREATE TRIGGER trg_organizations_updated_at
  BEFORE UPDATE ON organizations
  FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TABLE org_members (
  id           UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  org_id       UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
  user_id      UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  role         TEXT NOT NULL DEFAULT 'viewer'
                 CHECK (role IN ('owner','admin','bid_manager','finance','sales','viewer')),
  department   TEXT,
  status       TEXT NOT NULL DEFAULT 'active' CHECK (status IN ('active','suspended')),
  invited_by   UUID REFERENCES users(id),
  joined_at    TIMESTAMPTZ DEFAULT NOW(),
  created_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE (org_id, user_id)
);

CREATE INDEX idx_org_members_user_id ON org_members (user_id);
CREATE INDEX idx_org_members_org_id  ON org_members (org_id);

-- ─────────────────────────────────────────────────────────────────
-- 3. TENDERS  (pipeline-owned, no org_id)
-- ─────────────────────────────────────────────────────────────────

CREATE TABLE tenders (
  id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  gem_tender_id       TEXT NOT NULL UNIQUE,
  title               TEXT NOT NULL,
  description         TEXT,
  buyer_org_name      TEXT,
  buyer_org_id_gem    TEXT,
  state               TEXT,
  city                TEXT,
  category            TEXT,
  sub_category        TEXT,
  tender_value        NUMERIC(15,2),
  emd_amount          NUMERIC(15,2),
  published_date      DATE,
  closing_date        DATE,
  delivery_days       INT,
  status              TEXT NOT NULL DEFAULT 'active'
                        CHECK (status IN ('active','closed','cancelled','awarded')),
  corrigendum_count   INT NOT NULL DEFAULT 0,
  ai_score            INT,
  eligibility_score   INT,
  win_probability     NUMERIC(5,2),
  risk_score          INT,
  ai_summary          TEXT,
  ai_tags             TEXT[],
  raw_data            JSONB,
  source              TEXT NOT NULL DEFAULT 'gem_pipeline',
  created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_tenders_gem_id      ON tenders (gem_tender_id);
CREATE INDEX idx_tenders_closing     ON tenders (closing_date);
CREATE INDEX idx_tenders_status      ON tenders (status);
CREATE INDEX idx_tenders_category    ON tenders (category);
CREATE INDEX idx_tenders_state       ON tenders (state);
CREATE INDEX idx_tenders_ai_score    ON tenders (ai_score DESC);

CREATE TRIGGER trg_tenders_updated_at
  BEFORE UPDATE ON tenders
  FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TABLE tender_documents (
  id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  tender_id     UUID NOT NULL REFERENCES tenders(id) ON DELETE CASCADE,
  file_name     TEXT NOT NULL,
  s3_key        TEXT NOT NULL,
  document_type TEXT,
  file_size_kb  INT,
  extracted_text TEXT,
  created_at    TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_tender_docs_tender_id ON tender_documents (tender_id);

-- per-org overlay: tracked/saved state for a tender
CREATE TABLE org_tender_settings (
  org_id          UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
  tender_id       UUID NOT NULL REFERENCES tenders(id) ON DELETE CASCADE,
  is_tracked      BOOLEAN NOT NULL DEFAULT false,
  is_saved        BOOLEAN NOT NULL DEFAULT false,
  custom_score    INT,
  notes           TEXT,
  tags            TEXT[],
  added_by        UUID REFERENCES users(id),
  created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  PRIMARY KEY (org_id, tender_id)
);

CREATE TRIGGER trg_org_tender_settings_updated_at
  BEFORE UPDATE ON org_tender_settings
  FOR EACH ROW EXECUTE FUNCTION set_updated_at();

-- ─────────────────────────────────────────────────────────────────
-- 4. BIDS  (Kanban pipeline)
-- ─────────────────────────────────────────────────────────────────

CREATE TABLE bids (
  id               UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  org_id           UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
  tender_id        UUID REFERENCES tenders(id),
  title            TEXT NOT NULL,
  description      TEXT,
  stage            TEXT NOT NULL DEFAULT 'identified'
                     CHECK (stage IN ('identified','reviewing','preparing','approval','submitted','won','lost')),
  priority         TEXT NOT NULL DEFAULT 'medium'
                     CHECK (priority IN ('low','medium','high','critical')),
  assigned_to      UUID REFERENCES users(id),
  created_by       UUID NOT NULL REFERENCES users(id),
  due_date         DATE,
  tender_value     NUMERIC(15,2),
  our_bid_value    NUMERIC(15,2),
  win_probability  NUMERIC(5,2),
  progress_pct     INT NOT NULL DEFAULT 0,
  loss_reason      TEXT,
  won_value        NUMERIC(15,2),
  created_at       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at       TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_bids_org_id    ON bids (org_id);
CREATE INDEX idx_bids_org_stage ON bids (org_id, stage);
CREATE INDEX idx_bids_assigned  ON bids (assigned_to);

CREATE TRIGGER trg_bids_updated_at
  BEFORE UPDATE ON bids
  FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TABLE bid_activities (
  id           UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  bid_id       UUID NOT NULL REFERENCES bids(id) ON DELETE CASCADE,
  actor_id     UUID NOT NULL REFERENCES users(id),
  action       TEXT NOT NULL,
  from_value   TEXT,
  to_value     TEXT,
  note         TEXT,
  created_at   TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_bid_activities_bid_id ON bid_activities (bid_id);

CREATE TABLE bid_checklist_items (
  id           UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  bid_id       UUID NOT NULL REFERENCES bids(id) ON DELETE CASCADE,
  org_id       UUID NOT NULL REFERENCES organizations(id),
  title        TEXT NOT NULL,
  is_done      BOOLEAN NOT NULL DEFAULT false,
  due_date     DATE,
  assigned_to  UUID REFERENCES users(id),
  done_at      TIMESTAMPTZ,
  done_by      UUID REFERENCES users(id),
  sort_order   INT NOT NULL DEFAULT 0,
  created_at   TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_bid_checklist_bid_id ON bid_checklist_items (bid_id);

-- ─────────────────────────────────────────────────────────────────
-- 5. COMPLIANCE
-- ─────────────────────────────────────────────────────────────────

CREATE TABLE compliance_requirements (
  id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  org_id          UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
  name            TEXT NOT NULL,
  description     TEXT,
  category        TEXT,
  is_mandatory    BOOLEAN NOT NULL DEFAULT true,
  created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_compliance_req_org ON compliance_requirements (org_id);

-- documents table defined below; FK added after
CREATE TABLE compliance_documents (
  id                UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  org_id            UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
  requirement_id    UUID NOT NULL REFERENCES compliance_requirements(id) ON DELETE CASCADE,
  document_id       UUID,
  status            TEXT NOT NULL DEFAULT 'pending'
                      CHECK (status IN ('pending','valid','expiring_soon','expired')),
  expiry_date       DATE,
  verified_by       UUID REFERENCES users(id),
  verified_at       TIMESTAMPTZ,
  notes             TEXT,
  created_at        TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at        TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_compliance_docs_org    ON compliance_documents (org_id);
CREATE INDEX idx_compliance_docs_expiry ON compliance_documents (expiry_date);

CREATE TRIGGER trg_compliance_docs_updated_at
  BEFORE UPDATE ON compliance_documents
  FOR EACH ROW EXECUTE FUNCTION set_updated_at();

-- ─────────────────────────────────────────────────────────────────
-- 6. DOCUMENT VAULT
-- ─────────────────────────────────────────────────────────────────

CREATE TABLE document_folders (
  id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  org_id      UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
  name        TEXT NOT NULL,
  parent_id   UUID REFERENCES document_folders(id),
  created_by  UUID NOT NULL REFERENCES users(id),
  created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_doc_folders_org ON document_folders (org_id);

CREATE TABLE documents (
  id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  org_id          UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
  folder_id       UUID REFERENCES document_folders(id) ON DELETE SET NULL,
  name            TEXT NOT NULL,
  file_name       TEXT NOT NULL,
  s3_key          TEXT NOT NULL,
  s3_version_id   TEXT,
  file_size_kb    INT,
  mime_type       TEXT,
  document_type   TEXT,
  expiry_date     DATE,
  tags            TEXT[],
  health_score    INT,
  uploaded_by     UUID NOT NULL REFERENCES users(id),
  created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_documents_org    ON documents (org_id);
CREATE INDEX idx_documents_expiry ON documents (expiry_date);

CREATE TRIGGER trg_documents_updated_at
  BEFORE UPDATE ON documents
  FOR EACH ROW EXECUTE FUNCTION set_updated_at();

-- add FK now that documents exists
ALTER TABLE compliance_documents
  ADD CONSTRAINT fk_compliance_documents_doc
  FOREIGN KEY (document_id) REFERENCES documents(id) ON DELETE SET NULL;

CREATE TABLE document_versions (
  id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  document_id   UUID NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
  version_num   INT NOT NULL,
  s3_key        TEXT NOT NULL,
  s3_version_id TEXT,
  file_size_kb  INT,
  uploaded_by   UUID NOT NULL REFERENCES users(id),
  created_at    TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_doc_versions_doc_id ON document_versions (document_id);

-- ─────────────────────────────────────────────────────────────────
-- 7. ORDERS & DELIVERY
-- ─────────────────────────────────────────────────────────────────

CREATE TABLE orders (
  id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  org_id          UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
  bid_id          UUID REFERENCES bids(id) ON DELETE SET NULL,
  tender_id       UUID REFERENCES tenders(id) ON DELETE SET NULL,
  gem_order_id    TEXT UNIQUE,
  order_number    TEXT,
  buyer_org       TEXT,
  order_date      DATE,
  delivery_date   DATE,
  total_value     NUMERIC(15,2),
  status          TEXT NOT NULL DEFAULT 'received'
                    CHECK (status IN ('received','processing','dispatched','delivered','cancelled')),
  created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_orders_org ON orders (org_id);

CREATE TRIGGER trg_orders_updated_at
  BEFORE UPDATE ON orders
  FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TABLE order_items (
  id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  order_id      UUID NOT NULL REFERENCES orders(id) ON DELETE CASCADE,
  org_id        UUID NOT NULL REFERENCES organizations(id),
  description   TEXT NOT NULL,
  quantity      INT NOT NULL,
  unit_price    NUMERIC(15,2) NOT NULL,
  total_price   NUMERIC(15,2) NOT NULL,
  hsn_code      TEXT,
  created_at    TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_order_items_order ON order_items (order_id);

CREATE TABLE delivery_milestones (
  id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  order_id      UUID NOT NULL REFERENCES orders(id) ON DELETE CASCADE,
  org_id        UUID NOT NULL REFERENCES organizations(id),
  title         TEXT NOT NULL,
  due_date      DATE,
  completed_at  TIMESTAMPTZ,
  notes         TEXT,
  created_at    TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_delivery_milestones_order ON delivery_milestones (order_id);

-- ─────────────────────────────────────────────────────────────────
-- 8. PAYMENTS
-- ─────────────────────────────────────────────────────────────────

CREATE TABLE emd_payments (
  id               UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  org_id           UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
  bid_id           UUID REFERENCES bids(id) ON DELETE SET NULL,
  tender_id        UUID REFERENCES tenders(id) ON DELETE SET NULL,
  gem_tender_id    TEXT,
  tender_title     TEXT,
  amount           NUMERIC(15,2) NOT NULL,
  payment_date     DATE NOT NULL,
  payment_mode     TEXT CHECK (payment_mode IN ('neft','rtgs','upi','dd','online')),
  transaction_ref  TEXT,
  bank_name        TEXT,
  status           TEXT NOT NULL DEFAULT 'held'
                     CHECK (status IN ('held','refunded','forfeited')),
  refund_date      DATE,
  refund_amount    NUMERIC(15,2),
  refund_ref       TEXT,
  notes            TEXT,
  created_at       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at       TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_emd_org    ON emd_payments (org_id);
CREATE INDEX idx_emd_status ON emd_payments (status);

CREATE TRIGGER trg_emd_payments_updated_at
  BEFORE UPDATE ON emd_payments
  FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TABLE invoices (
  id               UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  org_id           UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
  order_id         UUID REFERENCES orders(id) ON DELETE SET NULL,
  invoice_number   TEXT,
  buyer_org        TEXT,
  amount           NUMERIC(15,2) NOT NULL,
  gst_amount       NUMERIC(15,2),
  total_amount     NUMERIC(15,2),
  invoice_date     DATE NOT NULL,
  due_date         DATE,
  paid_date        DATE,
  paid_amount      NUMERIC(15,2),
  status           TEXT NOT NULL DEFAULT 'pending'
                     CHECK (status IN ('pending','paid','overdue','partial')),
  payment_ref      TEXT,
  notes            TEXT,
  created_at       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at       TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_invoices_org      ON invoices (org_id);
CREATE INDEX idx_invoices_status   ON invoices (status);
CREATE INDEX idx_invoices_due_date ON invoices (due_date);

CREATE TRIGGER trg_invoices_updated_at
  BEFORE UPDATE ON invoices
  FOR EACH ROW EXECUTE FUNCTION set_updated_at();

-- ─────────────────────────────────────────────────────────────────
-- 9. COMPETITORS  (pipeline auto-populated)
-- ─────────────────────────────────────────────────────────────────

CREATE TABLE competitors (
  id                UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  org_id            UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
  company_name      TEXT NOT NULL,
  gem_seller_id     TEXT,
  tier              TEXT CHECK (tier IN ('tier1','tier2','tier3')),
  threat_level      TEXT CHECK (threat_level IN ('high','medium','low')),
  win_rate          NUMERIC(5,2),
  total_contracts   INT NOT NULL DEFAULT 0,
  total_win_value   NUMERIC(15,2),
  avg_bid_value     NUMERIC(15,2),
  active_states     TEXT[],
  active_categories TEXT[],
  first_seen_at     DATE,
  last_seen_at      DATE,
  created_at        TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at        TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE (org_id, company_name)
);

CREATE INDEX idx_competitors_org    ON competitors (org_id);
CREATE INDEX idx_competitors_threat ON competitors (org_id, threat_level);

CREATE TRIGGER trg_competitors_updated_at
  BEFORE UPDATE ON competitors
  FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TABLE competitor_bid_observations (
  id                 UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  org_id             UUID NOT NULL REFERENCES organizations(id),
  competitor_id      UUID NOT NULL REFERENCES competitors(id) ON DELETE CASCADE,
  tender_id          UUID REFERENCES tenders(id) ON DELETE SET NULL,
  gem_tender_id      TEXT NOT NULL,
  observed_bid_value NUMERIC(15,2),
  was_winner         BOOLEAN NOT NULL DEFAULT false,
  awarded_value      NUMERIC(15,2),
  observed_date      DATE,
  raw_data           JSONB,
  created_at         TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_comp_obs_competitor ON competitor_bid_observations (competitor_id);
CREATE INDEX idx_comp_obs_org        ON competitor_bid_observations (org_id);

-- ─────────────────────────────────────────────────────────────────
-- 10. AI ANALYSIS & PERFORMANCE
-- ─────────────────────────────────────────────────────────────────

CREATE TABLE ai_analysis_results (
  id                     UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  tender_id              UUID NOT NULL REFERENCES tenders(id) ON DELETE CASCADE,
  model_used             TEXT,
  eligibility_breakdown  JSONB,
  risk_factors           JSONB,
  win_strategy           TEXT,
  suggested_bid_range    JSONB,
  required_documents     TEXT[],
  key_clauses            TEXT[],
  raw_response           TEXT,
  generated_at           TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE (tender_id)
);

CREATE TABLE org_performance_snapshots (
  id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  org_id          UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
  snapshot_date   DATE NOT NULL,
  total_bids      INT,
  bids_won        INT,
  bids_lost       INT,
  win_rate        NUMERIC(5,2),
  total_bid_value NUMERIC(15,2),
  won_value       NUMERIC(15,2),
  avg_bid_value   NUMERIC(15,2),
  top_categories  JSONB,
  top_states      JSONB,
  created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE (org_id, snapshot_date)
);

CREATE INDEX idx_perf_snapshots_org ON org_performance_snapshots (org_id);

-- ─────────────────────────────────────────────────────────────────
-- 11. NOTIFICATIONS
-- ─────────────────────────────────────────────────────────────────

CREATE TABLE notifications (
  id           UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  org_id       UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
  user_id      UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  type         TEXT NOT NULL,
  title        TEXT NOT NULL,
  body         TEXT,
  entity_type  TEXT,
  entity_id    UUID,
  is_read      BOOLEAN NOT NULL DEFAULT false,
  read_at      TIMESTAMPTZ,
  created_at   TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_notifications_user_unread ON notifications (user_id, is_read);
CREATE INDEX idx_notifications_org         ON notifications (org_id);

CREATE TABLE notification_preferences (
  user_id      UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  org_id       UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
  channel      TEXT NOT NULL DEFAULT 'in_app'
                 CHECK (channel IN ('in_app','email','whatsapp')),
  event_types  TEXT[] NOT NULL DEFAULT ARRAY['tender_closing','bid_due','emd_due'],
  is_enabled   BOOLEAN NOT NULL DEFAULT true,
  PRIMARY KEY (user_id, org_id, channel)
);

-- ─────────────────────────────────────────────────────────────────
-- 12. GEM INTEGRATION
-- ─────────────────────────────────────────────────────────────────

CREATE TABLE gem_integrations (
  id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  org_id          UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
  gem_seller_id   TEXT NOT NULL,
  gem_username    TEXT,
  sync_enabled    BOOLEAN NOT NULL DEFAULT false,
  last_synced_at  TIMESTAMPTZ,
  sync_status     TEXT DEFAULT 'idle' CHECK (sync_status IN ('idle','running','failed')),
  sync_error      TEXT,
  preferences     JSONB,
  created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE (org_id)
);

CREATE TRIGGER trg_gem_integrations_updated_at
  BEFORE UPDATE ON gem_integrations
  FOR EACH ROW EXECUTE FUNCTION set_updated_at();
