-- 0027_add_tender_match_deep_link
-- TENDER_MATCH alerts navigate nowhere when clicked. Gives the InApp template (and the
-- Firebase clone of it) the entity metadata the clients build their deep link from.
--
-- How it broke: 0012 set metadata to {"actionUrl":"/tenders?matched=1"}, which nothing ever
-- read — user_notifications has no action_url column, and the SPA builds the link purely from
-- entity_type/entity_id (entityUrl() in NotificationsContext.tsx). 0015 then UPDATEd the
-- metadata wholesale to {orgId,type} so the digest would persist to the inbox at all; every
-- row has landed with entity_type NULL since, so entityUrl() returns undefined and the row
-- is not clickable. Shape below matches TENDER_AMENDED (0015) — the closest sibling: same
-- tender_alert type, same entity.
--
-- Keys are lowercase on purpose. InAppNotificationSender reads md.TryGetValue("entityType")
-- over a plain Dictionary<string,string> (ordinal, case-sensitive — HandlebarsTemplateRenderer
-- keys it by the raw JSON property name), so "EntityType" would silently miss and leave
-- entity_type NULL, i.e. reproduce this exact bug.
--
-- entityId renders EMPTY in two cases, both safe and intended: a multi-tender digest (one
-- notification covers N tenders — there is no single entity), and portals whose
-- mongo_tender_id is not Guid-shaped (user_notifications.entity_id is uuid). The sender's
-- Guid.TryParse drops both to NULL and entityUrl() falls back to the /tenders list — the
-- same Guid.TryParse-or-empty contract the other tender templates use via
-- InternalPipelineService. MatchingService supplies EntityId only for a single-tender digest.
--
-- Both channels move in one statement: 0020 seeds the Firebase (push) row by cloning InApp's
-- metadata, so that clone carries the same gap — and as an INSERT … ON CONFLICT DO NOTHING
-- that has already run, it will never re-clone the fix. The mobile app reads entityType/
-- entityId straight out of the FCM data payload (deepLinks.ts), so push taps need it too.
-- If 0020 has not been applied yet it still runs first (ascending filename order) and this
-- statement then corrects both rows.
--
-- Idempotent: a forward UPDATE re-setting the same values. Bumping updated_at invalidates the
-- processor's compiled-template cache (keyed by code|channel|updated_at).

UPDATE notification_templates
   SET metadata   = '{"orgId":"{{OrgId}}","type":"tender_alert","entityType":"tender","entityId":"{{EntityId}}"}'::jsonb,
       updated_at = now()
 WHERE code = 'TENDER_MATCH'
   AND channel IN ('InApp', 'Firebase');
