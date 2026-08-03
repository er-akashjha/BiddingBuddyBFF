-- 0034_add_platform_admin
--
-- A platform-operator identity, so buyer-access requests (0033) can be approved from
-- INSIDE the app instead of only by an out-of-band `X-Api-Key` curl.
--
-- Until now the only operator was "whoever holds Pipeline:ApiKey" — deliberately, because a
-- buyer publishes notices under a department's name and that trust decision must not be
-- self-served (see InternalOrganizationsController). That is still true: this does NOT open
-- org_type to customers. It names a small set of OUR people who may work the review queue
-- with their own logged-in identity, via a JWT-gated /api/admin/* surface that checks this
-- flag. The API key remains valid; this is an additional, auditable front door for operators.
--
-- Fails closed: the column defaults FALSE, so every existing user is a non-admin until
-- explicitly granted. There is no client-facing route to set it — it is granted by an
-- operator with database access, exactly like the trust it represents:
--
--     UPDATE users SET is_platform_admin = TRUE WHERE email = 'you@yourcompany.com';
--
-- Idempotent.

ALTER TABLE users
    ADD COLUMN IF NOT EXISTS is_platform_admin BOOLEAN NOT NULL DEFAULT FALSE;

-- Tiny partial index: the admin set is a handful of rows, and the only query that reads this
-- column looks for the TRUE ones. Keeps "who are the operators" a cheap answer without
-- indexing a column that is FALSE for every ordinary user.
CREATE INDEX IF NOT EXISTS ix_users_platform_admin
    ON users (is_platform_admin)
    WHERE is_platform_admin;
