-- 0016_add_contact_form_template
-- Seeds the CONTACT_FORM email template used by the public marketing "Contact us"
-- form (PublicContactController → INotificationPublisher → BidProcessor email worker).
-- The form is delivered as an email to the team inbox (config ContactForm:Recipient);
-- the visitor's own details live in the rendered body.
--
-- Idempotent: ON CONFLICT (code, channel) DO NOTHING — safe to re-run.

INSERT INTO notification_templates (code, channel, name, subject, body, body_format, metadata)
VALUES
  ('CONTACT_FORM', 'Email',
   'Contact form submission',
   'New contact form message from {{Name}}',
   '<p>A new message was submitted through the Tenders Agent contact form.</p>'
   || '<table cellpadding="6" style="border-collapse:collapse">'
   || '<tr><td><b>Name</b></td><td>{{Name}}</td></tr>'
   || '<tr><td><b>Email</b></td><td>{{Email}}</td></tr>'
   || '<tr><td><b>Company</b></td><td>{{Company}}</td></tr>'
   || '<tr><td><b>Submitted</b></td><td>{{SubmittedAt}}</td></tr>'
   || '</table>'
   || '<p><b>Message</b></p><p>{{Message}}</p>'
   || '<hr><p style="color:#64748b;font-size:12px">Reply directly to {{Email}} to respond to this enquiry.</p>',
   'Html',
   '{}'::jsonb)
ON CONFLICT ON CONSTRAINT uq_template_code_channel DO NOTHING;
