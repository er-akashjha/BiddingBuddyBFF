-- 0015_add_deadline_notifications
-- Notifications feature (docs/notifications) — all phases.
--   1. notification_reminders — idempotency ledger for the scheduled DeadlineScanWorker
--      (one row per (entity, milestone) already reminded) AND for one-shot pipeline
--      events like tender corrigendum.
--   2. Seeds notification_templates (Email + InApp) for every event code emitted by the
--      deadline scan, the inline bid/order/member hooks, the weekly digest, and the
--      corrigendum detector.
--   3. Adds orgId/type to the TENDER_MATCH InApp template so it persists to the inbox too.
--
-- Idempotent: table guarded with IF NOT EXISTS; template inserts ON CONFLICT DO NOTHING.
-- InApp metadata fields (orgId/type/entityType/entityId) are read by the BidProcessor
-- InAppNotificationSender to build the user_notifications row. Handlebars.Net (logic-less):
-- {{Var}}, {{#if}}{{/if}}.

-- ── 1. notification_reminders (dedup ledger) ────────────────────────────────

CREATE TABLE IF NOT EXISTS notification_reminders (
    id           UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    org_id       UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
    entity_type  VARCHAR(40) NOT NULL,   -- bid | invoice | compliance_document | delivery_milestone | emd | bid_checklist | tender
    entity_id    UUID        NOT NULL,
    reminder_key VARCHAR(80) NOT NULL,   -- e.g. BID_DUE_SOON, BID_OVERDUE, TENDER_AMENDED:2
    sent_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_notification_reminder UNIQUE (entity_type, entity_id, reminder_key)
);

CREATE INDEX IF NOT EXISTS idx_notification_reminders_org ON notification_reminders (org_id);

-- ── 2. Seed notification templates (Email + InApp per code) ──────────────────
-- Email bodies share one compact card; only headline/body/cta vary. The hidden
-- {{Link}} CTA is an absolute URL the publisher builds from Frontend:BaseUrl.

INSERT INTO notification_templates (code, channel, name, subject, body, body_format, metadata)
VALUES
  -- ░░ Deadline scan: bids ░░
  ('BID_DUE_SOON','Email','Bid due soon',
   'Bid "{{BidTitle}}" is due {{DueText}}',
   '<div style="font-family:-apple-system,Segoe UI,Roboto,Arial,sans-serif;max-width:560px;margin:0 auto;padding:24px;"><div style="font-size:15px;font-weight:700;color:#16263F;margin-bottom:16px;">Tenders<span style="color:#13B07C;">Agent</span></div><h1 style="font-size:18px;color:#16263F;margin:0 0 8px;">Bid due {{DueText}}</h1><p style="font-size:14px;color:#5b6b7b;line-height:21px;margin:0 0 18px;">Hi {{FirstName}}, your bid <b>{{BidTitle}}</b> is due {{DueText}} ({{DueDate}}). Make sure it is submitted in time.</p><a href="{{Link}}" style="display:inline-block;background:#13B07C;color:#fff;font-size:14px;font-weight:600;text-decoration:none;padding:10px 22px;border-radius:8px;">Open bid</a></div>',
   'Html','{}'::jsonb),
  ('BID_DUE_SOON','InApp','Bid due soon (in-app)','Bid due {{DueText}}','{{BidTitle}} is due {{DueText}}.','Text',
   '{"orgId":"{{OrgId}}","type":"deadline","entityType":"bid","entityId":"{{EntityId}}"}'::jsonb),

  ('BID_OVERDUE','Email','Bid overdue',
   'Overdue: bid "{{BidTitle}}"',
   '<div style="font-family:-apple-system,Segoe UI,Roboto,Arial,sans-serif;max-width:560px;margin:0 auto;padding:24px;"><div style="font-size:15px;font-weight:700;color:#16263F;margin-bottom:16px;">Tenders<span style="color:#13B07C;">Agent</span></div><h1 style="font-size:18px;color:#16263F;margin:0 0 8px;">Bid overdue</h1><p style="font-size:14px;color:#5b6b7b;line-height:21px;margin:0 0 18px;">Hi {{FirstName}}, your bid <b>{{BidTitle}}</b> was due {{DueText}} ({{DueDate}}) and is still open. Review it now.</p><a href="{{Link}}" style="display:inline-block;background:#13B07C;color:#fff;font-size:14px;font-weight:600;text-decoration:none;padding:10px 22px;border-radius:8px;">Open bid</a></div>',
   'Html','{}'::jsonb),
  ('BID_OVERDUE','InApp','Bid overdue (in-app)','Bid overdue','{{BidTitle}} is overdue ({{DueText}}).','Text',
   '{"orgId":"{{OrgId}}","type":"deadline","entityType":"bid","entityId":"{{EntityId}}"}'::jsonb),

  -- ░░ Deadline scan: bid checklist tasks ░░
  ('BID_TASK_DUE_SOON','Email','Bid task due soon',
   'Task "{{TaskTitle}}" is due {{DueText}}',
   '<div style="font-family:-apple-system,Segoe UI,Roboto,Arial,sans-serif;max-width:560px;margin:0 auto;padding:24px;"><div style="font-size:15px;font-weight:700;color:#16263F;margin-bottom:16px;">Tenders<span style="color:#13B07C;">Agent</span></div><h1 style="font-size:18px;color:#16263F;margin:0 0 8px;">Task due {{DueText}}</h1><p style="font-size:14px;color:#5b6b7b;line-height:21px;margin:0 0 18px;">Hi {{FirstName}}, your task <b>{{TaskTitle}}</b> on bid <b>{{BidTitle}}</b> is due {{DueText}} ({{DueDate}}).</p><a href="{{Link}}" style="display:inline-block;background:#13B07C;color:#fff;font-size:14px;font-weight:600;text-decoration:none;padding:10px 22px;border-radius:8px;">Open bid</a></div>',
   'Html','{}'::jsonb),
  ('BID_TASK_DUE_SOON','InApp','Bid task due soon (in-app)','Task due {{DueText}}','Task "{{TaskTitle}}" ({{BidTitle}}) is due {{DueText}}.','Text',
   '{"orgId":"{{OrgId}}","type":"deadline","entityType":"bid","entityId":"{{EntityId}}"}'::jsonb),

  ('BID_TASK_OVERDUE','Email','Bid task overdue',
   'Overdue task: "{{TaskTitle}}"',
   '<div style="font-family:-apple-system,Segoe UI,Roboto,Arial,sans-serif;max-width:560px;margin:0 auto;padding:24px;"><div style="font-size:15px;font-weight:700;color:#16263F;margin-bottom:16px;">Tenders<span style="color:#13B07C;">Agent</span></div><h1 style="font-size:18px;color:#16263F;margin:0 0 8px;">Task overdue</h1><p style="font-size:14px;color:#5b6b7b;line-height:21px;margin:0 0 18px;">Hi {{FirstName}}, your task <b>{{TaskTitle}}</b> on bid <b>{{BidTitle}}</b> was due {{DueText}} ({{DueDate}}).</p><a href="{{Link}}" style="display:inline-block;background:#13B07C;color:#fff;font-size:14px;font-weight:600;text-decoration:none;padding:10px 22px;border-radius:8px;">Open bid</a></div>',
   'Html','{}'::jsonb),
  ('BID_TASK_OVERDUE','InApp','Bid task overdue (in-app)','Task overdue','Task "{{TaskTitle}}" ({{BidTitle}}) is overdue.','Text',
   '{"orgId":"{{OrgId}}","type":"deadline","entityType":"bid","entityId":"{{EntityId}}"}'::jsonb),

  -- ░░ Deadline scan: invoices ░░
  ('INVOICE_DUE_SOON','Email','Invoice due soon',
   'Invoice {{InvoiceNumber}} is due {{DueText}}',
   '<div style="font-family:-apple-system,Segoe UI,Roboto,Arial,sans-serif;max-width:560px;margin:0 auto;padding:24px;"><div style="font-size:15px;font-weight:700;color:#16263F;margin-bottom:16px;">Tenders<span style="color:#13B07C;">Agent</span></div><h1 style="font-size:18px;color:#16263F;margin:0 0 8px;">Invoice due {{DueText}}</h1><p style="font-size:14px;color:#5b6b7b;line-height:21px;margin:0 0 18px;">Hi {{FirstName}}, invoice <b>{{InvoiceNumber}}</b>{{#if BuyerOrg}} for {{BuyerOrg}}{{/if}} of &#8377;{{Amount}} is due {{DueText}} ({{DueDate}}).</p><a href="{{Link}}" style="display:inline-block;background:#13B07C;color:#fff;font-size:14px;font-weight:600;text-decoration:none;padding:10px 22px;border-radius:8px;">View payments</a></div>',
   'Html','{}'::jsonb),
  ('INVOICE_DUE_SOON','InApp','Invoice due soon (in-app)','Invoice due {{DueText}}','Invoice {{InvoiceNumber}} is due {{DueText}}.','Text',
   '{"orgId":"{{OrgId}}","type":"payment_alert","entityType":"invoice","entityId":"{{EntityId}}"}'::jsonb),

  ('INVOICE_OVERDUE','Email','Invoice overdue',
   'Overdue invoice {{InvoiceNumber}}',
   '<div style="font-family:-apple-system,Segoe UI,Roboto,Arial,sans-serif;max-width:560px;margin:0 auto;padding:24px;"><div style="font-size:15px;font-weight:700;color:#16263F;margin-bottom:16px;">Tenders<span style="color:#13B07C;">Agent</span></div><h1 style="font-size:18px;color:#16263F;margin:0 0 8px;">Invoice overdue</h1><p style="font-size:14px;color:#5b6b7b;line-height:21px;margin:0 0 18px;">Hi {{FirstName}}, invoice <b>{{InvoiceNumber}}</b>{{#if BuyerOrg}} for {{BuyerOrg}}{{/if}} of &#8377;{{Amount}} was due {{DueText}} ({{DueDate}}) and remains unpaid.</p><a href="{{Link}}" style="display:inline-block;background:#13B07C;color:#fff;font-size:14px;font-weight:600;text-decoration:none;padding:10px 22px;border-radius:8px;">View payments</a></div>',
   'Html','{}'::jsonb),
  ('INVOICE_OVERDUE','InApp','Invoice overdue (in-app)','Invoice overdue','Invoice {{InvoiceNumber}} is overdue.','Text',
   '{"orgId":"{{OrgId}}","type":"payment_alert","entityType":"invoice","entityId":"{{EntityId}}"}'::jsonb),

  -- ░░ Deadline scan: compliance ░░
  ('COMPLIANCE_EXPIRING','Email','Compliance document expiring',
   '{{DocName}} expires {{ExpiryText}}',
   '<div style="font-family:-apple-system,Segoe UI,Roboto,Arial,sans-serif;max-width:560px;margin:0 auto;padding:24px;"><div style="font-size:15px;font-weight:700;color:#16263F;margin-bottom:16px;">Tenders<span style="color:#13B07C;">Agent</span></div><h1 style="font-size:18px;color:#16263F;margin:0 0 8px;">Compliance document expiring</h1><p style="font-size:14px;color:#5b6b7b;line-height:21px;margin:0 0 18px;">Hi {{FirstName}}, your <b>{{DocName}}</b> expires {{ExpiryText}} ({{ExpiryDate}}). Renew it to stay eligible for bids.</p><a href="{{Link}}" style="display:inline-block;background:#13B07C;color:#fff;font-size:14px;font-weight:600;text-decoration:none;padding:10px 22px;border-radius:8px;">Review compliance</a></div>',
   'Html','{}'::jsonb),
  ('COMPLIANCE_EXPIRING','InApp','Compliance expiring (in-app)','Compliance expiring','{{DocName}} expires {{ExpiryText}}.','Text',
   '{"orgId":"{{OrgId}}","type":"document_expiry","entityType":"compliance","entityId":"{{EntityId}}"}'::jsonb),

  ('COMPLIANCE_EXPIRED','Email','Compliance document expired',
   '{{DocName}} has expired',
   '<div style="font-family:-apple-system,Segoe UI,Roboto,Arial,sans-serif;max-width:560px;margin:0 auto;padding:24px;"><div style="font-size:15px;font-weight:700;color:#16263F;margin-bottom:16px;">Tenders<span style="color:#13B07C;">Agent</span></div><h1 style="font-size:18px;color:#16263F;margin:0 0 8px;">Compliance document expired</h1><p style="font-size:14px;color:#5b6b7b;line-height:21px;margin:0 0 18px;">Hi {{FirstName}}, your <b>{{DocName}}</b> expired {{ExpiryText}} ({{ExpiryDate}}). An expired certificate can disqualify your bids.</p><a href="{{Link}}" style="display:inline-block;background:#13B07C;color:#fff;font-size:14px;font-weight:600;text-decoration:none;padding:10px 22px;border-radius:8px;">Review compliance</a></div>',
   'Html','{}'::jsonb),
  ('COMPLIANCE_EXPIRED','InApp','Compliance expired (in-app)','Compliance expired','{{DocName}} has expired.','Text',
   '{"orgId":"{{OrgId}}","type":"document_expiry","entityType":"compliance","entityId":"{{EntityId}}"}'::jsonb),

  -- ░░ Deadline scan: delivery milestones ░░
  ('DELIVERY_OVERDUE','Email','Delivery milestone overdue',
   'Delivery milestone overdue: {{MilestoneTitle}}',
   '<div style="font-family:-apple-system,Segoe UI,Roboto,Arial,sans-serif;max-width:560px;margin:0 auto;padding:24px;"><div style="font-size:15px;font-weight:700;color:#16263F;margin-bottom:16px;">Tenders<span style="color:#13B07C;">Agent</span></div><h1 style="font-size:18px;color:#16263F;margin:0 0 8px;">Delivery milestone overdue</h1><p style="font-size:14px;color:#5b6b7b;line-height:21px;margin:0 0 18px;">Hi {{FirstName}}, the milestone <b>{{MilestoneTitle}}</b> was due {{DueText}} ({{DueDate}}) and is not yet complete.</p><a href="{{Link}}" style="display:inline-block;background:#13B07C;color:#fff;font-size:14px;font-weight:600;text-decoration:none;padding:10px 22px;border-radius:8px;">View order</a></div>',
   'Html','{}'::jsonb),
  ('DELIVERY_OVERDUE','InApp','Delivery overdue (in-app)','Delivery overdue','Delivery milestone "{{MilestoneTitle}}" is overdue.','Text',
   '{"orgId":"{{OrgId}}","type":"order_alert","entityType":"order","entityId":"{{EntityId}}"}'::jsonb),

  -- ░░ Deadline scan: EMD held too long ░░
  ('EMD_STUCK','Email','EMD still held',
   'EMD of &#8377;{{Amount}} held {{HeldDays}} days',
   '<div style="font-family:-apple-system,Segoe UI,Roboto,Arial,sans-serif;max-width:560px;margin:0 auto;padding:24px;"><div style="font-size:15px;font-weight:700;color:#16263F;margin-bottom:16px;">Tenders<span style="color:#13B07C;">Agent</span></div><h1 style="font-size:18px;color:#16263F;margin:0 0 8px;">EMD still held</h1><p style="font-size:14px;color:#5b6b7b;line-height:21px;margin:0 0 18px;">Hi {{FirstName}}, your EMD of &#8377;{{Amount}} for <b>{{TenderTitle}}</b> has been held for {{HeldDays}} days (since {{PaymentDate}}). Check whether a refund is due.</p><a href="{{Link}}" style="display:inline-block;background:#13B07C;color:#fff;font-size:14px;font-weight:600;text-decoration:none;padding:10px 22px;border-radius:8px;">View payments</a></div>',
   'Html','{}'::jsonb),
  ('EMD_STUCK','InApp','EMD still held (in-app)','EMD still held','EMD for {{TenderTitle}} held {{HeldDays}} days.','Text',
   '{"orgId":"{{OrgId}}","type":"payment_alert","entityType":"payment","entityId":"{{EntityId}}"}'::jsonb),

  -- ░░ Bid hooks: assigned ░░
  ('BID_ASSIGNED','Email','Bid assigned',
   'You were assigned to "{{BidTitle}}"',
   '<div style="font-family:-apple-system,Segoe UI,Roboto,Arial,sans-serif;max-width:560px;margin:0 auto;padding:24px;"><div style="font-size:15px;font-weight:700;color:#16263F;margin-bottom:16px;">Tenders<span style="color:#13B07C;">Agent</span></div><h1 style="font-size:18px;color:#16263F;margin:0 0 8px;">New bid assignment</h1><p style="font-size:14px;color:#5b6b7b;line-height:21px;margin:0 0 18px;">Hi {{FirstName}}, {{AssignedByName}} assigned you to the bid <b>{{BidTitle}}</b>.{{#if DueText}} It is due {{DueText}}.{{/if}}</p><a href="{{Link}}" style="display:inline-block;background:#13B07C;color:#fff;font-size:14px;font-weight:600;text-decoration:none;padding:10px 22px;border-radius:8px;">Open bid</a></div>',
   'Html','{}'::jsonb),
  ('BID_ASSIGNED','InApp','Bid assigned (in-app)','New bid assignment','{{AssignedByName}} assigned you to "{{BidTitle}}".','Text',
   '{"orgId":"{{OrgId}}","type":"team","entityType":"bid","entityId":"{{EntityId}}"}'::jsonb),

  -- ░░ Bid hooks: stage change ░░
  ('BID_STAGE_CHANGED','Email','Bid stage changed',
   '"{{BidTitle}}" moved to {{ToStage}}',
   '<div style="font-family:-apple-system,Segoe UI,Roboto,Arial,sans-serif;max-width:560px;margin:0 auto;padding:24px;"><div style="font-size:15px;font-weight:700;color:#16263F;margin-bottom:16px;">Tenders<span style="color:#13B07C;">Agent</span></div><h1 style="font-size:18px;color:#16263F;margin:0 0 8px;">Bid stage updated</h1><p style="font-size:14px;color:#5b6b7b;line-height:21px;margin:0 0 18px;">Hi {{FirstName}}, the bid <b>{{BidTitle}}</b> moved from {{FromStage}} to <b>{{ToStage}}</b>.</p><a href="{{Link}}" style="display:inline-block;background:#13B07C;color:#fff;font-size:14px;font-weight:600;text-decoration:none;padding:10px 22px;border-radius:8px;">Open bid</a></div>',
   'Html','{}'::jsonb),
  ('BID_STAGE_CHANGED','InApp','Bid stage changed (in-app)','Bid stage updated','"{{BidTitle}}" moved to {{ToStage}}.','Text',
   '{"orgId":"{{OrgId}}","type":"system","entityType":"bid","entityId":"{{EntityId}}"}'::jsonb),

  -- ░░ Bid hooks: won ░░
  ('BID_WON','Email','Bid won',
   'Bid won: "{{BidTitle}}"',
   '<div style="font-family:-apple-system,Segoe UI,Roboto,Arial,sans-serif;max-width:560px;margin:0 auto;padding:24px;"><div style="font-size:15px;font-weight:700;color:#16263F;margin-bottom:16px;">Tenders<span style="color:#13B07C;">Agent</span></div><h1 style="font-size:18px;color:#0F6E56;margin:0 0 8px;">Bid won</h1><p style="font-size:14px;color:#5b6b7b;line-height:21px;margin:0 0 18px;">Hi {{FirstName}}, congratulations — the bid <b>{{BidTitle}}</b> was marked won.{{#if WonValue}} Value: &#8377;{{WonValue}}.{{/if}}</p><a href="{{Link}}" style="display:inline-block;background:#13B07C;color:#fff;font-size:14px;font-weight:600;text-decoration:none;padding:10px 22px;border-radius:8px;">Open bid</a></div>',
   'Html','{}'::jsonb),
  ('BID_WON','InApp','Bid won (in-app)','Bid won','Bid won: "{{BidTitle}}".','Text',
   '{"orgId":"{{OrgId}}","type":"system","entityType":"bid","entityId":"{{EntityId}}"}'::jsonb),

  -- ░░ Bid hooks: lost ░░
  ('BID_LOST','Email','Bid lost',
   'Bid lost: "{{BidTitle}}"',
   '<div style="font-family:-apple-system,Segoe UI,Roboto,Arial,sans-serif;max-width:560px;margin:0 auto;padding:24px;"><div style="font-size:15px;font-weight:700;color:#16263F;margin-bottom:16px;">Tenders<span style="color:#13B07C;">Agent</span></div><h1 style="font-size:18px;color:#16263F;margin:0 0 8px;">Bid lost</h1><p style="font-size:14px;color:#5b6b7b;line-height:21px;margin:0 0 18px;">Hi {{FirstName}}, the bid <b>{{BidTitle}}</b> was marked lost.{{#if LossReason}} Reason: {{LossReason}}.{{/if}}</p><a href="{{Link}}" style="display:inline-block;background:#13B07C;color:#fff;font-size:14px;font-weight:600;text-decoration:none;padding:10px 22px;border-radius:8px;">Open bid</a></div>',
   'Html','{}'::jsonb),
  ('BID_LOST','InApp','Bid lost (in-app)','Bid lost','Bid lost: "{{BidTitle}}".','Text',
   '{"orgId":"{{OrgId}}","type":"system","entityType":"bid","entityId":"{{EntityId}}"}'::jsonb),

  -- ░░ Bid hooks: comment ░░
  ('BID_COMMENT','Email','Bid comment',
   'New comment on "{{BidTitle}}"',
   '<div style="font-family:-apple-system,Segoe UI,Roboto,Arial,sans-serif;max-width:560px;margin:0 auto;padding:24px;"><div style="font-size:15px;font-weight:700;color:#16263F;margin-bottom:16px;">Tenders<span style="color:#13B07C;">Agent</span></div><h1 style="font-size:18px;color:#16263F;margin:0 0 8px;">New comment</h1><p style="font-size:14px;color:#5b6b7b;line-height:21px;margin:0 0 18px;">Hi {{FirstName}}, {{AuthorName}} commented on <b>{{BidTitle}}</b>: &ldquo;{{Snippet}}&rdquo;</p><a href="{{Link}}" style="display:inline-block;background:#13B07C;color:#fff;font-size:14px;font-weight:600;text-decoration:none;padding:10px 22px;border-radius:8px;">Open bid</a></div>',
   'Html','{}'::jsonb),
  ('BID_COMMENT','InApp','Bid comment (in-app)','New comment','{{AuthorName}} commented on "{{BidTitle}}".','Text',
   '{"orgId":"{{OrgId}}","type":"team","entityType":"bid","entityId":"{{EntityId}}"}'::jsonb),

  -- ░░ Order received ░░
  ('ORDER_RECEIVED','Email','Order received',
   'New order received{{#if OrderRef}}: {{OrderRef}}{{/if}}',
   '<div style="font-family:-apple-system,Segoe UI,Roboto,Arial,sans-serif;max-width:560px;margin:0 auto;padding:24px;"><div style="font-size:15px;font-weight:700;color:#16263F;margin-bottom:16px;">Tenders<span style="color:#13B07C;">Agent</span></div><h1 style="font-size:18px;color:#0F6E56;margin:0 0 8px;">New order received</h1><p style="font-size:14px;color:#5b6b7b;line-height:21px;margin:0 0 18px;">Hi {{FirstName}}, a new order{{#if OrderRef}} <b>{{OrderRef}}</b>{{/if}}{{#if BuyerOrg}} from {{BuyerOrg}}{{/if}}{{#if Amount}} worth &#8377;{{Amount}}{{/if}} was recorded.</p><a href="{{Link}}" style="display:inline-block;background:#13B07C;color:#fff;font-size:14px;font-weight:600;text-decoration:none;padding:10px 22px;border-radius:8px;">View order</a></div>',
   'Html','{}'::jsonb),
  ('ORDER_RECEIVED','InApp','Order received (in-app)','New order received','New order{{#if OrderRef}} {{OrderRef}}{{/if}}{{#if BuyerOrg}} from {{BuyerOrg}}{{/if}}.','Text',
   '{"orgId":"{{OrgId}}","type":"order_alert","entityType":"order","entityId":"{{EntityId}}"}'::jsonb),

  -- ░░ Member role changed ░░
  ('MEMBER_ROLE_CHANGED','Email','Member role changed',
   'Your role in {{OrgName}} changed to {{NewRole}}',
   '<div style="font-family:-apple-system,Segoe UI,Roboto,Arial,sans-serif;max-width:560px;margin:0 auto;padding:24px;"><div style="font-size:15px;font-weight:700;color:#16263F;margin-bottom:16px;">Tenders<span style="color:#13B07C;">Agent</span></div><h1 style="font-size:18px;color:#16263F;margin:0 0 8px;">Your role changed</h1><p style="font-size:14px;color:#5b6b7b;line-height:21px;margin:0 0 18px;">Hi {{FirstName}}, {{ChangedByName}} changed your role in <b>{{OrgName}}</b> to <b>{{NewRole}}</b>.</p><a href="{{Link}}" style="display:inline-block;background:#13B07C;color:#fff;font-size:14px;font-weight:600;text-decoration:none;padding:10px 22px;border-radius:8px;">Open workspace</a></div>',
   'Html','{}'::jsonb),
  ('MEMBER_ROLE_CHANGED','InApp','Member role changed (in-app)','Role updated','Your role in {{OrgName}} is now {{NewRole}}.','Text',
   '{"orgId":"{{OrgId}}","type":"team"}'::jsonb),

  -- ░░ Weekly org digest ░░
  ('ORG_WEEKLY_DIGEST','Email','Weekly summary',
   'Your weekly tender summary',
   '<div style="font-family:-apple-system,Segoe UI,Roboto,Arial,sans-serif;max-width:560px;margin:0 auto;padding:24px;"><div style="font-size:15px;font-weight:700;color:#16263F;margin-bottom:16px;">Tenders<span style="color:#13B07C;">Agent</span></div><h1 style="font-size:18px;color:#16263F;margin:0 0 8px;">This week at {{OrgName}}</h1><p style="font-size:14px;color:#5b6b7b;line-height:21px;margin:0 0 18px;">Hi {{FirstName}}, here is where things stand: <b>{{OpenBids}}</b> open bids, <b>{{DueThisWeek}}</b> due this week, <b>{{OverdueBids}}</b> overdue, and <b>{{WonThisMonth}}</b> won this month.</p><a href="{{Link}}" style="display:inline-block;background:#13B07C;color:#fff;font-size:14px;font-weight:600;text-decoration:none;padding:10px 22px;border-radius:8px;">Open dashboard</a></div>',
   'Html','{}'::jsonb),
  ('ORG_WEEKLY_DIGEST','InApp','Weekly summary (in-app)','Weekly summary','{{OpenBids}} open bids, {{DueThisWeek}} due this week, {{OverdueBids}} overdue.','Text',
   '{"orgId":"{{OrgId}}","type":"system"}'::jsonb),

  -- ░░ Tender corrigendum / amendment ░░
  ('TENDER_AMENDED','Email','Tender amended',
   'Tender amended: "{{TenderTitle}}"',
   '<div style="font-family:-apple-system,Segoe UI,Roboto,Arial,sans-serif;max-width:560px;margin:0 auto;padding:24px;"><div style="font-size:15px;font-weight:700;color:#16263F;margin-bottom:16px;">Tenders<span style="color:#13B07C;">Agent</span></div><h1 style="font-size:18px;color:#854F0B;margin:0 0 8px;">A tender you are bidding on changed</h1><p style="font-size:14px;color:#5b6b7b;line-height:21px;margin:0 0 18px;">Hi {{FirstName}}, <b>{{TenderTitle}}</b> was amended. {{ChangeText}} Review the changes before they affect your bid.</p><a href="{{Link}}" style="display:inline-block;background:#13B07C;color:#fff;font-size:14px;font-weight:600;text-decoration:none;padding:10px 22px;border-radius:8px;">View tender</a></div>',
   'Html','{}'::jsonb),
  ('TENDER_AMENDED','InApp','Tender amended (in-app)','Tender amended','"{{TenderTitle}}" was amended. {{ChangeText}}','Text',
   '{"orgId":"{{OrgId}}","type":"tender_alert","entityType":"tender","entityId":"{{EntityId}}"}'::jsonb)
ON CONFLICT ON CONSTRAINT uq_template_code_channel DO NOTHING;

-- ── 3. Make the existing TENDER_MATCH InApp digest persist to the inbox ──────
-- It previously only carried actionUrl (the old mock InApp sender ignored everything).
-- Add orgId/type so the real sender can write a user_notifications row. The publisher
-- (MatchingService) now includes OrgId in the payload.
UPDATE notification_templates
   SET metadata   = '{"orgId":"{{OrgId}}","type":"tender_alert"}'::jsonb,
       updated_at = now()
 WHERE code = 'TENDER_MATCH' AND channel = 'InApp';
