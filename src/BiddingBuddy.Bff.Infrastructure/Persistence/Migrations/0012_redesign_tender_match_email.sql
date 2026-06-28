-- 0012_redesign_tender_match_email
-- WS4 — redesigned TENDER_MATCH email (the "deadline-first" concept).
--
-- Replaces the off-brand blue card layout with the official TendersAgent palette
-- (navy #16263F + green #13B07C), adds the logo, and renders a grouped, ranked
-- digest with "days left" urgency badges. Forward migration (UPDATE on the live
-- rows seeded in 0004/0005 and rebranded by 0009) — editing those in place would
-- not re-apply on databases that already ran them. Bumping updated_at invalidates
-- the processor's Handlebars template cache.
--
-- Renders against the payload MatchingService.DispatchDigestAsync now emits:
--   FirstName, Count, One (bool), FirstTitle, LogoUrl, SummaryLine,
--   ShowTotal (bool), TotalValue, AllUrl,
--   Tenders[] { Rank, Title, Url, Category, State, Value, DaysLeftLabel, IsUrgent (bool) }
--
-- Handlebars.Net (logic-less): {{Var}}, {{#each}}, {{#if}}{{else}}, {{#unless}}.
-- Idempotent: re-running just re-sets the same values.

UPDATE notification_templates SET
  subject = '{{Count}} new tender{{#unless One}}s{{/unless}} matching your interests',
  body_format = 'Html',
  body = '<div style="display:none;max-height:0;overflow:hidden;opacity:0;">{{Count}} new tender{{#unless One}}s{{/unless}} match your saved interests on TendersAgent</div>
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#eef1f5;margin:0;padding:24px 0;font-family:-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;">
  <tr><td align="center">
    <table role="presentation" width="600" cellpadding="0" cellspacing="0" style="max-width:600px;width:100%;background:#ffffff;border:1px solid #e6e9ee;border-radius:14px;overflow:hidden;">
      <tr><td style="background:#16263F;padding:18px 26px;">
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr>
          <td style="vertical-align:middle;">
            <img src="{{LogoUrl}}" width="28" height="28" alt="TendersAgent" style="display:inline-block;vertical-align:middle;border:0;" />
            <span style="display:inline-block;vertical-align:middle;margin-left:9px;font-size:18px;font-weight:600;color:#ffffff;letter-spacing:-0.3px;">Tenders<span style="color:#36C892;">Agent</span></span>
          </td>
          <td style="vertical-align:middle;text-align:right;"><span style="font-size:12px;color:#9fb2cc;">Tender alerts</span></td>
        </tr></table>
      </td></tr>
      <tr><td style="padding:24px 26px 0;">
        <h1 style="margin:0 0 6px;font-size:20px;line-height:26px;color:#16263F;font-weight:600;">{{Count}} new tender{{#unless One}}s{{/unless}} to act on</h1>
        <p style="margin:0;font-size:14px;line-height:21px;color:#5b6b7b;">Hi {{FirstName}}, these match your saved interests, ranked by how soon they close. Start at the top.</p>
      </td></tr>
      <tr><td style="padding:14px 26px 0;">
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#E1F5EE;border-radius:8px;"><tr><td style="padding:10px 14px;font-size:13px;color:#0F6E56;font-weight:600;">{{SummaryLine}}{{#if ShowTotal}}<span style="font-weight:400;"> &middot; total value &#8377;{{TotalValue}}</span>{{/if}}</td></tr></table>
      </td></tr>
      <tr><td style="padding:8px 26px 4px;">
        {{#each Tenders}}
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="border-top:1px solid #eef1f4;"><tr>
          <td style="vertical-align:top;width:34px;padding:15px 0;"><span style="display:inline-block;width:24px;height:24px;border-radius:12px;background:#16263F;color:#ffffff;font-size:12px;font-weight:600;text-align:center;line-height:24px;">{{Rank}}</span></td>
          <td style="vertical-align:top;padding:15px 0;">
            <a href="{{Url}}" style="font-size:15px;font-weight:600;color:#16263F;text-decoration:none;line-height:20px;">{{Title}}</a>
            <div style="margin-top:5px;font-size:13px;color:#5b6b7b;">{{#if Category}}{{Category}}{{/if}}{{#if State}} &middot; {{State}}{{/if}}{{#if Value}} &middot; &#8377;{{Value}}{{/if}}</div>
          </td>
          <td style="vertical-align:top;text-align:right;white-space:nowrap;padding:15px 0 15px 12px;">{{#if IsUrgent}}<span style="display:inline-block;font-size:12px;font-weight:600;color:#854F0B;background:#FAEEDA;padding:4px 10px;border-radius:20px;">{{DaysLeftLabel}}</span>{{else}}<span style="display:inline-block;font-size:12px;font-weight:600;color:#0F6E56;background:#E1F5EE;padding:4px 10px;border-radius:20px;">{{DaysLeftLabel}}</span>{{/if}}</td>
        </tr></table>
        {{/each}}
      </td></tr>
      <tr><td align="center" style="padding:20px 26px 26px;">
        <a href="{{AllUrl}}" style="display:inline-block;background:#13B07C;color:#ffffff;font-size:14px;font-weight:600;text-decoration:none;padding:12px 32px;border-radius:8px;">View all matched tenders</a>
      </td></tr>
      <tr><td style="padding:18px 26px;background:#fafbfc;border-top:1px solid #eef1f4;">
        <p style="margin:0;font-size:12px;line-height:18px;color:#8a98a6;">You are receiving this because your organisation set up tender interests on TendersAgent. Manage interests, change frequency, or <a href="{{AllUrl}}" style="color:#13B07C;text-decoration:none;">update your settings</a>.</p>
      </td></tr>
    </table>
  </td></tr>
</table>',
  updated_at = now()
WHERE code = 'TENDER_MATCH' AND channel = 'Email';

UPDATE notification_templates SET
  subject = '{{Count}} new tender{{#unless One}}s{{/unless}} matching your interests',
  body_format = 'Text',
  body = '{{Count}} new tender{{#unless One}}s{{/unless}} match your interests on TendersAgent, including "{{FirstTitle}}". Tap to review and act before they close.',
  metadata = '{"actionUrl":"/tenders?matched=1"}'::jsonb,
  updated_at = now()
WHERE code = 'TENDER_MATCH' AND channel = 'InApp';
