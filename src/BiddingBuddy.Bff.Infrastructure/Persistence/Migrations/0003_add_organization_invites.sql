-- 0003_add_organization_invites
-- Pending invites for users who DON'T yet exist in the users table. When the
-- recipient registers via the emailed link with the token, the invite is consumed:
-- the new user is created, an organization_members row is inserted in the
-- inviting org, and accepted_at is set.
--
-- Idempotent.

CREATE TABLE IF NOT EXISTS organization_invites (
    id           UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    org_id       UUID         NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
    email        TEXT         NOT NULL,         -- stored lowercased by the BFF
    role         TEXT         NOT NULL,
    department   TEXT         NULL,
    invited_by   UUID         NOT NULL REFERENCES users(id),
    token_hash   TEXT         NOT NULL,         -- SHA-256 hex of the raw token (raw token leaves only via email)
    expires_at   TIMESTAMPTZ  NOT NULL,
    accepted_at  TIMESTAMPTZ  NULL,
    created_at   TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_invites_token_hash
    ON organization_invites (token_hash);

CREATE INDEX IF NOT EXISTS ix_invites_org_email
    ON organization_invites (org_id, email);

-- Only one PENDING invite per (org, email) at a time. Accepted/expired ones can
-- coexist (history). Implemented as a partial unique index — Postgres-native,
-- but EF's fluent HasIndex() can't model partial indexes so this lives here.
CREATE UNIQUE INDEX IF NOT EXISTS uq_invites_one_pending_per_email_per_org
    ON organization_invites (org_id, email)
    WHERE accepted_at IS NULL;
