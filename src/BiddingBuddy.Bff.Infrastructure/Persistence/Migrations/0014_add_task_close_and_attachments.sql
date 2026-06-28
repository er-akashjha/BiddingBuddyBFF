-- 0014_add_task_close_and_attachments
-- Phases 2 & 3 of the Bid Management redesign.
--
-- 1. bid_comments gains checklist_item_id + kind, so a task-completion note links back
--    to its checklist item AND shows up in the existing Notes feed for free (BID-302).
-- 2. bid_attachments table — files attached to a bid / comment / checklist item, stored
--    in R2 (bucket bidding-buddy) via the existing presign pattern (BID-303).
-- 3. Seed the TASK_ASSIGNED notification template (Email + InApp) (BID-301).
--
-- Idempotent: every step is guarded; safe to re-run.

-- 1 ── bid_comments: link a comment to a checklist item + tag its kind -------------------
ALTER TABLE bid_comments ADD COLUMN IF NOT EXISTS checklist_item_id UUID;
ALTER TABLE bid_comments ADD COLUMN IF NOT EXISTS kind TEXT NOT NULL DEFAULT 'comment';

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_bid_comments_checklist_item') THEN
    ALTER TABLE bid_comments ADD CONSTRAINT fk_bid_comments_checklist_item
      FOREIGN KEY (checklist_item_id) REFERENCES bid_checklist_items(id) ON DELETE SET NULL;
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_bid_comments_kind') THEN
    ALTER TABLE bid_comments ADD CONSTRAINT ck_bid_comments_kind
      CHECK (kind IN ('comment','task_completion'));
  END IF;
END $$;

CREATE INDEX IF NOT EXISTS idx_bid_comments_checklist_item ON bid_comments (checklist_item_id);

-- 2 ── bid_attachments --------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS bid_attachments (
  id                UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  org_id            UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
  bid_id            UUID NOT NULL REFERENCES bids(id) ON DELETE CASCADE,
  checklist_item_id UUID REFERENCES bid_checklist_items(id) ON DELETE SET NULL,
  comment_id        UUID REFERENCES bid_comments(id) ON DELETE SET NULL,
  file_name         TEXT   NOT NULL,
  content_type      TEXT   NOT NULL,
  size_bytes        BIGINT NOT NULL,
  storage_key       TEXT   NOT NULL,
  uploaded_by       UUID   NOT NULL REFERENCES users(id),
  created_at        TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_bid_attachments_bid     ON bid_attachments (bid_id);
CREATE INDEX IF NOT EXISTS idx_bid_attachments_comment ON bid_attachments (comment_id);

-- 3 ── TASK_ASSIGNED notification template (Email + InApp) --------------------------------
INSERT INTO notification_templates (code, channel, name, subject, body, body_format, metadata)
VALUES
  ('TASK_ASSIGNED', 'Email',
   'Task assigned email',
   '{{AssignedByName}} assigned you a task on {{BidTitle}}',
   '<p>Hi {{AssigneeName}},</p><p><b>{{AssignedByName}}</b> assigned you a task: <b>{{TaskTitle}}</b> on bid <b>{{BidTitle}}</b>.{{DueText}}</p><p><a href="{{ActionUrl}}">Open the bid</a></p>',
   'Html',
   '{}'::jsonb),
  ('TASK_ASSIGNED', 'InApp',
   'Task assigned in-app message',
   'New task: {{TaskTitle}}',
   '{{AssignedByName}} assigned you "{{TaskTitle}}" on {{BidTitle}}.',
   'Text',
   '{"actionUrl":"{{ActionUrl}}"}'::jsonb)
ON CONFLICT ON CONSTRAINT uq_template_code_channel DO NOTHING;
</content>
