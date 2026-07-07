-- 0020: mobile push (FCM) — device registry + Firebase notification templates.
--
-- Part of the TendersAgent mobile app (docs/mobile-app/PLAN.md, work items B3/B4).
-- The push delivery rail already exists end-to-end (BFF publisher → RabbitMQ
-- notification.firebase → BidProcessor FirebaseNotificationSender). What was missing:
--   1. somewhere to store device tokens (this table), and
--   2. Firebase templates so the processor has something to render per event.
--
-- Fan-out is centralized in NotificationPublisher: for any notification with an InApp
-- recipient whose template code is push-worthy, it appends one Firebase delivery using
-- the recipient user's most-recently-seen active, push-enabled device token.

-- ── 1. Device registry ──────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS user_devices (
    id                uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id           uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    platform          text NOT NULL CHECK (platform IN ('ios','android')),
    fcm_token         text NOT NULL UNIQUE,
    app_version       text,
    push_enabled      boolean NOT NULL DEFAULT true,   -- per-device mute (the app's push toggle)
    last_seen_at      timestamptz NOT NULL DEFAULT now(),
    created_at        timestamptz NOT NULL DEFAULT now(),
    revoked_at        timestamptz,                     -- set on logout, or by the processor on FCM Unregistered
    revocation_reason text
);

-- Fan-out query: newest active push-enabled device for a user. Partial index keeps it tight.
CREATE INDEX IF NOT EXISTS idx_user_devices_active
    ON user_devices (user_id, last_seen_at DESC)
    WHERE revoked_at IS NULL AND push_enabled = true;

-- ── 2. Firebase templates ───────────────────────────────────────────────────
-- Seed by cloning the existing InApp rows for the push-worthy (money / deadline /
-- assignment / outcome) events. Cloning guarantees the Handlebars variable names and
-- the deep-link metadata (orgId/type/entityType/entityId) match the in-app bell exactly,
-- and InApp bodies are already plain 'Text' — correct for an FCM notification body.
INSERT INTO notification_templates (code, channel, name, subject, body, body_format, metadata, is_active)
SELECT code,
       'Firebase',
       name || ' (push)',
       subject,
       body,
       body_format,
       metadata,
       is_active
FROM notification_templates
WHERE channel = 'InApp'
  AND code IN (
      'BID_DUE_SOON', 'BID_OVERDUE', 'BID_ASSIGNED', 'BID_WON', 'BID_LOST',
      'BID_TASK_DUE_SOON', 'BID_TASK_OVERDUE',
      'TENDER_MATCH', 'TENDER_AMENDED', 'TENDER_CLOSING_SOON', 'TENDER_HIGH_FIT',
      'COMPLIANCE_EXPIRING', 'COMPLIANCE_EXPIRED',
      'INVOICE_DUE_SOON', 'INVOICE_OVERDUE'
  )
ON CONFLICT DO NOTHING;
