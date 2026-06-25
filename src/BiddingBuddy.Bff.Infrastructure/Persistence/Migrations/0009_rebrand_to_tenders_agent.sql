-- Rebrand: "BiddingBuddy" -> "Tenders Agent" in user-facing notification copy.
--
-- The brand name was seeded into notification_templates by earlier migrations
-- (0002 WELCOME/TEAM_INVITATION/PASSWORD_RESET/EMAIL_VERIFICATION, 0005 TENDER_MATCH,
-- 0006 EMAIL_VERIFICATION OTP, 0007 PASSWORD_RESET OTP). Those migrations have
-- already run on live databases, so editing them does nothing here — this forward
-- migration rewrites the rendered subject/body text in place.
--
-- Idempotent: after it runs, no row matches the WHERE clause, so a re-run is a no-op.

UPDATE notification_templates
SET subject = REPLACE(subject, 'BiddingBuddy', 'Tenders Agent'),
    body    = REPLACE(body,    'BiddingBuddy', 'Tenders Agent')
WHERE subject LIKE '%BiddingBuddy%'
   OR body    LIKE '%BiddingBuddy%';
