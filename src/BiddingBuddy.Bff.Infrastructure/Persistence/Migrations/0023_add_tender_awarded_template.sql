-- 0023_add_tender_awarded_template
-- Seeds the TENDER_AWARDED notification template (Email + InApp) used by the tender-results
-- feature: InternalPipelineService.OnTenderAwardedAsync → INotificationPublisher → BidProcessor
-- notification workers. Fired when a GeM tender an org tracked / bid on is awarded.
--
-- Payload keys (from OnTenderAwardedAsync): FirstName, TenderTitle, WinnerName, WinningValue,
-- ParticipantCount, Outcome (won|lost|tracked), OrgId, EntityId, Link.
--
-- Idempotent: ON CONFLICT (code, channel) DO NOTHING — safe to re-run.

INSERT INTO notification_templates (code, channel, name, subject, body, body_format, metadata)
VALUES
  ('TENDER_AWARDED', 'Email',
   'Tender awarded',
   'Result out: {{TenderTitle}}',
   '<p>Hi {{FirstName}},</p>'
   || '<p>A tender you tracked has been <b>awarded</b>.</p>'
   || '<table cellpadding="6" style="border-collapse:collapse">'
   || '<tr><td><b>Tender</b></td><td>{{TenderTitle}}</td></tr>'
   || '<tr><td><b>Winner (L1)</b></td><td>{{WinnerName}}</td></tr>'
   || '<tr><td><b>Winning value</b></td><td>{{WinningValue}}</td></tr>'
   || '<tr><td><b>Participants</b></td><td>{{ParticipantCount}}</td></tr>'
   || '<tr><td><b>Your outcome</b></td><td>{{Outcome}}</td></tr>'
   || '</table>'
   || '<p><a href="{{Link}}">View the full result and price ladder →</a></p>'
   || '<hr><p style="color:#64748b;font-size:12px">Winning value is the L1 offered price from GeM''s '
   || 'public result view, not necessarily the final contract value.</p>',
   'Html',
   '{}'::jsonb),

  ('TENDER_AWARDED', 'InApp',
   'Tender awarded',
   'Result out: {{TenderTitle}}',
   'Awarded to {{WinnerName}} at {{WinningValue}} ({{ParticipantCount}} bidders). Your outcome: {{Outcome}}.',
   'Text',
   '{"orgId":"{{OrgId}}","type":"tender_alert","entityType":"tender","entityId":"{{EntityId}}","actionUrl":"{{Link}}"}'::jsonb)
ON CONFLICT ON CONSTRAINT uq_template_code_channel DO NOTHING;
