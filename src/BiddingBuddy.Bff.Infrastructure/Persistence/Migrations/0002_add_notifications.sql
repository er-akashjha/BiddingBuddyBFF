-- 0002_add_notifications
-- Notification subsystem (handoff from BidProcessor team).
--   1. RENAMES the existing in-app inbox `notifications` -> `user_notifications`
--      so the dispatch-event table can claim the `notifications` name (per spec).
--   2. Creates notification_templates, notifications, notification_deliveries,
--      notification_logs with CHECK constraints + indexes from the handoff.
--   3. Seeds a few sample templates so the processor team can smoke-test.
--
-- Idempotent: every step is guarded; safe to re-run.

-- ── 1. Rename existing in-app inbox out of the way ──────────────────────────

DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM information_schema.tables
             WHERE table_schema = current_schema() AND table_name = 'notifications')
     AND NOT EXISTS (SELECT 1 FROM information_schema.columns
                     WHERE table_schema = current_schema() AND table_name = 'notifications'
                     AND column_name = 'category')
     AND NOT EXISTS (SELECT 1 FROM information_schema.tables
                     WHERE table_schema = current_schema() AND table_name = 'user_notifications')
  THEN
    -- Existing table is the OLD inbox schema (has 'type','title','is_read'); rename it.
    EXECUTE 'ALTER TABLE notifications RENAME TO user_notifications';
  END IF;
END $$;

-- ── 2. notification_templates ───────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS notification_templates (
    id           UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    code         VARCHAR(100) NOT NULL,
    channel      VARCHAR(20)  NOT NULL,
    name         VARCHAR(200) NOT NULL,
    subject      VARCHAR(500) NULL,
    body         TEXT         NOT NULL,
    body_format  VARCHAR(20)  NOT NULL DEFAULT 'Html',
    metadata     JSONB        NOT NULL DEFAULT '{}',
    is_active    BOOLEAN      NOT NULL DEFAULT TRUE,
    created_at   TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at   TIMESTAMPTZ  NULL,
    CONSTRAINT uq_template_code_channel UNIQUE (code, channel),
    CONSTRAINT ck_template_channel CHECK (channel IN ('Email','Sms','WhatsApp','Firebase','InApp')),
    CONSTRAINT ck_template_format  CHECK (body_format IN ('Html','Text','Markdown'))
);

-- ── 3. notifications (dispatch event) ───────────────────────────────────────

CREATE TABLE IF NOT EXISTS notifications (
    id             UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    category       VARCHAR(20)  NOT NULL,
    template_code  VARCHAR(100) NOT NULL,
    user_id        UUID         NULL,
    payload        JSONB        NOT NULL DEFAULT '{}',
    correlation_id UUID         NOT NULL DEFAULT gen_random_uuid(),
    created_at     TIMESTAMPTZ  NOT NULL DEFAULT now(),
    CONSTRAINT ck_notification_category CHECK (category IN ('Transactional','Information','Marketing'))
);

-- ── 4. notification_deliveries (per-channel processing unit) ────────────────

CREATE TABLE IF NOT EXISTS notification_deliveries (
    id                UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    notification_id   UUID         NOT NULL REFERENCES notifications(id) ON DELETE CASCADE,
    channel           VARCHAR(20)  NOT NULL,
    recipient_address VARCHAR(500) NOT NULL,
    status            VARCHAR(20)  NOT NULL DEFAULT 'Pending',
    retry_count       INTEGER      NOT NULL DEFAULT 0,
    max_retries       INTEGER      NOT NULL DEFAULT 5,
    next_retry_at     TIMESTAMPTZ  NULL,
    locked_at         TIMESTAMPTZ  NULL,
    locked_by         VARCHAR(100) NULL,
    last_error        TEXT         NULL,
    version           INTEGER      NOT NULL DEFAULT 0,
    created_at        TIMESTAMPTZ  NOT NULL DEFAULT now(),
    processed_at      TIMESTAMPTZ  NULL,
    failed_at         TIMESTAMPTZ  NULL,
    CONSTRAINT uq_delivery_per_channel UNIQUE (notification_id, channel),
    CONSTRAINT ck_delivery_channel CHECK (channel IN ('Email','Sms','WhatsApp','Firebase','InApp')),
    CONSTRAINT ck_delivery_status  CHECK (status IN ('Pending','Processing','Retrying','Completed','Failed'))
);

CREATE INDEX IF NOT EXISTS ix_deliveries_retry_scan ON notification_deliveries (next_retry_at)
    WHERE status IN ('Pending','Retrying');
CREATE INDEX IF NOT EXISTS ix_deliveries_stale ON notification_deliveries (locked_at)
    WHERE status = 'Processing';
CREATE INDEX IF NOT EXISTS ix_deliveries_notification ON notification_deliveries (notification_id);

-- ── 5. notification_logs (audit; processor-owned) ───────────────────────────

CREATE TABLE IF NOT EXISTS notification_logs (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    delivery_id         UUID         NOT NULL REFERENCES notification_deliveries(id) ON DELETE CASCADE,
    notification_id     UUID         NOT NULL,
    channel             VARCHAR(20)  NOT NULL,
    provider            VARCHAR(100) NULL,
    recipient_address   VARCHAR(500) NULL,
    subject             VARCHAR(500) NULL,
    status              VARCHAR(20)  NOT NULL,
    provider_message_id VARCHAR(255) NULL,
    attempt_number      INTEGER      NOT NULL,
    error_message       TEXT         NULL,
    created_at          TIMESTAMPTZ  NOT NULL DEFAULT now(),
    completed_at        TIMESTAMPTZ  NULL,
    CONSTRAINT ck_log_status CHECK (status IN ('Sent','Failed'))
);

CREATE INDEX IF NOT EXISTS ix_logs_delivery ON notification_logs (delivery_id);
CREATE INDEX IF NOT EXISTS ix_logs_notification ON notification_logs (notification_id);

-- ── 6. Seed sample templates (idempotent via UNIQUE (code,channel)) ─────────

INSERT INTO notification_templates (code, channel, name, subject, body, body_format, metadata)
VALUES
  ('WELCOME', 'Email',
   'Welcome email',
   'Welcome to BiddingBuddy, {{FirstName}}!',
   '<p>Hi {{FirstName}},</p><p>Thanks for joining BiddingBuddy. Your organization <b>{{OrganizationName}}</b> is ready to start tracking tenders.</p>',
   'Html',
   '{}'::jsonb),
  ('WELCOME', 'InApp',
   'Welcome in-app message',
   'Welcome, {{FirstName}}',
   'You can start tracking GeM tenders for {{OrganizationName}} now.',
   'Text',
   '{"actionUrl":"/dashboard"}'::jsonb),
  ('TEAM_INVITATION', 'Email',
   'Team invitation email',
   '{{InvitedByName}} invited you to join {{OrganizationName}} on BiddingBuddy',
   '<p>Hi {{FirstName}},</p><p><b>{{InvitedByName}}</b> has invited you to join <b>{{OrganizationName}}</b>.</p><p><a href="{{InvitationLink}}">Accept invitation</a></p>',
   'Html',
   '{}'::jsonb),
  ('TEAM_INVITATION', 'InApp',
   'Team invitation in-app message',
   '{{InvitedByName}} invited you to {{OrganizationName}}',
   'Open this notification to accept.',
   'Text',
   '{"actionUrl":"{{InvitationLink}}"}'::jsonb),
  ('PASSWORD_RESET', 'Email',
   'Password reset email',
   'Reset your BiddingBuddy password',
   '<p>Hi {{FirstName}},</p><p>Reset your password using the link below. It expires in {{ExpiryMinutes}} minutes.</p><p><a href="{{ResetLink}}">Reset password</a></p>',
   'Html',
   '{}'::jsonb),
  ('EMAIL_VERIFICATION', 'Email',
   'Email verification',
   'Verify your email for BiddingBuddy',
   '<p>Hi {{FirstName}},</p><p>Confirm your email by clicking below.</p><p><a href="{{VerificationLink}}">Verify email</a></p>',
   'Html',
   '{}'::jsonb)
ON CONFLICT ON CONSTRAINT uq_template_code_channel DO NOTHING;
