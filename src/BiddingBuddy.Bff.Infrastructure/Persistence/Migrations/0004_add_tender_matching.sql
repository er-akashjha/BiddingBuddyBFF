-- 0004_add_tender_matching
-- Tender-match digest feature:
--   1. tender_alert_rules  — per-org "interests" (the matching criteria).
--   2. org_alert_settings  — per-org digest delivery prefs (batch size, channels, roles).
--   3. tender_matches      — buffer + dedup of matched tenders awaiting a digest flush.
--   4. Seeds the TENDER_MATCH notification template (Email + InApp), grouped list.
--
-- Idempotent: every step guarded; safe to re-run.

-- ── 1. tender_alert_rules ───────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS tender_alert_rules (
    id           UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    org_id       UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
    name         VARCHAR(200) NOT NULL,
    categories   TEXT[]       NULL,
    states       TEXT[]       NULL,
    keywords     TEXT[]       NULL,
    min_value    NUMERIC(15,2) NULL,
    max_value    NUMERIC(15,2) NULL,
    min_ai_score INTEGER      NULL,
    is_active    BOOLEAN      NOT NULL DEFAULT TRUE,
    created_by   UUID         NULL REFERENCES users(id),
    created_at   TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at   TIMESTAMPTZ  NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_tender_alert_rules_org_id ON tender_alert_rules (org_id);
CREATE INDEX IF NOT EXISTS idx_tender_alert_rules_active ON tender_alert_rules (is_active) WHERE is_active;

-- ── 2. org_alert_settings ───────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS org_alert_settings (
    org_id          UUID PRIMARY KEY REFERENCES organizations(id) ON DELETE CASCADE,
    is_enabled      BOOLEAN     NOT NULL DEFAULT TRUE,
    digest_size     INTEGER     NOT NULL DEFAULT 10,
    notify_channels TEXT[]      NOT NULL DEFAULT '{Email,InApp}'::TEXT[],
    notify_roles    TEXT[]      NOT NULL DEFAULT '{owner,admin,bid_manager}'::TEXT[],
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT ck_org_alert_digest_size CHECK (digest_size BETWEEN 1 AND 50)
);

-- ── 3. tender_matches (buffer + dedup) ──────────────────────────────────────

CREATE TABLE IF NOT EXISTS tender_matches (
    id         UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    org_id     UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
    tender_id  UUID NOT NULL REFERENCES tenders(id) ON DELETE CASCADE,
    rule_id    UUID NULL REFERENCES tender_alert_rules(id) ON DELETE SET NULL,
    status     VARCHAR(20) NOT NULL DEFAULT 'pending',
    matched_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    batch_id   UUID        NULL,
    sent_at    TIMESTAMPTZ NULL,
    CONSTRAINT uq_tender_match_org_tender UNIQUE (org_id, tender_id),
    CONSTRAINT ck_tender_match_status CHECK (status IN ('pending','sent','expired'))
);

CREATE INDEX IF NOT EXISTS idx_tender_matches_org_status ON tender_matches (org_id, status);

-- updated_at triggers (set_updated_at() created by the base schema) ───────────

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'trg_tender_alert_rules_updated_at') THEN
    CREATE TRIGGER trg_tender_alert_rules_updated_at
      BEFORE UPDATE ON tender_alert_rules
      FOR EACH ROW EXECUTE FUNCTION set_updated_at();
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'trg_org_alert_settings_updated_at') THEN
    CREATE TRIGGER trg_org_alert_settings_updated_at
      BEFORE UPDATE ON org_alert_settings
      FOR EACH ROW EXECUTE FUNCTION set_updated_at();
  END IF;
END $$;

-- ── 4. Seed TENDER_MATCH template (grouped list of tenders) ─────────────────

INSERT INTO notification_templates (code, channel, name, subject, body, body_format, metadata)
VALUES
  ('TENDER_MATCH', 'Email',
   'Tender match digest email',
   '{{Count}} new tender{{#unless One}}s{{/unless}} matching your interests',
   '<p>Hi {{FirstName}},</p><p>We found <b>{{Count}}</b> new tender{{#unless One}}s{{/unless}} that match your saved interests:</p><ul>{{#each Tenders}}<li><a href="{{Url}}"><b>{{Title}}</b></a><br/>{{Category}}{{#if State}} · {{State}}{{/if}}{{#if Value}} · ₹{{Value}}{{/if}} · closes {{ClosingDate}}</li>{{/each}}</ul><p><a href="{{AllUrl}}">View all matched tenders</a></p>',
   'Html',
   '{}'::jsonb),
  ('TENDER_MATCH', 'InApp',
   'Tender match digest in-app message',
   '{{Count}} new tender{{#unless One}}s{{/unless}} matching your interests',
   '{{Count}} new tender{{#unless One}}s{{/unless}} match your interests — including "{{FirstTitle}}".',
   'Text',
   '{"actionUrl":"/tenders?matched=1"}'::jsonb)
ON CONFLICT ON CONSTRAINT uq_template_code_channel DO NOTHING;
