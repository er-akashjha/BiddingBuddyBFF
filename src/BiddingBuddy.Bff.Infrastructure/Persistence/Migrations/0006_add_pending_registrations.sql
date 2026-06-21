-- 0006_add_pending_registrations
-- Verify-first signup: a password registration no longer creates a user directly.
-- Instead we stash the (BCrypt-hashed) credentials here, email a 6-digit OTP, and
-- only on POST /api/auth/verify-email do we create the real user + organization.
-- Mirrors organization_invites (0003): a pending row keyed by a hashed secret,
-- with an expiry and a "supersede prior pending" partial-unique guard.
--
-- Idempotent.

CREATE TABLE IF NOT EXISTS pending_registrations (
    id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    email         TEXT         NOT NULL,         -- stored lowercased by the BFF
    name          TEXT         NOT NULL,
    password_hash TEXT         NOT NULL,         -- BCrypt hash; plaintext is never stored
    org_name      TEXT         NULL,             -- NULL when joining via an invite
    phone         TEXT         NULL,
    invite_token  TEXT         NULL,             -- raw org-invite token, consumed at verify time
    code_hash     TEXT         NOT NULL,         -- SHA-256 hex of the 6-digit OTP (raw code leaves only via email)
    attempt_count INTEGER      NOT NULL DEFAULT 0,
    resend_count  INTEGER      NOT NULL DEFAULT 0,
    expires_at    TIMESTAMPTZ  NOT NULL,
    consumed_at   TIMESTAMPTZ  NULL,             -- set when the account is created (or when superseded)
    created_at    TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at    TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_pending_reg_email
    ON pending_registrations (email);

-- At most one PENDING (un-consumed) signup per email at a time. Re-signing-up
-- supersedes the prior pending row (the BFF sets consumed_at on it first). Consumed
-- rows can coexist (history). Partial unique index — Postgres-native; EF's fluent
-- HasIndex() can't model a partial index, so it lives here.
CREATE UNIQUE INDEX IF NOT EXISTS uq_pending_reg_one_active_per_email
    ON pending_registrations (email)
    WHERE consumed_at IS NULL;

-- ── Re-word the EMAIL_VERIFICATION template for an OTP code ──────────────────
-- 0002 seeded a link-style body ({{VerificationLink}}); the flow is now a 6-digit
-- code, so switch the subject + body to render {{Code}} / {{ExpiryMinutes}}.
-- Idempotent UPDATE (safe to re-run); only touches the Email channel row.
UPDATE notification_templates
SET subject     = 'Your BiddingBuddy verification code',
    body        = '<p>Hi {{FirstName}},</p><p>Your BiddingBuddy verification code is:</p><p style="font-size:24px;font-weight:bold;letter-spacing:4px">{{Code}}</p><p>It expires in {{ExpiryMinutes}} minutes. If you didn''t request this, you can ignore this email.</p>',
    body_format = 'Html',
    updated_at  = now()
WHERE code = 'EMAIL_VERIFICATION' AND channel = 'Email';
