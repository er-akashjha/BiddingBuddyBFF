-- 0005_improve_tender_match_template
-- Replaces the placeholder TENDER_MATCH bodies seeded in 0004 with a polished,
-- email-client-safe HTML template (Email) + a tighter in-app line (InApp).
--
-- Renders against the payload MatchingService emits:
--   FirstName, Count, One (bool), FirstTitle,
--   Tenders[] { Title, Category, State, Value, ClosingDate, Url }, AllUrl
--
-- Handlebars.Net (logic-less): {{Var}}, {{#each}}, {{#if}}, {{#unless}}.
-- Bumping updated_at invalidates the processor's template cache.
-- Idempotent: re-running just re-sets the same values.

UPDATE notification_templates SET
  subject = '{{Count}} new tender{{#unless One}}s{{/unless}} matching your interests',
  body_format = 'Html',
  body = '<div style="display:none;max-height:0;overflow:hidden;opacity:0;">{{Count}} new tender{{#unless One}}s{{/unless}} match your saved interests on BiddingBuddy</div>
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f4f6f8;margin:0;padding:24px 0;font-family:-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;">
  <tr><td align="center">
    <table role="presentation" width="600" cellpadding="0" cellspacing="0" style="max-width:600px;width:100%;background:#ffffff;border:1px solid #e6e9ee;border-radius:12px;overflow:hidden;">
      <tr><td style="background:#0066FF;padding:20px 28px;">
        <span style="color:#ffffff;font-size:18px;font-weight:700;letter-spacing:.2px;">BiddingBuddy</span>
        <span style="color:#cfe0ff;font-size:12px;float:right;padding-top:5px;">Tender alerts</span>
      </td></tr>
      <tr><td style="padding:28px 28px 8px;">
        <h1 style="margin:0 0 6px;font-size:20px;line-height:26px;color:#0b1620;">Hi {{FirstName}}, {{Count}} new match{{#unless One}}es{{/unless}} for you</h1>
        <p style="margin:0;font-size:14px;line-height:21px;color:#5b6b7b;">These newly published tenders match your saved interests, sorted by closing date. Act on the ones at the top first.</p>
      </td></tr>
      <tr><td style="padding:14px 28px 4px;">
        {{#each Tenders}}
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin:0 0 12px;border:1px solid #e6e9ee;border-radius:10px;">
          <tr><td style="padding:14px 16px;">
            <a href="{{Url}}" style="display:inline-block;font-size:15px;font-weight:600;line-height:20px;color:#0066FF;text-decoration:none;">{{Title}}</a>
            <p style="margin:7px 0 0;font-size:13px;line-height:18px;color:#5b6b7b;">{{#if Category}}{{Category}}{{/if}}{{#if State}} &middot; {{State}}{{/if}}{{#if Value}} &middot; &#8377;{{Value}}{{/if}}</p>
            <p style="margin:5px 0 0;font-size:12px;line-height:16px;color:#8a98a6;">Closes {{ClosingDate}}</p>
          </td></tr>
        </table>
        {{/each}}
      </td></tr>
      <tr><td align="center" style="padding:10px 28px 28px;">
        <a href="{{AllUrl}}" style="display:inline-block;background:#0066FF;color:#ffffff;font-size:14px;font-weight:600;text-decoration:none;padding:12px 30px;border-radius:8px;">View all matched tenders</a>
      </td></tr>
      <tr><td style="padding:18px 28px;background:#fafbfc;border-top:1px solid #eef1f4;">
        <p style="margin:0;font-size:12px;line-height:18px;color:#8a98a6;">You are receiving this because your organization set up tender interests on BiddingBuddy. Manage your interests and digest frequency in <a href="{{AllUrl}}" style="color:#0066FF;text-decoration:none;">Settings</a>.</p>
      </td></tr>
    </table>
  </td></tr>
</table>',
  updated_at = now()
WHERE code = 'TENDER_MATCH' AND channel = 'Email';

UPDATE notification_templates SET
  subject = '{{Count}} new tender{{#unless One}}s{{/unless}} matching your interests',
  body_format = 'Text',
  body = '{{Count}} new tender{{#unless One}}s{{/unless}} match your interests, including {{FirstTitle}}. Tap to review and act before they close.',
  metadata = '{"actionUrl":"/tenders?matched=1"}'::jsonb,
  updated_at = now()
WHERE code = 'TENDER_MATCH' AND channel = 'InApp';
