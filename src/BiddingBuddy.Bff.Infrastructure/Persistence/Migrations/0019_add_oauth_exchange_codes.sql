-- 0019: one-time authorization codes for the mobile OAuth handoff (PKCE).
--
-- Native apps can't receive tokens via the SPA redirect, and tokens must never
-- ride a redirect URL. For client=mobile OAuth flows the callback now mints a
-- short-lived single-use code; the app redeems it (with its PKCE S256 verifier)
-- at POST /api/auth/oauth/exchange for a normal access+refresh pair.

CREATE TABLE IF NOT EXISTS oauth_exchange_codes (
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id         uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    code_hash       text NOT NULL UNIQUE,
    code_challenge  text NOT NULL,
    is_new_user     boolean NOT NULL DEFAULT false,
    expires_at      timestamptz NOT NULL,
    used_at         timestamptz,
    created_at      timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_oauth_exchange_codes_expires_at
    ON oauth_exchange_codes (expires_at);
