-- 0032_add_entra_sso
--
-- Enterprise SSO: an organization binds itself to its Microsoft Entra tenant, and any
-- work account signing in from that tenant joins automatically instead of needing an
-- individual invite (0003) or a join request (0030).
--
-- Three parts:
--   1. organizations.entra_tenant_id + sso_default_role — the binding and what it grants
--   2. oauth_accounts.tenant_id                        — the proof a user belongs to a tenant
--   3. org_sso_domains                                 — routing only; grants nothing
--
-- THE INVARIANT, because it is the only thing standing between this feature and handing
-- one company's workspace to another: membership is granted by the `tid` claim of a
-- signature-verified Entra id_token, NEVER by an email domain. org_sso_domains exists so
-- the login page knows which button to press for you. It is not an authorization input.
--
-- Idempotent throughout.

-- ── 1. the binding ──────────────────────────────────────────────────────────

ALTER TABLE organizations ADD COLUMN IF NOT EXISTS entra_tenant_id  UUID NULL;
ALTER TABLE organizations ADD COLUMN IF NOT EXISTS sso_default_role TEXT NOT NULL DEFAULT 'viewer';

-- Role granted to someone who walks in via SSO. Deliberately the least-privileged role in the
-- vocabulary: an auto-join is the one membership nobody explicitly approved, so it should be able
-- to read the workspace and change nothing until a human promotes them.
ALTER TABLE organizations DROP CONSTRAINT IF EXISTS ck_organizations_sso_default_role;
ALTER TABLE organizations ADD  CONSTRAINT ck_organizations_sso_default_role
    CHECK (sso_default_role IN (
        'owner','admin','bid_manager','finance','sales','viewer',
        'po_admin','po_publisher','po_opener','po_evaluator','auditor'));

-- One tenant, one workspace. Two orgs claiming the same directory would make auto-join
-- ambiguous, and "ambiguous" here means picking someone's employer for them.
--
-- Note this is a genuine UNIQUE, unlike 0030's GSTIN index which deliberately only reported
-- duplicates. That restraint was right there because the column predated the check, so the
-- databases most in need of the constraint were exactly the ones a CREATE UNIQUE INDEX would
-- abort on. entra_tenant_id is new in this script — no row can already violate it. Enforce.
CREATE UNIQUE INDEX IF NOT EXISTS uq_organizations_entra_tenant
    ON organizations (entra_tenant_id)
    WHERE entra_tenant_id IS NOT NULL;

-- ── 2. proof of tenant membership ───────────────────────────────────────────

-- Populated from the id_token's `tid` on every Microsoft sign-in. Two jobs: it is what an
-- auto-join is matched against, and it is what POST /api/organizations/{id}/sso/entra reads to
-- decide whether the caller may bind a tenant — you can only claim the directory you personally
-- just authenticated against, which is what stops anyone typing a competitor's (publicly
-- discoverable) tenant GUID and collecting their staff.
ALTER TABLE oauth_accounts ADD COLUMN IF NOT EXISTS tenant_id UUID NULL;

CREATE INDEX IF NOT EXISTS ix_oauth_accounts_tenant
    ON oauth_accounts (tenant_id)
    WHERE tenant_id IS NOT NULL;

-- ── 3. routing domains ──────────────────────────────────────────────────────

-- Which email domains should be sent to Microsoft instead of shown a password box.
--
-- Rows are written by the server when a tid-matched user signs in, never typed by a customer,
-- and that is what makes them trustworthy without us building a DNS-verification flow: Entra
-- refuses to add a custom domain to a tenant until someone proves ownership via a DNS TXT
-- record, so a work account's domain is already Microsoft-verified by the time we see it.
CREATE TABLE IF NOT EXISTS org_sso_domains (
    id         UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    org_id     UUID        NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
    domain     TEXT        NOT NULL,
    source     TEXT        NOT NULL DEFAULT 'entra',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT ck_org_sso_domain_source CHECK (source IN ('entra','manual'))
);

-- A domain routes to exactly one place. Lower-cased because hostnames are case-insensitive and
-- ACME.COM must not become a second, competing route for acme.com.
CREATE UNIQUE INDEX IF NOT EXISTS uq_org_sso_domains_domain
    ON org_sso_domains (lower(domain));

CREATE INDEX IF NOT EXISTS ix_org_sso_domains_org
    ON org_sso_domains (org_id, created_at DESC);

-- ── 4. notification template ────────────────────────────────────────────────
--
-- Auto-join is the only path into a workspace that no human approved, so the owners are told
-- after the fact. Without this the first sign an admin gets that SSO is live is a stranger's
-- name in the member list.

INSERT INTO notification_templates (code, channel, name, subject, body, body_format, metadata)
VALUES
  ('SSO_MEMBER_JOINED', 'Email',
   'Someone joined via single sign-on',
   '{{MemberName}} joined {{OrgName}} via Microsoft SSO',
   '<p>Hi {{FirstName}},</p>'
   || '<p><b>{{MemberName}}</b> ({{MemberEmail}}) signed in with your organisation''s Microsoft '
   || 'account and was added to <b>{{OrgName}}</b> as <b>{{Role}}</b>.</p>'
   || '<p>They were let in automatically because your workspace is connected to your Microsoft '
   || 'Entra directory, and their account belongs to it.</p>'
   || '<p><a href="{{Link}}" style="background:#0f172a;color:#fff;padding:10px 18px;border-radius:6px;text-decoration:none">Review your team</a></p>'
   || '<hr><p style="color:#64748b;font-size:12px">If this is unexpected, you can disconnect single '
   || 'sign-on in Settings &rarr; Single sign-on. Existing members keep their access.</p>',
   'Html',
   '{}'::jsonb),

  ('SSO_MEMBER_JOINED', 'InApp',
   'SSO member joined in-app message',
   '{{MemberName}} joined via SSO',
   '{{MemberName}} ({{MemberEmail}}) was added to {{OrgName}} as {{Role}} via Microsoft single sign-on.',
   'Text',
   '{"actionUrl":"/team"}'::jsonb)
ON CONFLICT ON CONSTRAINT uq_template_code_channel DO NOTHING;
