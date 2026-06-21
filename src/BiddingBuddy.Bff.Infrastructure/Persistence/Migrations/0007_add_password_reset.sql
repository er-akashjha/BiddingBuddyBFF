-- 0007_add_password_reset
-- Password reset via 6-digit OTP (consistent with the signup verification flow).
-- "Forgot password" stores the SHA-256 hash of an emailed code here; the password
-- is changed only when the matching code is submitted to POST /api/auth/reset-password.
-- Mirrors pending_registrations (0006): hashed secret, expiry, attempt cap, and a
-- "supersede prior active" partial-unique guard.
--
-- Idempotent.

CREATE TABLE IF NOT EXISTS password_reset_codes (
    id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id       UUID         NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    code_hash     TEXT         NOT NULL,         -- SHA-256 hex of the 6-digit OTP (raw code leaves only via email)
    attempt_count INTEGER      NOT NULL DEFAULT 0,
    resend_count  INTEGER      NOT NULL DEFAULT 0,
    expires_at    TIMESTAMPTZ  NOT NULL,
    consumed_at   TIMESTAMPTZ  NULL,             -- set when the password is changed (or when superseded)
    created_at    TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at    TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_password_reset_user
    ON password_reset_codes (user_id);

-- At most one ACTIVE (un-consumed) reset code per user. Requesting again supersedes
-- the prior code (the BFF sets consumed_at on it first). Partial unique index —
-- Postgres-native; EF's fluent HasIndex() can't model a partial index, so it lives here.
CREATE UNIQUE INDEX IF NOT EXISTS uq_password_reset_one_active_per_user
    ON password_reset_codes (user_id)
    WHERE consumed_at IS NULL;

-- ── Re-word the PASSWORD_RESET template for an OTP code ──────────────────────
-- 0002 seeded a link-style body ({{ResetLink}}); the flow is now a 6-digit code,
-- so switch the body to render {{Code}} / {{ExpiryMinutes}}. Idempotent UPDATE.
UPDATE notification_templates
SET subject     = 'Your BiddingBuddy password reset code',
    body        = '<p>Hi {{FirstName}},</p><p>Use this code to reset your BiddingBuddy password:</p><p style="font-size:24px;font-weight:bold;letter-spacing:4px">{{Code}}</p><p>It expires in {{ExpiryMinutes}} minutes. If you didn''t request this, you can ignore this email and your password stays unchanged.</p>',
    body_format = 'Html',
    updated_at  = now()
WHERE code = 'PASSWORD_RESET' AND channel = 'Email';
