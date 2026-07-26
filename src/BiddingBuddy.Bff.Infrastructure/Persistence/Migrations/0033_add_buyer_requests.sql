-- 0033_add_buyer_requests
--
-- The inbound path to becoming a buyer. Migration 0031 made becoming a buyer an
-- operator-only act (POST /internal/organizations/{id}/org-type) with no way for an
-- org to ASK — which is fine for a department we go out and onboard, but leaves a
-- self-serve supplier who wants to publish with nowhere to raise their hand.
--
-- This adds the request. The APPROVAL is unchanged in spirit: an operator still makes
-- the call and the conversion still runs through SetOrgTypeAsync, because a buyer
-- publishes notices on the public portal under a department's name and that trust
-- decision cannot be self-asserted. The request just gives them a front door and gives
-- the operator something concrete to verify.
--
-- Mirrors org_join_requests (0030): one live request per subject, decided rows kept as
-- history, partial unique index. The subject here is the ORG asking the platform, where
-- there it was a person asking an org.
--
-- Idempotent throughout.

-- ── 1. org_buyer_requests ───────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS org_buyer_requests (
    id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    org_id        UUID        NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
    requested_by  UUID        NOT NULL REFERENCES users(id),

    status        TEXT        NOT NULL DEFAULT 'pending',

    -- The procuring-entity identity the org is CLAIMING. Two jobs: it is what the
    -- operator verifies against, and on approval it is written onto the organization
    -- verbatim (via SetOrgTypeAsync) rather than retyped. Nullable — a small ULB may
    -- not know its ministry — but the justification is not.
    entity_type            TEXT NULL,
    ministry               TEXT NULL,
    department             TEXT NULL,
    office                 TEXT NULL,
    procuring_entity_code  TEXT NULL,

    -- Why this org should be trusted to publish under a government name. Mandatory:
    -- an approval is a judgement, and a judgement needs something to judge.
    justification TEXT        NOT NULL,

    -- The operator's note on the decision — the evidence that was checked, or the
    -- reason for refusal. Surfaced back to the org on rejection.
    decision_note TEXT        NULL,
    decided_at    TIMESTAMPTZ NULL,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT ck_buyer_request_status
        CHECK (status IN ('pending','approved','rejected','cancelled')),

    -- Same 8 values as organizations.entity_type (0031). Kept in step so a request
    -- cannot claim an entity type the org column would later reject.
    CONSTRAINT ck_buyer_request_entity_type
        CHECK (entity_type IS NULL OR entity_type IN
            ('central','state','psu','ulb','autonomous','cooperative','trust','private'))
);

-- One LIVE request per org. Decided rows accumulate as history, so a rejected org can
-- reapply once it has more to show — but cannot stack five pending rows in the queue.
-- The service returns the existing row rather than erroring, which makes the client's
-- "Request buyer access" button idempotent.
CREATE UNIQUE INDEX IF NOT EXISTS uq_buyer_requests_one_pending_per_org
    ON org_buyer_requests (org_id)
    WHERE status = 'pending';

-- The operator queue: pending first, oldest first (fair — first asked, first reviewed).
CREATE INDEX IF NOT EXISTS ix_buyer_requests_status
    ON org_buyer_requests (status, created_at);

-- "My request" on the Settings card.
CREATE INDEX IF NOT EXISTS ix_buyer_requests_org
    ON org_buyer_requests (org_id, created_at DESC);

-- ── 2. notification templates ───────────────────────────────────────────────
--
-- SUBMITTED goes to the OPERATOR (a configured ops address, not an org user — there is
-- no operator user in this system). APPROVED / REJECTED go back to the org.

INSERT INTO notification_templates (code, channel, name, subject, body, body_format, metadata)
VALUES
  ('BUYER_REQUEST_SUBMITTED', 'Email',
   'An organization asked to become a buyer',
   'Buyer request: {{OrgName}}',
   '<p>{{OrgName}} has requested buyer access — the ability to publish tender notices.</p>'
   || '<table style="border-collapse:collapse;margin:12px 0">'
   || '<tr><td style="padding:4px 12px 4px 0;color:#64748b">Requested by</td><td>{{RequesterName}} ({{RequesterEmail}})</td></tr>'
   || '<tr><td style="padding:4px 12px 4px 0;color:#64748b">Entity type</td><td>{{EntityType}}</td></tr>'
   || '<tr><td style="padding:4px 12px 4px 0;color:#64748b">Ministry / Dept</td><td>{{Ministry}} {{Department}}</td></tr>'
   || '<tr><td style="padding:4px 12px 4px 0;color:#64748b;vertical-align:top">Justification</td><td>{{Justification}}</td></tr>'
   || '</table>'
   || '<p style="color:#64748b;font-size:13px">Verify offline, then approve or reject:</p>'
   || '<pre style="background:#f1f5f9;padding:10px;border-radius:6px;font-size:12px">'
   || 'POST /internal/organizations/buyer-requests/{{RequestId}}/approve</pre>'
   || '<hr><p style="color:#64748b;font-size:12px">Nobody becomes a buyer until an operator approves this.</p>',
   'Html',
   '{}'::jsonb),

  ('BUYER_REQUEST_APPROVED', 'Email',
   'Buyer access approved',
   'You can now publish tenders on {{OrgName}}',
   '<p>Hi {{FirstName}},</p>'
   || '<p><b>{{OrgName}}</b> has been approved to publish tender notices. A new '
   || '<b>Tendering</b> section is now in your sidebar.</p>'
   || '{{#if DecisionNote}}<p style="border-left:3px solid #cbd5e1;padding-left:12px;color:#475569">{{DecisionNote}}</p>{{/if}}'
   || '<p><a href="{{Link}}" style="background:#0f172a;color:#fff;padding:10px 18px;border-radius:6px;text-decoration:none">Author your first tender</a></p>'
   || '<hr><p style="color:#64748b;font-size:12px">You author and publish notices; bids are received wherever you receive them today.</p>',
   'Html',
   '{}'::jsonb),

  ('BUYER_REQUEST_APPROVED', 'InApp',
   'Buyer access approved (in-app)',
   'Buyer access approved',
   '{{OrgName}} can now publish tenders. Find Tendering in the sidebar.',
   'Text',
   '{"actionUrl":"/buyer/tenders"}'::jsonb),

  ('BUYER_REQUEST_REJECTED', 'Email',
   'About your buyer access request',
   'Your buyer request for {{OrgName}}',
   '<p>Hi {{FirstName}},</p>'
   || '<p>Your request for <b>{{OrgName}}</b> to publish tenders was not approved.</p>'
   || '{{#if DecisionNote}}<p style="border-left:3px solid #cbd5e1;padding-left:12px;color:#475569">{{DecisionNote}}</p>{{/if}}'
   || '<p>You can raise a new request once the points above are addressed.</p>',
   'Html',
   '{}'::jsonb),

  ('BUYER_REQUEST_REJECTED', 'InApp',
   'Buyer access request declined (in-app)',
   'Buyer request declined',
   'Your request for {{OrgName}} to publish tenders was not approved.',
   'Text',
   '{"actionUrl":"/settings"}'::jsonb)
ON CONFLICT ON CONSTRAINT uq_template_code_channel DO NOTHING;
