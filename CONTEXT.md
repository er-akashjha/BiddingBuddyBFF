# BiddingBuddyBFF — Backend for Frontend

Complete reference for the SaaS API. One user → multiple organizations. PostgreSQL + ASP.NET Core 8.

---

## Architecture

```
┌──────────────────────────────────────────────────────────────────────┐
│  React SPA (app-byrywqh0vkzl.appmedo.com)                            │
│  Auth: JWT Bearer (access token 15 min / refresh 30 days)            │
│  Org context: X-Org-Id header on every org-scoped request            │
└──────────────────┬───────────────────────────────────────────────────┘
                   │ HTTPS
                   ▼
┌──────────────────────────────────────────────────────────────────────┐
│  BiddingBuddyBFF  (ASP.NET Core 8, localhost:7100 dev)               │
│                                                                      │
│  BiddingBuddy.Bff.Api          — controllers, middleware, Program    │
│  BiddingBuddy.Bff.Core         — entities, interfaces, service ifaces│
│  BiddingBuddy.Bff.Infrastructure — EF Core + Npgsql repos + services │
└──────────────────┬───────────────────────────────────────────────────┘
                   │
                   ├── PostgreSQL (bidding_buddy DB)
                   ├── AWS S3 (document vault)
                   └── GET /internal/* from BidProcessor pipeline
```

### Clean Architecture

```
Controllers (Api)
    ↓
Services (Core) — interfaces + AuthService etc.
    ↓
Repositories (Infrastructure) — EF Core + Npgsql
    ↓
PostgreSQL
```

### Solution layout

```
BiddingBuddyBFF/
├── CONTEXT.md                      ← this file
├── database/
│   └── schema.sql                  ← full DDL, run once to create DB
├── BiddingBuddyBFF.sln
└── src/
    ├── BiddingBuddy.Bff.Api/
    │   ├── Controllers/
    │   │   ├── AuthController.cs
    │   │   ├── OrganizationsController.cs
    │   │   ├── TendersController.cs
    │   │   ├── BidsController.cs
    │   │   ├── ComplianceController.cs
    │   │   ├── DocumentsController.cs
    │   │   ├── OrdersController.cs
    │   │   ├── PaymentsController.cs
    │   │   ├── CompetitorsController.cs
    │   │   ├── AnalysisController.cs
    │   │   ├── NotificationsController.cs
    │   │   ├── IntegrationsController.cs
    │   │   └── InternalController.cs        ← pipeline ingestion
    │   ├── Middleware/
    │   │   └── OrgContextMiddleware.cs      ← validates X-Org-Id
    │   ├── Program.cs
    │   └── appsettings.json
    ├── BiddingBuddy.Bff.Core/
    │   ├── Entities/                        ← 28 EF Core entities
    │   ├── DTOs/                            ← request/response shapes
    │   ├── Interfaces/                      ← repo + service contracts
    │   └── Extensions/
    │       └── CoreServiceExtensions.cs
    └── BiddingBuddy.Bff.Infrastructure/
        ├── Persistence/
        │   ├── BffDbContext.cs
        │   └── Configurations/              ← one IEntityTypeConfiguration<T> per entity
        ├── Repositories/
        ├── Services/
        │   ├── TokenService.cs
        │   ├── AuthService.cs
        │   └── OAuthProviderService.cs
        └── Extensions/
            └── InfrastructureServiceExtensions.cs
```

---

## Configuration (appsettings.json)

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=bidding_buddy;Username=postgres;Password=postgres"
  },
  "Jwt": {
    "Secret": "CHANGE_ME_32plus_chars_secret_key_here!!!",
    "Issuer": "BiddingBuddyBFF",
    "Audience": "BiddingBuddyClients",
    "AccessTokenExpiryMinutes": 15,
    "RefreshTokenExpiryDays": 30
  },
  "OAuth": {
    "Google": {
      "ClientId": "YOUR_GOOGLE_CLIENT_ID",
      "ClientSecret": "YOUR_GOOGLE_CLIENT_SECRET"
    },
    "GitHub": {
      "ClientId": "YOUR_GITHUB_CLIENT_ID",
      "ClientSecret": "YOUR_GITHUB_CLIENT_SECRET"
    }
  },
  "Frontend": {
    "BaseUrl": "http://localhost:3000",
    "AuthCallbackPath": "/auth/callback"
  },
  "Pipeline": {
    "ApiKey": "pipeline_internal_secret"
  }
}
```

---

## Auth Design

Two authentication paths are supported. Both produce the same `TokenResponseDto` and feed into the same JWT-based session.

| Path | How |
|---|---|
| **Email/password** | `POST /api/auth/register` (new account) or `POST /api/auth/login` (existing). Password stored as BCrypt hash (`password_hash` on `users`). |
| **OAuth (Google / GitHub)** | Browser redirected to provider via `GET /api/auth/oauth/{provider}`. BFF handles callback, upserts user, mints tokens. |

### Email / Password Registration Flow

```
POST /api/auth/register  { name, email, password, orgName, phone? }
  1. Reject if email already exists → 409
  2. Reject if password < 8 chars   → 400
  3. BCrypt.HashPassword(password)  stored in users.password_hash
  4. INSERT users row
  5. INSERT organizations row (owned_by = user.id, name = orgName)
  6. INSERT org_members row (role = "owner")
  7. Mint JWT + refresh token        → 201 TokenResponseDto
```

### Email / Password Login Flow

```
POST /api/auth/login  { email, password }
  1. Lookup users WHERE email = $email
  2. Fail if not found OR password_hash IS NULL  → 401
  3. BCrypt.Verify(password, password_hash)
  4. Fail if mismatch                            → 401
  5. Update last_login_at
  6. Mint JWT + refresh token                    → 200 TokenResponseDto
```

### Social Login Flow

```
Browser                      BFF                       Google / GitHub
  │                           │                              │
  │ GET /api/auth/oauth/google │                              │
  │ ?returnUrl=/dashboard      │                              │
  │──────────────────────────▶│                              │
  │                           │ build state JWT              │
  │◀── 302 to Google ─────────│ (contains returnUrl+nonce)  │
  │──────────────────────────────────────── user consents ──▶│
  │◀──────────────────── callback?code=...&state=... ────────│
  │                           │◀────────────────────────────│
  │                           │ 1. validate state JWT        │
  │                           │ 2. POST to token endpoint    │
  │                           │ 3. GET userinfo              │
  │                           │ 4. upsert users row          │
  │                           │ 5. upsert oauth_accounts row │
  │                           │ 6. mint access JWT (15 min)  │
  │                           │ 7. store refresh token hash  │
  │◀── 302 {frontend}/auth/callback?access_token=...&refresh_token=... ──│
```

### User upsert logic

```
Find oauth_accounts WHERE provider=$p AND provider_user_id=$external_id
  → FOUND: load user, update tokens
  → NOT FOUND:
      Find users WHERE email=$provider_email
        → FOUND: link new oauth_accounts row to existing user
        → NOT FOUND: INSERT users (name, email, avatar from provider profile)
                     INSERT oauth_accounts
Mint JWT with { sub: user.id, email, name, iat, exp }
```

### JWT Claims

```json
{ "sub": "uuid", "email": "user@example.com", "name": "Ravi Kumar", "jti": "uuid", "iat": 0, "exp": 0 }
```

### Org context

Every org-scoped API call requires `X-Org-Id: <org_uuid>` header.
`OrgContextMiddleware` validates:
1. Header is present
2. User is a member of that org (cached in-memory per request)
3. Injects `HttpContext.Items["OrgId"]` for controllers to use

---

## Database Schema — 28 Tables

### 1. Auth & Identity

```sql
CREATE EXTENSION IF NOT EXISTS "pgcrypto";

CREATE TABLE users (
  id             UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  email          TEXT NOT NULL UNIQUE,
  name           TEXT NOT NULL,
  avatar_url     TEXT,
  phone          TEXT,
  password_hash  TEXT,          -- NULL for OAuth-only accounts; BCrypt hash for email/password accounts
  is_active      BOOLEAN NOT NULL DEFAULT true,
  last_login_at  TIMESTAMPTZ,
  created_at     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at     TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

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

CREATE TABLE refresh_tokens (
  id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id     UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  token_hash  TEXT NOT NULL UNIQUE,
  expires_at  TIMESTAMPTZ NOT NULL,
  revoked_at  TIMESTAMPTZ,
  created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX idx_refresh_tokens_user_id ON refresh_tokens (user_id);
CREATE INDEX idx_refresh_tokens_hash ON refresh_tokens (token_hash);
```

### 2. Organizations

```sql
CREATE TABLE organizations (
  id                   UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  owned_by             UUID NOT NULL REFERENCES users(id),
  name                 TEXT NOT NULL,
  slug                 TEXT UNIQUE,
  gstin                TEXT,
  pan                  TEXT,
  industry             TEXT,
  company_size         TEXT,
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

CREATE TABLE org_members (
  id           UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  org_id       UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
  user_id      UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  role         TEXT NOT NULL DEFAULT 'viewer',
  department   TEXT,
  status       TEXT NOT NULL DEFAULT 'active',
  invited_by   UUID REFERENCES users(id),
  joined_at    TIMESTAMPTZ DEFAULT NOW(),
  created_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE (org_id, user_id)
);
CREATE INDEX idx_org_members_user_id ON org_members (user_id);
```

### 3. Tenders (pipeline-owned, global)

```sql
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
  status              TEXT NOT NULL DEFAULT 'active',
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
CREATE INDEX idx_tenders_gem_id ON tenders (gem_tender_id);
CREATE INDEX idx_tenders_closing_date ON tenders (closing_date);
CREATE INDEX idx_tenders_status ON tenders (status);
CREATE INDEX idx_tenders_category ON tenders (category);
CREATE INDEX idx_tenders_state ON tenders (state);
CREATE INDEX idx_tenders_ai_score ON tenders (ai_score DESC);

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
CREATE INDEX idx_tender_documents_tender_id ON tender_documents (tender_id);

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
```

### 4. Bids

```sql
CREATE TABLE bids (
  id               UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  org_id           UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
  tender_id        UUID REFERENCES tenders(id),
  title            TEXT NOT NULL,
  description      TEXT,
  stage            TEXT NOT NULL DEFAULT 'identified',
  priority         TEXT NOT NULL DEFAULT 'medium',
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
CREATE INDEX idx_bids_org_id ON bids (org_id);
CREATE INDEX idx_bids_org_stage ON bids (org_id, stage);
CREATE INDEX idx_bids_assigned_to ON bids (assigned_to);

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
```

### 5. Compliance

```sql
CREATE TABLE compliance_requirements (
  id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  org_id          UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
  name            TEXT NOT NULL,
  description     TEXT,
  category        TEXT,
  is_mandatory    BOOLEAN NOT NULL DEFAULT true,
  created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX idx_compliance_req_org_id ON compliance_requirements (org_id);

CREATE TABLE compliance_documents (
  id                UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  org_id            UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
  requirement_id    UUID NOT NULL REFERENCES compliance_requirements(id),
  document_id       UUID,                          -- FK to documents (added after)
  status            TEXT NOT NULL DEFAULT 'pending',
  expiry_date       DATE,
  verified_by       UUID REFERENCES users(id),
  verified_at       TIMESTAMPTZ,
  notes             TEXT,
  created_at        TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at        TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX idx_compliance_docs_org_id ON compliance_documents (org_id);
CREATE INDEX idx_compliance_docs_expiry ON compliance_documents (expiry_date);
```

### 6. Document Vault

```sql
CREATE TABLE document_folders (
  id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  org_id      UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
  name        TEXT NOT NULL,
  parent_id   UUID REFERENCES document_folders(id),
  created_by  UUID NOT NULL REFERENCES users(id),
  created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX idx_document_folders_org_id ON document_folders (org_id);

CREATE TABLE documents (
  id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  org_id          UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
  folder_id       UUID REFERENCES document_folders(id),
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
CREATE INDEX idx_documents_org_id ON documents (org_id);
CREATE INDEX idx_documents_expiry ON documents (expiry_date);

-- Add FK from compliance_documents now that documents table exists
ALTER TABLE compliance_documents
  ADD CONSTRAINT fk_compliance_documents_document
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
CREATE INDEX idx_document_versions_document_id ON document_versions (document_id);
```

### 7. Orders & Delivery

```sql
CREATE TABLE orders (
  id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  org_id          UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
  bid_id          UUID REFERENCES bids(id),
  tender_id       UUID REFERENCES tenders(id),
  gem_order_id    TEXT UNIQUE,
  order_number    TEXT,
  buyer_org       TEXT,
  order_date      DATE,
  delivery_date   DATE,
  total_value     NUMERIC(15,2),
  status          TEXT NOT NULL DEFAULT 'received',
  created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX idx_orders_org_id ON orders (org_id);

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
CREATE INDEX idx_order_items_order_id ON order_items (order_id);

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
CREATE INDEX idx_delivery_milestones_order_id ON delivery_milestones (order_id);
```

### 8. Payments

```sql
CREATE TABLE emd_payments (
  id               UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  org_id           UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
  bid_id           UUID REFERENCES bids(id),
  tender_id        UUID REFERENCES tenders(id),
  gem_tender_id    TEXT,
  tender_title     TEXT,
  amount           NUMERIC(15,2) NOT NULL,
  payment_date     DATE NOT NULL,
  payment_mode     TEXT,
  transaction_ref  TEXT,
  bank_name        TEXT,
  status           TEXT NOT NULL DEFAULT 'held',
  refund_date      DATE,
  refund_amount    NUMERIC(15,2),
  refund_ref       TEXT,
  notes            TEXT,
  created_at       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at       TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX idx_emd_payments_org_id ON emd_payments (org_id);
CREATE INDEX idx_emd_payments_status ON emd_payments (status);

CREATE TABLE invoices (
  id               UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  org_id           UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
  order_id         UUID REFERENCES orders(id),
  invoice_number   TEXT,
  buyer_org        TEXT,
  amount           NUMERIC(15,2) NOT NULL,
  gst_amount       NUMERIC(15,2),
  total_amount     NUMERIC(15,2),
  invoice_date     DATE NOT NULL,
  due_date         DATE,
  paid_date        DATE,
  paid_amount      NUMERIC(15,2),
  status           TEXT NOT NULL DEFAULT 'pending',
  payment_ref      TEXT,
  notes            TEXT,
  created_at       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at       TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX idx_invoices_org_id ON invoices (org_id);
CREATE INDEX idx_invoices_status ON invoices (status);
CREATE INDEX idx_invoices_due_date ON invoices (due_date);
```

### 9. Competitors (pipeline auto-populated)

```sql
CREATE TABLE competitors (
  id                UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  org_id            UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
  company_name      TEXT NOT NULL,
  gem_seller_id     TEXT,
  tier              TEXT,
  threat_level      TEXT,
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
CREATE INDEX idx_competitors_org_id ON competitors (org_id);
CREATE INDEX idx_competitors_threat ON competitors (org_id, threat_level);

CREATE TABLE competitor_bid_observations (
  id                 UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  org_id             UUID NOT NULL REFERENCES organizations(id),
  competitor_id      UUID NOT NULL REFERENCES competitors(id) ON DELETE CASCADE,
  tender_id          UUID REFERENCES tenders(id),
  gem_tender_id      TEXT NOT NULL,
  observed_bid_value NUMERIC(15,2),
  was_winner         BOOLEAN NOT NULL DEFAULT false,
  awarded_value      NUMERIC(15,2),
  observed_date      DATE,
  raw_data           JSONB,
  created_at         TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX idx_competitor_obs_competitor_id ON competitor_bid_observations (competitor_id);
CREATE INDEX idx_competitor_obs_org_id ON competitor_bid_observations (org_id);
```

### 10. AI Analysis & Performance

```sql
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
CREATE INDEX idx_org_perf_snapshots_org_id ON org_performance_snapshots (org_id);
```

### 11. Notifications

```sql
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
CREATE INDEX idx_notifications_org_id ON notifications (org_id);

CREATE TABLE notification_preferences (
  user_id      UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  org_id       UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
  channel      TEXT NOT NULL DEFAULT 'in_app',
  event_types  TEXT[] NOT NULL DEFAULT ARRAY['tender_closing','bid_due','emd_due'],
  is_enabled   BOOLEAN NOT NULL DEFAULT true,
  PRIMARY KEY (user_id, org_id, channel)
);
```

### 12. GeM Integration

```sql
CREATE TABLE gem_integrations (
  id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  org_id          UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
  gem_seller_id   TEXT NOT NULL,
  gem_username    TEXT,
  sync_enabled    BOOLEAN NOT NULL DEFAULT false,
  last_synced_at  TIMESTAMPTZ,
  sync_status     TEXT DEFAULT 'idle',
  sync_error      TEXT,
  preferences     JSONB,
  created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE (org_id)
);
```

### updated_at trigger (apply to all tables with updated_at)

```sql
CREATE OR REPLACE FUNCTION set_updated_at()
RETURNS TRIGGER AS $$
BEGIN
  NEW.updated_at = NOW();
  RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Apply to each table that has updated_at:
CREATE TRIGGER trg_users_updated_at BEFORE UPDATE ON users
  FOR EACH ROW EXECUTE FUNCTION set_updated_at();
CREATE TRIGGER trg_oauth_accounts_updated_at BEFORE UPDATE ON oauth_accounts
  FOR EACH ROW EXECUTE FUNCTION set_updated_at();
CREATE TRIGGER trg_organizations_updated_at BEFORE UPDATE ON organizations
  FOR EACH ROW EXECUTE FUNCTION set_updated_at();
CREATE TRIGGER trg_tenders_updated_at BEFORE UPDATE ON tenders
  FOR EACH ROW EXECUTE FUNCTION set_updated_at();
CREATE TRIGGER trg_org_tender_settings_updated_at BEFORE UPDATE ON org_tender_settings
  FOR EACH ROW EXECUTE FUNCTION set_updated_at();
CREATE TRIGGER trg_bids_updated_at BEFORE UPDATE ON bids
  FOR EACH ROW EXECUTE FUNCTION set_updated_at();
CREATE TRIGGER trg_compliance_documents_updated_at BEFORE UPDATE ON compliance_documents
  FOR EACH ROW EXECUTE FUNCTION set_updated_at();
CREATE TRIGGER trg_documents_updated_at BEFORE UPDATE ON documents
  FOR EACH ROW EXECUTE FUNCTION set_updated_at();
CREATE TRIGGER trg_orders_updated_at BEFORE UPDATE ON orders
  FOR EACH ROW EXECUTE FUNCTION set_updated_at();
CREATE TRIGGER trg_emd_payments_updated_at BEFORE UPDATE ON emd_payments
  FOR EACH ROW EXECUTE FUNCTION set_updated_at();
CREATE TRIGGER trg_invoices_updated_at BEFORE UPDATE ON invoices
  FOR EACH ROW EXECUTE FUNCTION set_updated_at();
CREATE TRIGGER trg_competitors_updated_at BEFORE UPDATE ON competitors
  FOR EACH ROW EXECUTE FUNCTION set_updated_at();
CREATE TRIGGER trg_gem_integrations_updated_at BEFORE UPDATE ON gem_integrations
  FOR EACH ROW EXECUTE FUNCTION set_updated_at();
```

---

## API Reference

**Base URL**: `/api`
**Auth**: `Authorization: Bearer <access_token>`
**Org context**: `X-Org-Id: <org_uuid>` (required on all org-scoped endpoints)

---

### Auth — `/api/auth`

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| `POST` | `/auth/register` | None | Create account with email + password; also creates org + sets owner role |
| `POST` | `/auth/login` | None | Email/password sign-in |
| `GET`  | `/auth/oauth/{provider}` | None | Redirect to OAuth provider. Providers: `google`, `github`. Query: `?returnUrl=/dashboard` |
| `GET`  | `/auth/oauth/{provider}/callback` | None | OAuth callback — handled server-side, redirects browser to frontend |
| `POST` | `/auth/refresh` | None | Rotate refresh token, get new access token |
| `POST` | `/auth/logout` | Bearer | Revoke refresh token |
| `GET`  | `/auth/me` | Bearer | Current user + org memberships |
| `PATCH`| `/auth/me` | Bearer | Update name, phone |
| `GET`  | `/auth/me/providers` | Bearer | List connected OAuth providers |
| `DELETE`|`/auth/me/providers/{provider}` | Bearer | Unlink OAuth provider |

**POST /auth/register**
```
Request:
{
  "name": "Rajesh Kumar",
  "email": "rajesh@acme.in",
  "password": "Str0ng!Pass",       -- min 8 chars
  "orgName": "Acme Technologies Pvt. Ltd.",
  "phone": "+91 98765 43210"        -- optional
}

Response 201:  { "accessToken": "jwt...", "refreshToken": "opaque...", "expiresIn": 900 }
Response 409:  { "error": "Email already registered." }
Response 400:  { "error": "Password must be at least 8 characters." }
```

**POST /auth/login**
```
Request:   { "email": "rajesh@acme.in", "password": "Str0ng!Pass" }
Response 200:  { "accessToken": "jwt...", "refreshToken": "opaque...", "expiresIn": 900 }
Response 401:  { "error": "Invalid email or password." }
```

**POST /auth/refresh**
```
Request:  { "refresh_token": "opaque-string" }
Response: { "access_token": "jwt...", "refresh_token": "new-opaque-string", "expires_in": 900 }
```

**GET /auth/me**
```json
{
  "id": "uuid",
  "email": "ravi@example.com",
  "name": "Ravi Kumar",
  "avatar_url": "https://lh3.googleusercontent.com/...",
  "phone": null,
  "organizations": [
    {
      "id": "uuid",
      "name": "Acme Supplies Pvt Ltd",
      "slug": "acme-supplies",
      "role": "owner",
      "logo_url": null,
      "is_active": true,
      "primary_category": "Computers & IT Hardware"
    }
  ],
  "connected_providers": ["google"]
}
```

**PATCH /auth/me**
```
Request:  { "name": "Ravi Kumar", "phone": "+91-9876543210" }
Response: UserDto (same as GET /auth/me)
```

---

### Organizations — `/api/organizations` (all require `X-Org-Id` except create/list)

| Method | Path | Description |
|--------|------|-------------|
| `GET`    | `/organizations` | List all orgs user owns or is member of |
| `POST`   | `/organizations` | Create org (no X-Org-Id needed) |
| `GET`    | `/organizations/{id}` | Org detail |
| `PATCH`  | `/organizations/{id}` | Update org profile |
| `DELETE` | `/organizations/{id}` | Soft-deactivate org |
| `GET`    | `/organizations/{id}/members` | List team members |
| `POST`   | `/organizations/{id}/members` | Add member by email |
| `PATCH`  | `/organizations/{id}/members/{userId}` | Change role |
| `DELETE` | `/organizations/{id}/members/{userId}` | Remove member |
| `GET`    | `/organizations/{id}/stats` | Dashboard header stats |

**POST /organizations — Request**
```json
{
  "name": "Acme Supplies Pvt Ltd",
  "gstin": "29AAACR5055K1Z5",
  "pan": "AAACR5055K",
  "industry": "Electronics",
  "company_size": "small",
  "gem_seller_id": "GEM-SELLER-12345",
  // Must be a canonical taxonomy label (bidding-buddy-ui/src/lib/tenderTaxonomy.ts,
  // BiddingBuddyServices TenderTaxonomy.cs). Creating an org with a sector seeds a
  // tender_alert_rules row from it, and category matching is exact — an invented
  // label like "IT Hardware" would match zero tenders, silently and forever.
  "primary_category": "Computers & IT Hardware",
  "registered_address": "12, MG Road",
  "city": "Bengaluru",
  "state": "Karnataka",
  "pincode": "560001",
  "website": "https://acme.in"
}
```

**GET /organizations/{id}/stats — Response**
```json
{
  "active_bids": 12,
  "tenders_tracked": 45,
  "win_rate": 34.5,
  "emd_held": 250000.00,
  "overdue_invoices": 2,
  "expiring_documents": 3,
  "compliance_health": 78
}
```

---

### Tenders — `/api/tenders`

| Method | Path | Description |
|--------|------|-------------|
| `GET`    | `/tenders` | Paginated list with filters |
| `GET`    | `/tenders/{id}` | Tender detail |
| `GET`    | `/tenders/{id}/documents` | S3-backed documents |
| `GET`    | `/tenders/{id}/analysis` | AI analysis |
| `POST`   | `/tenders/{id}/track` | Mark as tracked for org |
| `DELETE` | `/tenders/{id}/track` | Untrack |
| `POST`   | `/tenders/{id}/save` | Save for org |
| `DELETE` | `/tenders/{id}/save` | Unsave |
| `PATCH`  | `/tenders/{id}/settings` | Update notes, tags, custom_score |
| `GET`    | `/tenders/saved` | Saved tenders for org |
| `GET`    | `/tenders/tracked` | Tracked tenders for org |

**GET /tenders — Query params**
```
page, limit (default 20)
status=active|closed|cancelled|awarded
category=IT Hardware
state=Karnataka
min_value=100000, max_value=5000000
closing_before=2025-08-30, closing_after=2025-07-01
min_ai_score=70
search=laptop procurement
sort=closing_date|ai_score|tender_value
order=asc|desc
```

**GET /tenders — Response**
```json
{
  "data": [{
    "id": "uuid",
    "gem_tender_id": "GEM-2024-T-12345",
    "title": "Supply of Laptops",
    "buyer_org_name": "Ministry of Education",
    "state": "Delhi",
    "category": "IT Hardware",
    "tender_value": 5000000.00,
    "emd_amount": 50000.00,
    "closing_date": "2025-08-15",
    "status": "active",
    "ai_score": 87,
    "win_probability": 72.5,
    "org_settings": { "is_tracked": true, "is_saved": false, "notes": null }
  }],
  "pagination": { "page": 1, "limit": 20, "total": 342, "pages": 18 }
}
```

**GET /tenders/{id} — Response (full)**
```json
{
  "id": "uuid",
  "gem_tender_id": "GEM-2024-T-12345",
  "title": "Supply of Laptops",
  "description": "...",
  "buyer_org_name": "Ministry of Education",
  "state": "Delhi", "city": "New Delhi",
  "category": "IT Hardware", "sub_category": "Notebooks",
  "tender_value": 5000000.00,
  "emd_amount": 50000.00,
  "published_date": "2025-07-01",
  "closing_date": "2025-08-15",
  "delivery_days": 30,
  "status": "active",
  "corrigendum_count": 1,
  "ai_score": 87, "eligibility_score": 92,
  "win_probability": 72.5, "risk_score": 18,
  "ai_summary": "High-value IT procurement...",
  "ai_tags": ["repeat_buyer", "low_competition"],
  "documents": [{ "id": "uuid", "file_name": "Bid_Document.pdf", "document_type": "bid_document", "file_size_kb": 842 }],
  "org_settings": { "is_tracked": true, "is_saved": false, "notes": null, "custom_score": null }
}
```

---

### Bids — `/api/bids`

Stages: `identified → reviewing → preparing → approval → submitted → won → lost`

| Method | Path | Description |
|--------|------|-------------|
| `GET`    | `/bids` | All bids (Kanban grouped). Query: `?stage=&assigned_to=&priority=` |
| `POST`   | `/bids` | Create bid |
| `GET`    | `/bids/{id}` | Bid detail |
| `PATCH`  | `/bids/{id}` | Update bid |
| `DELETE` | `/bids/{id}` | Delete bid |
| `PATCH`  | `/bids/{id}/stage` | Move stage |
| `GET`    | `/bids/{id}/activities` | Activity timeline |
| `POST`   | `/bids/{id}/activities` | Add note |
| `GET`    | `/bids/{id}/checklist` | Checklist |
| `POST`   | `/bids/{id}/checklist` | Add item |
| `PATCH`  | `/bids/{id}/checklist/{itemId}` | Update/complete item |
| `DELETE` | `/bids/{id}/checklist/{itemId}` | Remove item |

**GET /bids — Response**
```json
{
  "columns": {
    "identified":  { "count": 8, "total_value": 4500000, "items": [...] },
    "reviewing":   { "count": 3, "total_value": 1200000, "items": [...] },
    "preparing":   { "count": 5, "total_value": 8900000, "items": [...] },
    "approval":    { "count": 2, "total_value": 3100000, "items": [...] },
    "submitted":   { "count": 4, "total_value": 7600000, "items": [...] },
    "won":         { "count": 6, "total_value": 12000000, "items": [...] },
    "lost":        { "count": 3, "total_value": 2300000, "items": [...] }
  }
}
```

**POST /bids — Request**
```json
{
  "tender_id": "uuid",
  "title": "Laptop Supply Bid",
  "stage": "identified",
  "priority": "high",
  "assigned_to": "uuid",
  "due_date": "2025-08-10",
  "tender_value": 5000000.00,
  "our_bid_value": 4750000.00,
  "win_probability": 70.0
}
```

**PATCH /bids/{id}/stage — Request**
```json
{ "stage": "submitted", "note": "Submitted via GeM portal at 3pm" }
```

---

### Compliance — `/api/compliance`

| Method | Path | Description |
|--------|------|-------------|
| `GET`    | `/compliance/requirements` | List requirements |
| `POST`   | `/compliance/requirements` | Add requirement |
| `PATCH`  | `/compliance/requirements/{id}` | Update |
| `DELETE` | `/compliance/requirements/{id}` | Remove |
| `GET`    | `/compliance/documents` | All compliance docs with status |
| `POST`   | `/compliance/documents` | Link vault doc to requirement |
| `PATCH`  | `/compliance/documents/{id}` | Update status/expiry |
| `GET`    | `/compliance/health` | Health summary |

**GET /compliance/health — Response**
```json
{
  "health_score": 78,
  "total_requirements": 12,
  "valid": 9,
  "expiring_soon": 2,
  "expired": 1,
  "pending": 0,
  "expiring_items": [
    { "requirement": "MSME Certificate", "expiry_date": "2025-08-20", "days_left": 22 }
  ]
}
```

---

### Documents — `/api/documents`

| Method | Path | Description |
|--------|------|-------------|
| `GET`    | `/documents` | List documents. Query: `?folder_id=&document_type=&search=` |
| `POST`   | `/documents/upload-url` | Get S3 presigned PUT URL |
| `POST`   | `/documents` | Create record after S3 upload |
| `GET`    | `/documents/{id}` | Document detail |
| `PATCH`  | `/documents/{id}` | Update name, tags, expiry |
| `DELETE` | `/documents/{id}` | Soft delete |
| `GET`    | `/documents/{id}/download-url` | Presigned GET URL (15 min TTL) |
| `GET`    | `/documents/{id}/versions` | Version history |
| `POST`   | `/documents/{id}/versions` | Upload new version |
| `GET`    | `/documents/folders` | List folders |
| `POST`   | `/documents/folders` | Create folder |
| `DELETE` | `/documents/folders/{id}` | Delete folder |

**POST /documents/upload-url — Request/Response**
```json
// Request
{ "file_name": "MSME_Certificate.pdf", "mime_type": "application/pdf", "folder_id": "uuid" }
// Response
{
  "upload_url": "https://s3.amazonaws.com/bidding-buddy-dev/org-uuid/docs/2025/07/uuid-MSME_Certificate.pdf?...",
  "s3_key": "org-uuid/docs/2025/07/uuid-MSME_Certificate.pdf",
  "expires_in": 600
}
```

---

### Orders — `/api/orders`

| Method | Path | Description |
|--------|------|-------------|
| `GET`    | `/orders` | List orders. Query: `?status=&bid_id=` |
| `POST`   | `/orders` | Create order |
| `GET`    | `/orders/{id}` | Order + items + milestones |
| `PATCH`  | `/orders/{id}` | Update status |
| `POST`   | `/orders/{id}/items` | Add line item |
| `POST`   | `/orders/{id}/milestones` | Add milestone |
| `PATCH`  | `/orders/{id}/milestones/{milestoneId}` | Mark complete |

---

### Payments — `/api/payments`

| Method | Path | Description |
|--------|------|-------------|
| `GET`    | `/payments/emd` | List EMD payments. Query: `?status=held|refunded|forfeited` |
| `POST`   | `/payments/emd` | Record EMD payment |
| `PATCH`  | `/payments/emd/{id}` | Update (refund / forfeit) |
| `DELETE` | `/payments/emd/{id}` | Delete record |
| `GET`    | `/payments/invoices` | List invoices. Query: `?status=pending|paid|overdue` |
| `POST`   | `/payments/invoices` | Create invoice |
| `PATCH`  | `/payments/invoices/{id}` | Update (mark paid, etc.) |
| `DELETE` | `/payments/invoices/{id}` | Delete |
| `GET`    | `/payments/summary` | Financial summary |

**GET /payments/summary — Response**
```json
{
  "receivables": {
    "total_pending": 1250000.00,
    "total_overdue": 320000.00,
    "total_paid_this_month": 850000.00,
    "overdue_count": 3
  },
  "emd": {
    "total_held": 380000.00,
    "total_refunded_ytd": 120000.00,
    "total_forfeited_ytd": 50000.00,
    "held_count": 4
  }
}
```

**POST /payments/emd — Request**
```json
{
  "bid_id": "uuid",
  "tender_id": "uuid",
  "gem_tender_id": "GEM-2024-T-12345",
  "tender_title": "Supply of Laptops",
  "amount": 50000.00,
  "payment_date": "2025-07-20",
  "payment_mode": "neft",
  "transaction_ref": "TXN20250720001",
  "bank_name": "HDFC Bank"
}
```

---

### Competitors — `/api/competitors`

| Method | Path | Description |
|--------|------|-------------|
| `GET`    | `/competitors` | List. Query: `?threat_level=high|medium|low&sort=win_rate` |
| `GET`    | `/competitors/{id}` | Detail + bid observation history |
| `GET`    | `/competitors/summary` | Competitive landscape overview |
| `PATCH`  | `/competitors/{id}` | Override tier or threat_level |

**GET /competitors/summary — Response**
```json
{
  "total": 34,
  "high_threat": 5,
  "medium_threat": 12,
  "low_threat": 17,
  "top_competitors": [
    { "company_name": "TechPro Solutions", "win_rate": 58.3, "threat_level": "high", "total_contracts": 142 }
  ],
  "market_concentration": "fragmented"
}
```

---

### Analysis — `/api/analysis`

| Method | Path | Description |
|--------|------|-------------|
| `GET`  | `/analysis/dashboard` | AI metrics dashboard |
| `GET`  | `/analysis/recommendations` | Top tender recommendations for org |
| `GET`  | `/analysis/performance` | Win/loss trend data |
| `GET`  | `/analysis/market-trends` | Category + state trend heatmap |
| `POST` | `/analysis/tenders/{tenderId}/reanalyze` | Queue fresh AI enrichment |

**GET /analysis/dashboard — Response**
```json
{
  "opportunity_score": 82,
  "active_opportunities": 23,
  "recommended_bids": 5,
  "risk_alerts": [
    { "type": "closing_soon", "tender_id": "uuid", "title": "Laptop Supply", "closing_in_hours": 36 }
  ],
  "performance": { "win_rate": 34.5, "avg_bid_value": 2100000, "total_won_ytd": 12000000 }
}
```

---

### Notifications — `/api/notifications`

| Method | Path | Description |
|--------|------|-------------|
| `GET`    | `/notifications` | List. Query: `?is_read=false&limit=20` |
| `PATCH`  | `/notifications/{id}/read` | Mark read |
| `POST`   | `/notifications/read-all` | Mark all read |
| `GET`    | `/notifications/preferences` | Preferences |
| `PATCH`  | `/notifications/preferences` | Update preferences |

---

### GeM Integration — `/api/integrations`

| Method | Path | Description |
|--------|------|-------------|
| `GET`    | `/integrations/gem` | Get config |
| `PUT`    | `/integrations/gem` | Save/update config |
| `POST`   | `/integrations/gem/sync` | Trigger manual sync |
| `GET`    | `/integrations/gem/sync-status` | Check sync status |

---

### Internal Pipeline Endpoints — `/internal`

Auth: `X-Api-Key: {Pipeline:ApiKey}` from appsettings. These are called by BidProcessor workers.

| Method | Path | Called by | Description |
|--------|------|-----------|-------------|
| `POST` | `/internal/tenders` | EnrichBidWorker | Upsert enriched tender |
| `POST` | `/internal/tender-documents` | ProcessBidDocumentsWorker | Store extracted doc |
| `POST` | `/internal/competitors` | EnrichBidWorker | Upsert competitor observation |

**POST /internal/tenders — Request**
```json
{
  "gem_tender_id": "GEM-2024-T-12345",
  "title": "Supply of Laptops",
  "buyer_org_name": "Ministry of Education",
  "state": "Delhi",
  "category": "IT Hardware",
  "tender_value": 5000000.00,
  "emd_amount": 50000.00,
  "published_date": "2025-07-01",
  "closing_date": "2025-08-15",
  "delivery_days": 30,
  "status": "active",
  "corrigendum_count": 0,
  "ai_score": 87,
  "eligibility_score": 92,
  "win_probability": 72.5,
  "risk_score": 18,
  "ai_summary": "...",
  "ai_tags": ["repeat_buyer"],
  "raw_data": {}
}
```

**POST /internal/competitors — Request**
```json
{
  "gem_tender_id": "GEM-2024-T-12345",
  "competitor_name": "TechPro Solutions Pvt Ltd",
  "gem_seller_id": "GEM-SELLER-99999",
  "observed_bid_value": 4800000.00,
  "was_winner": true,
  "awarded_value": 4800000.00,
  "observed_date": "2025-07-20",
  "org_ids": ["uuid1", "uuid2"],
  "raw_data": {}
}
```

---

## Roles & Permissions

| Role | Tenders | Bids | Docs | Compliance | Payments | Members | Settings |
|------|---------|------|------|------------|----------|---------|----------|
| owner | RW | RW | RW | RW | RW | RW | RW |
| admin | RW | RW | RW | RW | RW | RW | R |
| bid_manager | RW | RW | RW | R | R | R | - |
| finance | R | R | R | R | RW | - | - |
| sales | R | RW | R | R | R | - | - |
| viewer | R | R | R | R | R | - | - |

---

## Running the BFF

```bash
cd BiddingBuddyBFF/src/BiddingBuddy.Bff.Api
dotnet run
# Swagger UI: https://localhost:7100/swagger
```

### EF Core migrations (once DB is ready)

```bash
cd BiddingBuddyBFF/src/BiddingBuddy.Bff.Api
dotnet ef migrations add InitialCreate --project ../BiddingBuddy.Bff.Infrastructure
dotnet ef database update
```

Alternatively, run `database/schema.sql` directly in psql:
```bash
psql -U postgres -d bidding_buddy -f database/schema.sql
```
