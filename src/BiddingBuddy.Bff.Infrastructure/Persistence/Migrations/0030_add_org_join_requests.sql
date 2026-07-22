-- 0030_add_org_join_requests
--
-- Closes the duplicate-organization hole: POST /api/organizations used to insert
-- unconditionally, so the second person from a company to sign up silently got a
-- brand-new empty workspace instead of joining their colleagues.
--
-- Three parts:
--   1. org_join_requests — the way a blocked signup asks to be let in
--   2. lookup indexes that make the duplicate probe cheap
--   3. the JOIN_REQUEST / _APPROVED / _REJECTED notification templates
--
-- Idempotent throughout.

-- ── 1. org_join_requests ────────────────────────────────────────────────────
--
-- Mirrors organization_invites, pointing the other way: an invite is the org
-- reaching out to a person, a join request is a person reaching in. Both share
-- the rule that matters — membership is NEVER granted by the request itself,
-- only by an owner/admin acting on it.

CREATE TABLE IF NOT EXISTS org_join_requests (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    org_id      UUID        NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
    user_id     UUID        NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    status      TEXT        NOT NULL DEFAULT 'pending',
    message     TEXT        NULL,          -- optional note from the requester
    role        TEXT        NULL,          -- role granted on approval; NULL until decided
    decided_by  UUID        NULL REFERENCES users(id),
    decided_at  TIMESTAMPTZ NULL,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT ck_join_request_status
        CHECK (status IN ('pending','approved','rejected','cancelled'))
);

-- One LIVE request per (org, user). Decided rows accumulate as history, so a
-- rejected applicant can apply again later — but cannot stack five pending rows
-- in the admin's queue. The service returns the existing row rather than erroring,
-- which is what makes the client's "Request to join" button idempotent.
CREATE UNIQUE INDEX IF NOT EXISTS uq_join_requests_one_pending_per_user_per_org
    ON org_join_requests (org_id, user_id)
    WHERE status = 'pending';

-- The Team page's queue: pending rows for one org, newest first.
CREATE INDEX IF NOT EXISTS ix_join_requests_org_status
    ON org_join_requests (org_id, status, created_at DESC);

-- "My requests" on the onboarding waiting state.
CREATE INDEX IF NOT EXISTS ix_join_requests_user
    ON org_join_requests (user_id, created_at DESC);

-- ── 2. duplicate-probe indexes ──────────────────────────────────────────────

-- GSTIN, normalized. The expression MUST stay byte-identical to what
-- OrganizationService emits — EF renders `.Replace(" ", "").ToUpper()` as
-- upper(replace(gstin, ' ', '')), and writing the two calls the other way round
-- produces a different expression that silently falls back to a sequential scan.
CREATE INDEX IF NOT EXISTS ix_organizations_gstin_normalized
    ON organizations (upper(replace(gstin, ' ', '')))
    WHERE gstin IS NOT NULL;

-- Company-name prefix match. text_pattern_ops is what lets a btree serve
-- `lower(name) LIKE 'acme%'`; the default opclass cannot under a non-C collation.
-- Prefix-only is a deliberate limit — see docs/org-join-requests/PLAN.md §2.
CREATE INDEX IF NOT EXISTS ix_organizations_name_lower
    ON organizations (lower(name) text_pattern_ops);

-- Report, do not enforce.
--
-- A UNIQUE index on the normalized GSTIN is the obvious guarantee and it is NOT
-- safe to add here: this check has never existed, so any database that most needs
-- the fix is the one most likely to already hold duplicates — and CREATE UNIQUE
-- INDEX would abort the whole migration on exactly those. Surface them instead and
-- let an operator reconcile; promoting this to UNIQUE afterwards is a one-line
-- follow-up migration.
DO $$
DECLARE
    dup RECORD;
    found_any BOOLEAN := FALSE;
BEGIN
    FOR dup IN
        SELECT upper(replace(gstin, ' ', '')) AS norm,
               count(*)                        AS n,
               string_agg(name || ' (' || id || ')', ', ' ORDER BY created_at) AS orgs
        FROM organizations
        WHERE gstin IS NOT NULL AND btrim(gstin) <> ''
        GROUP BY 1
        HAVING count(*) > 1
    LOOP
        found_any := TRUE;
        RAISE NOTICE 'DUPLICATE GSTIN % — % organizations: %', dup.norm, dup.n, dup.orgs;
    END LOOP;

    IF found_any THEN
        RAISE NOTICE 'Pre-existing duplicate GSTINs found (listed above). New signups are blocked from here on; these rows predate the check and need manual reconciliation.';
    END IF;
END $$;

-- ── 3. notification templates ───────────────────────────────────────────────

INSERT INTO notification_templates (code, channel, name, subject, body, body_format, metadata)
VALUES
  ('JOIN_REQUEST', 'Email',
   'Someone asked to join your workspace',
   '{{RequesterName}} asked to join {{OrgName}} on Tenders Agent',
   '<p>Hi {{FirstName}},</p>'
   || '<p><b>{{RequesterName}}</b> ({{RequesterEmail}}) signed up and identified '
   || '<b>{{OrgName}}</b> as their company. They are waiting to be let in.</p>'
   || '{{#if Message}}<p style="border-left:3px solid #cbd5e1;padding-left:12px;color:#475569">{{Message}}</p>{{/if}}'
   || '<p>Nobody joins your workspace until you approve them, and you choose what role they get.</p>'
   || '<p><a href="{{Link}}" style="background:#0f172a;color:#fff;padding:10px 18px;border-radius:6px;text-decoration:none">Review the request</a></p>'
   || '<hr><p style="color:#64748b;font-size:12px">If you do not recognise this person, reject the request — they will not gain any access.</p>',
   'Html',
   '{}'::jsonb),

  ('JOIN_REQUEST', 'InApp',
   'Join request in-app message',
   '{{RequesterName}} asked to join {{OrgName}}',
   '{{RequesterName}} ({{RequesterEmail}}) is waiting for approval to join your workspace.',
   'Text',
   '{"actionUrl":"/team"}'::jsonb),

  ('JOIN_REQUEST_APPROVED', 'Email',
   'Your join request was approved',
   'You are in — welcome to {{OrgName}}',
   '<p>Hi {{FirstName}},</p>'
   || '<p><b>{{ApproverName}}</b> approved your request to join <b>{{OrgName}}</b>. '
   || 'You have been added as <b>{{Role}}</b>.</p>'
   || '<p><a href="{{Link}}" style="background:#0f172a;color:#fff;padding:10px 18px;border-radius:6px;text-decoration:none">Open your workspace</a></p>',
   'Html',
   '{}'::jsonb),

  ('JOIN_REQUEST_APPROVED', 'InApp',
   'Join request approved in-app message',
   'You joined {{OrgName}}',
   '{{ApproverName}} approved your request to join {{OrgName}} as {{Role}}.',
   'Text',
   '{"actionUrl":"/dashboard"}'::jsonb),

  ('JOIN_REQUEST_REJECTED', 'Email',
   'Your join request was declined',
   'About your request to join {{OrgName}}',
   '<p>Hi {{FirstName}},</p>'
   || '<p>Your request to join <b>{{OrgName}}</b> was not approved.</p>'
   || '<p>If you believe this is a mistake, the quickest fix is to ask a colleague who already '
   || 'uses Tenders Agent to invite you directly. You can also set up your own workspace.</p>'
   || '<p><a href="{{Link}}" style="background:#0f172a;color:#fff;padding:10px 18px;border-radius:6px;text-decoration:none">Back to Tenders Agent</a></p>',
   'Html',
   '{}'::jsonb),

  ('JOIN_REQUEST_REJECTED', 'InApp',
   'Join request declined in-app message',
   'Request to join {{OrgName}} declined',
   'Your request to join {{OrgName}} was not approved. Ask a colleague to invite you, or create your own workspace.',
   'Text',
   '{"actionUrl":"/onboarding/company"}'::jsonb)
ON CONFLICT ON CONSTRAINT uq_template_code_channel DO NOTHING;
