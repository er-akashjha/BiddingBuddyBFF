-- 0025_add_competitor_won_template
-- Seeds the COMPETITOR_WON notification template (Email + InApp), fired by
-- InternalPipelineService.NotifyCompetitorWinAsync when a competitor an org tracks
-- (`competitors.company_name`) is the inferred winner of a GeM award.
--
-- This is the trigger the notification catalog listed as deferred ("competitor-spotted — no
-- trigger exists"): until the award feed, nothing in the product knew who won anything.
--
-- Payload keys (from NotifyCompetitorWinAsync): FirstName, CompetitorName, TenderTitle,
-- WinningValue, ParticipantCount, Category, OrgId, EntityId, Link.
--
-- Idempotent: ON CONFLICT (code, channel) DO NOTHING — safe to re-run.

INSERT INTO notification_templates (code, channel, name, subject, body, body_format, metadata)
VALUES
  ('COMPETITOR_WON', 'Email',
   'Competitor won a tender',
   '{{CompetitorName}} just won a tender',
   '<p>Hi {{FirstName}},</p>'
   || '<p>A competitor you track, <b>{{CompetitorName}}</b>, was the L1 (lowest qualified) bidder on a tender.</p>'
   || '<table cellpadding="6" style="border-collapse:collapse">'
   || '<tr><td><b>Tender</b></td><td>{{TenderTitle}}</td></tr>'
   || '<tr><td><b>Category</b></td><td>{{Category}}</td></tr>'
   || '<tr><td><b>Winning value</b></td><td>{{WinningValue}}</td></tr>'
   || '<tr><td><b>Participants</b></td><td>{{ParticipantCount}}</td></tr>'
   || '</table>'
   || '<p><a href="{{Link}}">See the full price ladder →</a></p>'
   || '<hr><p style="color:#64748b;font-size:12px">Winning value is the L1 offered price from GeM''s '
   || 'public result view, not necessarily the final contract value. The winner is inferred as the '
   || 'lowest qualified bidder — GeM does not stamp an explicit winner.</p>',
   'Html',
   '{}'::jsonb),

  ('COMPETITOR_WON', 'InApp',
   'Competitor won a tender',
   '{{CompetitorName}} won a tender',
   '{{CompetitorName}} was L1 at {{WinningValue}} on "{{TenderTitle}}" ({{ParticipantCount}} bidders).',
   'Text',
   '{"orgId":"{{OrgId}}","type":"competitor_alert","entityType":"tender","entityId":"{{EntityId}}","actionUrl":"{{Link}}"}'::jsonb)
ON CONFLICT ON CONSTRAINT uq_template_code_channel DO NOTHING;
