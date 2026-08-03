-- 0031_add_buyer_tendering
--
-- Phase 1 of buyer-side tendering (docs/gov-tendering/PLAN.md §3): a government
-- department authors a tender notice here and publishes it. Bids are still received
-- wherever they are received today — we never touch a bid, so this phase needs no
-- STQC certification, no PKI and no HSM.
--
-- Seven parts:
--   1. organizations.org_type            — buyer / supplier discriminator (net-new, §5.2.8)
--   2. buyer roles on org_members — the GePNIC separation-of-duties model (§4.I)
--   3. tender_ownership                  — "this org owns this tender" (net-new, §5.2.2)
--   4. tender_drafts                     — the mutable authoring document
--   5. tender_versions                   — immutable, hash-chained published versions (§4.M)
--   6. corrigenda                        — append-only amendments (§4.L)
--   7. audit_events                      — every field change, no hard delete (§4.M)
--   8. notification templates for publication and corrigendum
--
-- Idempotent throughout.

-- ── 1. organizations.org_type ───────────────────────────────────────────────
--
-- 'supplier' is the default and describes every org that exists today: they bid.
-- 'buyer' is a department that publishes. 'both' exists because a PSU genuinely is
-- both — it tenders for its own procurement and bids for contracts elsewhere — and
-- forcing it to keep two workspaces would split its document vault for no reason.

ALTER TABLE organizations
  ADD COLUMN IF NOT EXISTS org_type TEXT NOT NULL DEFAULT 'supplier';

DO $$
DECLARE c record;
BEGIN
  FOR c IN
    SELECT con.conname
      FROM pg_constraint con
      JOIN pg_class rel ON rel.oid = con.conrelid
     WHERE rel.relname = 'organizations'
       AND con.contype = 'c'
       AND pg_get_constraintdef(con.oid) ILIKE '%org_type%'
  LOOP
    EXECUTE format('ALTER TABLE organizations DROP CONSTRAINT %I', c.conname);
  END LOOP;
END $$;

ALTER TABLE organizations ADD CONSTRAINT organizations_org_type_check
  CHECK (org_type IN ('supplier','buyer','both'));

-- Procuring-entity identity. Only meaningful for buyers; NULL for every supplier.
-- These are the §4.A fields that belong to the ORGANISATION rather than to any one
-- tender — a department retypes its ministry and address on every notice today.
ALTER TABLE organizations
  ADD COLUMN IF NOT EXISTS entity_type          TEXT NULL,   -- central|state|psu|ulb|autonomous|cooperative|trust|private
  ADD COLUMN IF NOT EXISTS ministry             TEXT NULL,
  ADD COLUMN IF NOT EXISTS department           TEXT NULL,
  ADD COLUMN IF NOT EXISTS office               TEXT NULL,
  ADD COLUMN IF NOT EXISTS procuring_entity_code TEXT NULL;  -- the department's own org code, free text

DO $$
DECLARE c record;
BEGIN
  FOR c IN
    SELECT con.conname
      FROM pg_constraint con
      JOIN pg_class rel ON rel.oid = con.conrelid
     WHERE rel.relname = 'organizations'
       AND con.contype = 'c'
       AND pg_get_constraintdef(con.oid) ILIKE '%entity_type%'
  LOOP
    EXECUTE format('ALTER TABLE organizations DROP CONSTRAINT %I', c.conname);
  END LOOP;
END $$;

ALTER TABLE organizations ADD CONSTRAINT organizations_entity_type_check
  CHECK (entity_type IS NULL OR entity_type IN
    ('central','state','psu','ulb','autonomous','cooperative','trust','private'));

-- Buyer surfaces are hidden entirely for supplier orgs, so the discriminator is read
-- on nearly every buyer request. Partial — supplier rows are the overwhelming majority
-- and never need to be found this way.
CREATE INDEX IF NOT EXISTS ix_organizations_org_type
  ON organizations (org_type)
  WHERE org_type <> 'supplier';

-- ── 2. buyer roles ──────────────────────────────────────────────────────────
--
-- GePNIC separates five duties across five credentials (§2.3): PO Admin creates,
-- PO Publisher publishes, PO Opener opens the tender box, PO Evaluator evaluates,
-- Auditor observes. That separation is the control that makes the process auditable,
-- so it is kept — but as ROLES on one account rather than five sets of credentials
-- that get shared in practice.
--
-- Phase 1 only exercises po_admin, po_publisher and auditor: there is no bid to open
-- or evaluate until Phase 3. po_opener and po_evaluator are defined now so committee
-- membership recorded on a Phase-1 tender is still meaningful when Phase 3 lands, and
-- so that the CHECK does not have to be widened again on a table that predates DbMigrator.
--
-- ⚠ org_members PREDATES DbMigrator (created by hand from database/schema.sql),
-- so its constraint name in any given environment is whatever Postgres generated.
-- Drop by LOOKUP over pg_constraint, never by name. The loop also re-drops the constraint
-- this script adds, which is what makes the block re-runnable.
DO $$
DECLARE c record;
BEGIN
  FOR c IN
    SELECT con.conname
      FROM pg_constraint con
      JOIN pg_class rel ON rel.oid = con.conrelid
     WHERE rel.relname = 'org_members'
       AND con.contype = 'c'
       AND pg_get_constraintdef(con.oid) ILIKE '%role%'
  LOOP
    EXECUTE format('ALTER TABLE org_members DROP CONSTRAINT %I', c.conname);
  END LOOP;
END $$;

ALTER TABLE org_members ADD CONSTRAINT org_members_role_check
  CHECK (role IN (
    'owner','admin','bid_manager','finance','sales','viewer',   -- supplier-side, unchanged
    'po_admin','po_publisher','po_opener','po_evaluator','auditor'  -- buyer-side, new
  ));

-- ── 3. tender_ownership ─────────────────────────────────────────────────────
--
-- Tenders are GLOBAL in this system — verified, neither the Mongo model nor the
-- Postgres entity carries an org_id, and org association lives only in the
-- OrgTenderSettings join (PLAN §5.2.2). "This department owns and may edit this
-- tender" therefore has nowhere to live and is net-new state.
--
-- Keyed on the DRAFT rather than on tenders.id: a draft is owned from the moment it
-- is created, which is long before any tender row exists anywhere.

CREATE TABLE IF NOT EXISTS tender_ownership (
    id           UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    org_id       UUID        NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
    draft_id     UUID        NOT NULL,   -- FK added after tender_drafts exists, below
    relationship TEXT        NOT NULL DEFAULT 'owner',
    created_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT ck_tender_ownership_relationship
        CHECK (relationship IN ('owner','delegate'))
);

-- ── 4. tender_drafts ────────────────────────────────────────────────────────
--
-- The mutable authoring document. Structured columns for everything queried, filtered
-- or validated; one JSONB for the long tail of §4 (eligibility text, cover definitions,
-- statutory flags) that is written and read as a whole and never searched.
--
-- reference_code is OURS and URL-SAFE BY CONSTRUCTION (PLAN §5.2.9). A department's own
-- file number looks like "F.No.12-3/2026-Admin" — those slashes are exactly why the
-- enrichment-status endpoint already has to take its id in the body rather than the
-- route. Generating TA-2026-000123 and keeping the department's number as an ordinary
-- display field (department_reference) means the id never has to be escaped anywhere.

CREATE TABLE IF NOT EXISTS tender_drafts (
    id                    UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    org_id                UUID        NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
    reference_code        TEXT        NOT NULL,        -- TA-2026-000123, generated, URL-safe
    department_reference  TEXT        NULL,            -- the department's own file number, free text

    status                TEXT        NOT NULL DEFAULT 'draft',
    -- draft → published → (amended)* → awarded | cancelled
    -- 'amended' is not a status: a corrigendum bumps the version, it does not move the
    -- tender out of 'published'. Cancellation IS terminal and is recorded as a corrigendum too.

    -- B. Basic details
    title                 TEXT        NOT NULL DEFAULT '',
    description           TEXT        NOT NULL DEFAULT '',
    scope_of_work         TEXT        NOT NULL DEFAULT '',
    tender_type           TEXT        NULL,   -- open|limited|single|global|eoi|rfp|rfq|two_stage
    procurement_category  TEXT        NULL,   -- goods|works|services|consultancy
    form_of_contract      TEXT        NULL,
    bidding_system        TEXT        NULL,   -- single_cover|two_cover|three_cover|multi_cover
    evaluation_method     TEXT        NULL,   -- l1|qcbs|lcs|fixed_budget|single_source
    technical_weightage   NUMERIC(5,2) NULL,  -- QCBS only; financial weightage is 100 - this
    gfr_rule_cited        TEXT        NULL,

    -- Taxonomy. MUST be canonical: BiddingBuddyServices rewrites an off-taxonomy value
    -- silently on write, and alert matching is EXACT, so an unresolved category means the
    -- tender matches nobody, forever (PLAN §5.2.1). The BFF rejects rather than rewrites.
    category              TEXT        NULL,
    state                 TEXT        NULL,
    city                  TEXT        NULL,
    pincode               TEXT        NULL,

    -- E. Fees and financials
    estimated_value       NUMERIC(18,2) NULL,
    value_disclosed       BOOLEAN     NOT NULL DEFAULT TRUE,
    emd_amount            NUMERIC(18,2) NULL,
    emd_percentage        NUMERIC(5,2)  NULL,
    emd_mode              TEXT        NULL,
    emd_exemptions        TEXT[]      NOT NULL DEFAULT '{}',   -- mse|startup|nsic
    tender_fee            NUMERIC(18,2) NULL,
    tender_fee_exemptions TEXT[]      NOT NULL DEFAULT '{}',
    performance_security_pct NUMERIC(5,2) NULL,
    bid_validity_days     INT         NULL,

    -- F. Critical dates. All TIMESTAMPTZ, all server-authoritative, all cross-validated.
    published_at          TIMESTAMPTZ NULL,
    doc_download_start    TIMESTAMPTZ NULL,
    doc_download_end      TIMESTAMPTZ NULL,
    clarification_start   TIMESTAMPTZ NULL,
    clarification_end     TIMESTAMPTZ NULL,
    prebid_meeting_at     TIMESTAMPTZ NULL,
    prebid_venue          TEXT        NULL,
    bid_submission_start  TIMESTAMPTZ NULL,
    bid_submission_end    TIMESTAMPTZ NULL,
    technical_opening_at  TIMESTAMPTZ NULL,
    financial_opening_at  TIMESTAMPTZ NULL,

    -- H. Statutory compliance — the audit-proofing layer, and the wedge (§2.5)
    mse_reservation_pct   NUMERIC(5,2) NULL,
    mii_applicable        BOOLEAN     NOT NULL DEFAULT FALSE,
    mii_local_content_pct NUMERIC(5,2) NULL,
    mii_class_restriction TEXT        NULL,   -- class_i_only|class_i_and_ii|none
    lbs_declaration_required BOOLEAN  NOT NULL DEFAULT FALSE,
    startup_relaxation    BOOLEAN     NOT NULL DEFAULT FALSE,
    integrity_pact_applicable BOOLEAN NOT NULL DEFAULT FALSE,
    integrity_pact_monitor TEXT       NULL,
    gemarpts_reference    TEXT        NULL,

    -- The long tail of §4: covers[], items[], tech specs[], eligibility{}, documents[],
    -- contact{}. Written and read whole, never searched — a column each would be 40 more
    -- columns that no query ever names.
    detail                JSONB       NOT NULL DEFAULT '{}'::jsonb,

    -- Which compliance rule set this draft was last validated against. Pinned at publish
    -- so a rule change two years from now cannot retroactively make a published tender
    -- look non-compliant (PLAN §6.1).
    rule_set_version      TEXT        NULL,

    -- Set once, at first publish. The Mongo _id of the projected tender, so corrigenda
    -- can re-project to the same document and every existing read path resolves.
    mongo_tender_id       TEXT        NULL,
    current_version       INT         NOT NULL DEFAULT 0,

    created_by            UUID        NOT NULL REFERENCES users(id),
    published_by          UUID        NULL REFERENCES users(id),
    created_at            TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at            TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT ck_tender_drafts_status
        CHECK (status IN ('draft','published','awarded','cancelled')),
    CONSTRAINT ck_tender_drafts_tender_type
        CHECK (tender_type IS NULL OR tender_type IN
            ('open','limited','single','global','eoi','rfp','rfq','two_stage')),
    CONSTRAINT ck_tender_drafts_procurement_category
        CHECK (procurement_category IS NULL OR procurement_category IN
            ('goods','works','services','consultancy')),
    CONSTRAINT ck_tender_drafts_bidding_system
        CHECK (bidding_system IS NULL OR bidding_system IN
            ('single_cover','two_cover','three_cover','multi_cover')),
    CONSTRAINT ck_tender_drafts_evaluation_method
        CHECK (evaluation_method IS NULL OR evaluation_method IN
            ('l1','qcbs','lcs','fixed_budget','single_source')),
    CONSTRAINT ck_tender_drafts_mii_class
        CHECK (mii_class_restriction IS NULL OR mii_class_restriction IN
            ('class_i_only','class_i_and_ii','none'))
);

-- The reference code is the natural key a department quotes in correspondence and is
-- unique across the whole platform, not per-org: two departments quoting TA-2026-000123
-- at the same auditor and meaning different tenders is exactly the ambiguity the code
-- exists to remove.
CREATE UNIQUE INDEX IF NOT EXISTS uq_tender_drafts_reference_code
    ON tender_drafts (reference_code);

-- The buyer's own tender list: their drafts, newest first, filtered by status.
CREATE INDEX IF NOT EXISTS ix_tender_drafts_org_status
    ON tender_drafts (org_id, status, updated_at DESC);

-- Corrigendum re-projection and the on-awarded hook both arrive holding the Mongo id.
CREATE INDEX IF NOT EXISTS ix_tender_drafts_mongo_tender_id
    ON tender_drafts (mongo_tender_id)
    WHERE mongo_tender_id IS NOT NULL;

-- Deferred FK: tender_ownership is declared above tender_drafts so the file reads in
-- dependency order for a human, which means the reference has to be added afterwards.
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_constraint WHERE conname = 'fk_tender_ownership_draft'
  ) THEN
    ALTER TABLE tender_ownership
      ADD CONSTRAINT fk_tender_ownership_draft
      FOREIGN KEY (draft_id) REFERENCES tender_drafts(id) ON DELETE CASCADE;
  END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS uq_tender_ownership_org_draft
    ON tender_ownership (org_id, draft_id);

CREATE INDEX IF NOT EXISTS ix_tender_ownership_draft
    ON tender_ownership (draft_id);

-- reference_code allocation. A sequence rather than max()+1: two officers clicking
-- "New tender" in the same second must not race for the same number, and the unique
-- index would turn that race into a 500 on an empty form.
CREATE SEQUENCE IF NOT EXISTS tender_reference_seq START 1;

-- ── 5. tender_versions — immutable, hash-chained ────────────────────────────
--
-- The audit truth (§4.M). Every publish and every corrigendum appends one row holding
-- the complete canonical snapshot; rows are NEVER updated or deleted.
--
-- The chain: content_hash = sha256(canonical JSON of the snapshot)
--            chain_hash   = sha256(prev_chain_hash || content_hash)
-- Version 1 has prev_chain_hash = '' (genesis). Any alteration to a historical row
-- breaks every chain_hash after it, which is detectable by replay without needing a
-- notary — and replay is what the downloadable audit file lets an inspector do.
--
-- This is tamper-EVIDENT, not tamper-PROOF: whoever can write these rows can rebuild
-- the chain. RFC 3161 timestamping (§4.M) is what closes that and it is Phase 3 work.

CREATE TABLE IF NOT EXISTS tender_versions (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    draft_id        UUID        NOT NULL REFERENCES tender_drafts(id) ON DELETE CASCADE,
    version         INT         NOT NULL,
    reason          TEXT        NOT NULL,   -- 'published' | 'corrigendum' | 'award' | 'cancellation'
    snapshot        JSONB       NOT NULL,
    content_hash    TEXT        NOT NULL,
    prev_chain_hash TEXT        NOT NULL DEFAULT '',
    chain_hash      TEXT        NOT NULL,
    rule_set_version TEXT       NOT NULL,
    published_by    UUID        NOT NULL REFERENCES users(id),
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT ck_tender_versions_reason
        CHECK (reason IN ('published','corrigendum','award','cancellation'))
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_tender_versions_draft_version
    ON tender_versions (draft_id, version);

CREATE INDEX IF NOT EXISTS ix_tender_versions_draft
    ON tender_versions (draft_id, version DESC);

-- ── 6. corrigenda ───────────────────────────────────────────────────────────
--
-- Date extensions and amendments are a constant, first-class workflow (§2.4), not an
-- exception path. Append-only and versioned: a corrigendum never edits its predecessor.

CREATE TABLE IF NOT EXISTS corrigenda (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    draft_id        UUID        NOT NULL REFERENCES tender_drafts(id) ON DELETE CASCADE,
    version_id      UUID        NULL REFERENCES tender_versions(id),
    corrigendum_no  INT         NOT NULL,
    type            TEXT        NOT NULL,   -- date_extension|amendment|cancellation|retender
    reason          TEXT        NOT NULL,
    -- The field-level diff: [{ field, oldValue, newValue }]. Rendered as the diff view
    -- bidders see, and replayable against the previous snapshot.
    changes         JSONB       NOT NULL DEFAULT '[]'::jsonb,
    notified_at     TIMESTAMPTZ NULL,
    created_by      UUID        NOT NULL REFERENCES users(id),
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT ck_corrigenda_type
        CHECK (type IN ('date_extension','amendment','cancellation','retender'))
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_corrigenda_draft_no
    ON corrigenda (draft_id, corrigendum_no);

CREATE INDEX IF NOT EXISTS ix_corrigenda_draft
    ON corrigenda (draft_id, corrigendum_no DESC);

-- ── 7. audit_events ─────────────────────────────────────────────────────────
--
-- Every field change: who, when, old value, new value (§4.M). Deliberately generic —
-- entity_type/entity_id rather than a FK — because the audit file must survive the
-- deletion of what it describes, and a FK with ON DELETE CASCADE would erase exactly
-- the evidence an inspector came for.

CREATE TABLE IF NOT EXISTS audit_events (
    id           BIGSERIAL PRIMARY KEY,
    org_id       UUID        NOT NULL,
    entity_type  TEXT        NOT NULL,   -- 'tender_draft' | 'corrigendum' | 'committee'
    entity_id    UUID        NOT NULL,
    action       TEXT        NOT NULL,   -- created|updated|published|corrigendum_issued|awarded|cancelled|committee_changed
    actor_id     UUID        NULL,       -- NULL = system action; no FK, see above
    actor_name   TEXT        NOT NULL DEFAULT '',
    actor_role   TEXT        NOT NULL DEFAULT '',
    changes      JSONB       NOT NULL DEFAULT '[]'::jsonb,
    ip_address   TEXT        NULL,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- The audit file for one tender, oldest first — the order an inspector reads in.
CREATE INDEX IF NOT EXISTS ix_audit_events_entity
    ON audit_events (entity_type, entity_id, created_at);

CREATE INDEX IF NOT EXISTS ix_audit_events_org
    ON audit_events (org_id, created_at DESC);

-- ── 8. tender_committee_members ─────────────────────────────────────────────
--
-- §4.I. Bid openers are recorded as M-of-N key holders in Phase 3; in Phase 1 the
-- record is documentary — it is what the published notice and the audit file name.
-- Members are org users, so a departing officer's row survives their membership being
-- suspended: the audit file must still say who was on the committee at the time.

CREATE TABLE IF NOT EXISTS tender_committee_members (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    draft_id    UUID        NOT NULL REFERENCES tender_drafts(id) ON DELETE CASCADE,
    user_id     UUID        NULL REFERENCES users(id),
    committee   TEXT        NOT NULL,   -- opening|technical|financial|monitor
    member_name TEXT        NOT NULL,
    designation TEXT        NOT NULL DEFAULT '',
    email       TEXT        NOT NULL DEFAULT '',
    is_chair    BOOLEAN     NOT NULL DEFAULT FALSE,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT ck_committee_kind
        CHECK (committee IN ('opening','technical','financial','monitor'))
);

CREATE INDEX IF NOT EXISTS ix_committee_draft
    ON tender_committee_members (draft_id, committee);

-- ── updated_at trigger ──────────────────────────────────────────────────────

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'trg_tender_drafts_updated_at') THEN
    CREATE TRIGGER trg_tender_drafts_updated_at
      BEFORE UPDATE ON tender_drafts
      FOR EACH ROW EXECUTE FUNCTION set_updated_at();
  END IF;
END $$;

-- ── 9. notification templates ───────────────────────────────────────────────
--
-- A corrigendum has to reach bidders or the extension may as well not have happened
-- (§2.4). Recipients are resolved from tender_matches — the people our own matching
-- rail already told about this tender — so the notice reaches exactly whoever was
-- told it existed.

INSERT INTO notification_templates (code, channel, name, subject, body, body_format, metadata)
VALUES
  ('TENDER_CORRIGENDUM', 'Email',
   'A tender you were alerted to has changed',
   'Corrigendum {{CorrigendumNo}} — {{TenderTitle}}',
   '<p>Hi {{FirstName}},</p>'
   || '<p><b>{{BuyerName}}</b> issued corrigendum <b>{{CorrigendumNo}}</b> on a tender you were alerted to.</p>'
   || '<p style="font-size:16px;font-weight:600;margin:16px 0 4px">{{TenderTitle}}</p>'
   || '<p style="color:#64748b;margin:0 0 16px">{{ReferenceCode}} · {{CorrigendumType}}</p>'
   || '<p style="border-left:3px solid #cbd5e1;padding-left:12px;color:#475569">{{Reason}}</p>'
   || '{{#if Changes}}<table style="border-collapse:collapse;width:100%;margin:16px 0">'
   || '<tr style="text-align:left;color:#64748b;font-size:12px"><th style="padding:6px 8px">Field</th><th style="padding:6px 8px">Was</th><th style="padding:6px 8px">Now</th></tr>'
   || '{{#each Changes}}<tr style="border-top:1px solid #e2e8f0">'
   || '<td style="padding:6px 8px">{{Field}}</td>'
   || '<td style="padding:6px 8px;color:#94a3b8;text-decoration:line-through">{{OldValue}}</td>'
   || '<td style="padding:6px 8px;font-weight:600">{{NewValue}}</td></tr>{{/each}}</table>{{/if}}'
   || '{{#if NewSubmissionEnd}}<p><b>New submission deadline: {{NewSubmissionEnd}}</b></p>{{/if}}'
   || '<p><a href="{{Url}}" style="background:#0f172a;color:#fff;padding:10px 18px;border-radius:6px;text-decoration:none">View the tender</a></p>'
   || '<hr><p style="color:#64748b;font-size:12px">You are receiving this because this tender matched one of your interests.</p>',
   'Html',
   '{}'::jsonb),

  ('TENDER_CORRIGENDUM', 'InApp',
   'Corrigendum in-app message',
   'Corrigendum {{CorrigendumNo}} — {{TenderTitle}}',
   '{{BuyerName}} issued corrigendum {{CorrigendumNo}} ({{CorrigendumType}}) on {{TenderTitle}}.',
   'Text',
   '{"actionUrl":"/tenders/{{MongoTenderId}}"}'::jsonb),

  ('TENDER_PUBLISHED', 'Email',
   'Your tender is live',
   '{{ReferenceCode}} is published',
   '<p>Hi {{FirstName}},</p>'
   || '<p><b>{{TenderTitle}}</b> is now published and visible to suppliers.</p>'
   || '<p style="color:#64748b">{{ReferenceCode}}{{#if DepartmentReference}} · {{DepartmentReference}}{{/if}}</p>'
   || '<p>{{MatchedSupplierLine}}</p>'
   || '<p>Bids close <b>{{SubmissionEnd}}</b>.</p>'
   || '<p><a href="{{Url}}" style="background:#0f172a;color:#fff;padding:10px 18px;border-radius:6px;text-decoration:none">View the published notice</a></p>'
   || '<hr><p style="color:#64748b;font-size:12px">Version {{Version}} · published {{PublishedAt}} IST. '
   || 'The audit file for this tender records every change and is downloadable from the tender page.</p>',
   'Html',
   '{}'::jsonb),

  ('TENDER_PUBLISHED', 'InApp',
   'Tender published in-app message',
   '{{ReferenceCode}} is live',
   '{{TenderTitle}} is published. Bids close {{SubmissionEnd}}.',
   'Text',
   '{"actionUrl":"/buyer/tenders/{{DraftId}}"}'::jsonb)
ON CONFLICT ON CONSTRAINT uq_template_code_channel DO NOTHING;
