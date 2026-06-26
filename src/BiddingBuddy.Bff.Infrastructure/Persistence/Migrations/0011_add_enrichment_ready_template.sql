-- 0011_add_enrichment_ready_template
-- "Your AI analysis is ready" notification, sent when a tender a customer paid to
-- enrich finishes enrichment. Email + InApp. Renders against the payload
-- InternalPipelineService emits: FirstName, Title, Category, Url.
--
-- Handlebars.Net (logic-less): {{Var}}, {{#if}}. metadata string values are rendered too.
-- Idempotent: ON CONFLICT DO NOTHING (re-running is a no-op).

INSERT INTO notification_templates (code, channel, name, subject, body, body_format, metadata)
VALUES
  ('TENDER_ENRICHMENT_READY', 'Email',
   'AI analysis ready email',
   'Your AI analysis for "{{Title}}" is ready',
   '<div style="display:none;max-height:0;overflow:hidden;opacity:0;">Your AI analysis is ready on BiddingBuddy</div>
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f4f6f8;margin:0;padding:24px 0;font-family:-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;">
  <tr><td align="center">
    <table role="presentation" width="600" cellpadding="0" cellspacing="0" style="max-width:600px;width:100%;background:#ffffff;border:1px solid #e6e9ee;border-radius:12px;overflow:hidden;">
      <tr><td style="background:#0066FF;padding:20px 28px;">
        <span style="color:#ffffff;font-size:18px;font-weight:700;letter-spacing:.2px;">BiddingBuddy</span>
        <span style="color:#cfe0ff;font-size:12px;float:right;padding-top:5px;">AI analysis</span>
      </td></tr>
      <tr><td style="padding:28px 28px 8px;">
        <h1 style="margin:0 0 6px;font-size:20px;line-height:26px;color:#0b1620;">Hi {{FirstName}}, your AI analysis is ready</h1>
        <p style="margin:0;font-size:14px;line-height:21px;color:#5b6b7b;">We have finished analysing the tender you unlocked{{#if Category}} ({{Category}}){{/if}}. Eligibility, risk and win-strategy insights are now available.</p>
      </td></tr>
      <tr><td style="padding:14px 28px 4px;">
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin:0 0 12px;border:1px solid #e6e9ee;border-radius:10px;">
          <tr><td style="padding:14px 16px;">
            <a href="{{Url}}" style="display:inline-block;font-size:15px;font-weight:600;line-height:20px;color:#0066FF;text-decoration:none;">{{Title}}</a>
          </td></tr>
        </table>
      </td></tr>
      <tr><td align="center" style="padding:10px 28px 28px;">
        <a href="{{Url}}" style="display:inline-block;background:#0066FF;color:#ffffff;font-size:14px;font-weight:600;text-decoration:none;padding:12px 30px;border-radius:8px;">View AI analysis</a>
      </td></tr>
      <tr><td style="padding:18px 28px;background:#fafbfc;border-top:1px solid #eef1f4;">
        <p style="margin:0;font-size:12px;line-height:18px;color:#8a98a6;">You are receiving this because your organization unlocked AI analysis for this tender on BiddingBuddy.</p>
      </td></tr>
    </table>
  </td></tr>
</table>',
   'Html',
   '{}'::jsonb),

  ('TENDER_ENRICHMENT_READY', 'InApp',
   'AI analysis ready in-app',
   'AI analysis ready',
   'Your AI analysis for "{{Title}}" is ready — tap to view eligibility, risk and win strategy.',
   'Text',
   '{"actionUrl":"{{Url}}"}'::jsonb)
ON CONFLICT ON CONSTRAINT uq_template_code_channel DO NOTHING;
