# Release Notes — BiddingBuddyBFF

Current version: **v25**

Convention: every change lands as a new `## vN — YYYY-MM-DD HH:mm IST` entry at the top (newest first). The counter increments by 1 per release, per repo.

---

## v25 — 2026-07-18 11:56 IST

**The sector picked at onboarding now actually does something.**

`organizations.primary_category` was write-only: set by onboarding, echoed in `OrgDetailDto` and
`/api/auth/me`, and read by no logic anywhere. Real alerting runs off `tender_alert_rules` +
`MatchingService`, which never looked at it. So the onboarding page's promise — *"We'll surface the
most relevant government tenders for you right away"* — resolved to nothing, and a new org got zero
tender alerts until someone found Settings → Interests and built a rule by hand.

`OrganizationService` now seeds **one category-only `tender_alert_rules` row** from the sector:

- on `POST /api/organizations`, when the payload carries a sector;
- on `PATCH /api/organizations/{id}`, only when the sector is **first** set — which is the path that
  matters for social signups, since `AuthService` creates the org with a name only and onboarding
  PATCHes the sector in afterwards.

**This is only safe because of ui v20.** The picker now emits the canonical 40-entry taxonomy
verbatim — the same vocabulary the pipeline assigns to `tenders.category`. `MatchingService` compares
categories with full-string `OrdinalIgnoreCase` (no substring, no stemming), so the free-form labels
the picker used to emit would have produced a rule matching **zero tenders, silently, forever**. Do
not reintroduce hand-typed sectors upstream of this.

**Idempotent by "org owns no rules yet".** `tender_alert_rules` has no unique constraint, so a
repeat call would otherwise just append a duplicate the user has to find and delete. Seeding is
skipped entirely if the org already owns any rule, and a later sector *change* does not re-seed —
once interests exist they are the user's to curate, and quietly widening someone's feed is worse
than doing nothing.

**Failure is non-fatal**, matching the notification-publish contract elsewhere in the class: the org
and sector are already committed, so a seeding error logs a warning and onboarding still succeeds.

### On the first digest — deliberately immediate, and it cannot flood

A brand-new org has no `org_alert_settings` row, so `last_digest_sent_at` is NULL and the 6 h
cooldown gate does not trip: the first digest goes out on the next scan tick (≤15 min) rather than
6 h later. That is the behaviour we want — it *is* the "right away" the page promises — and it is
bounded, because `ScanNewTendersAsync` only ever evaluates tenders with `alerts_scanned_at IS NULL`.
There is no backlog to blast, only newly-ingested tenders, and the recipient list for a fresh org is
just the owner. No settings row is created; the lazy defaults (enabled, 360 min, Email+InApp) are
already correct, and writing one would only duplicate them.

**Known limit — the flip side of the same mechanism.** Because matching is forward-only, an org
signing up at midday sees nothing until new tenders in its sector are ingested, i.e. after the
downloader's next nightly run. "Right away" is really "as soon as new tenders arrive". Closing that
gap means re-arming already-scanned tenders, which is a **global** operation
(`POST /internal/matching/scan?backfill=true` re-arms every tender for every org) — deliberately not
done here. A scoped `UPDATE tenders SET alerts_scanned_at = NULL WHERE category = '<sector>'` is the
narrower option, but it still re-arms that category for *all* orgs, so it needs its own decision.

No schema change — `tender_alert_rules` and `org_alert_settings` already exist (migrations `0004`,
`0011`). Pure data path; nothing to migrate.

Covered by `tests/BiddingBuddy.Bff.Tests/Orgs/StarterAlertRuleSeedingTests.cs` (9 tests: both seed
paths, category verbatim + no invented constraints, the three no-seed cases, and both idempotency
guards).

## v24 — 2026-07-18 11:39 IST

**Fix: every new signup got a 500 on "Create workspace" — migrations now apply on startup.**

`POST /api/organizations` failed for 100% of new users with `An error occurred while saving the
entity changes`, and the SPA showed "Could not create your workspace." Root cause was **migration
`0024_add_org_gem_seller_name.sql` never being applied in prod**. v22 added `GemSellerName` to the
`Organization` entity + configuration, so EF emits `gem_seller_name` in the INSERT column list
unconditionally (it has no `HasDefaultValueSql`) — Postgres answered `42703 undefined_column` and EF
wrapped it in the opaque `DbUpdateException` above. The payload was irrelevant: the UI never sends
the field. Onboarding was simply the first request to touch `organizations`; `GetAsync` does a full
entity load, so the read-back on line 54 of `OrganizationService` would have failed even if the
INSERT had succeeded.

Migrations were manual (`POST /internal/migrations` after each rebuild) and had been missed across
several releases — the v18/v22 notes flag `0023`/`0024` as outstanding. **The BFF now applies pending
migrations on startup**, before it serves traffic, so deploy and migrate are one step. This is safe
to run every boot: scripts are idempotent, ordered by filename, and each commits with its
`schema_migrations` row in a single transaction.

- Failure is **deliberately non-fatal** — it logs at `Fatal` and continues. A crash-looping container
  takes the whole site down; a schema that is behind only breaks the endpoints touching new columns.
- Opt out with `Database:AutoMigrateOnStartup=false` to gate a risky migration behind a manual window.

Deploying this build applies `0023`–`0027` automatically. No manual curl required.

## v23 — 2026-07-16 20:30 IST

**Fix: clearing the GeM seller name silently did nothing.** `UpdateAsync` assigns every field behind
`if (dto.X is not null)`, so `null` means "not supplied" for all fifteen of them — but the GeM
identity card (ui v16) sends `null` to mean *clear*. The clear was dropped, the endpoint returned
the **unchanged** org, and the SPA wrote the old value straight back into auth state and reported
success. The field looked empty until reload, then the old name came back.

It matters because that name is free text on GeM's side: a typo means "we never find you on a
ladder", and the org-name fallback in `MarketController`/`TendersController` only applies while the
field is blank. So a user who typo'd it — the very failure the card's "Check against GeM results"
button exists to surface — **could not clear it to get the default back**, short of retyping their
org name by hand. Until they did, their bids never auto-resolved to won/lost.

- **Blank, not null, is the clear signal** for `GemSellerId`/`GemSellerName`. Whitespace counts as
  blank (a cleared input is not a name), and values are trimmed — stray whitespace is invisible on
  screen but is a different `SellerKey`, i.e. the same silent "we never find you" by another route.
- **`null` still means "not supplied"** for every field on `UpdateOrgDto`, so a caller patching only
  the website cannot wipe an org's GeM identity. The convention is preserved, not replaced — this
  DTO has no way to distinguish omitted from explicit-null, and the two GeM fields are the only ones
  a user has a real reason to unset.
- Paired with **bidding-buddy-ui v19**, which sends blank instead of null. No migration.
- 7 tests in `Orgs/GemSellerIdentityUpdateTests.cs`; full suite green at **131**.

---

## v22 — 2026-07-16 20:02 IST

**Tender list + detail now report `bidKind`, so the UI can flag reverse auctions.** A production
check found 5,190 GeM reverse auctions that were indistinguishable from ordinary tenders — and
title-less, which made them look like broken records rather than a different kind of thing
(pipeline fix: BidProcessor v10).

- `TenderListItemDto.BidKind` + `TenderDetailDto.BidKind` (optional, trailing — existing callers
  unaffected), populated in `TenderDetailsTranslator` for both `ToListDto` and `ToDetailsDto`.
- `TenderSourceDto.BidKind` reads Mongo's new `source.bidKind` (Services v7).
- New `TenderKind.Resolve(stored, platformTenderId)` — **stored value wins; otherwise the kind is
  re-derived from the bid number**, which GeM has always encoded. That is what makes the badge work
  for the 5,190 tenders already in Mongo without waiting on a backfill.

**No migration.** Tender list and detail proxy Mongo via BiddingBuddyServices — Postgres `tenders`
is never read on this path — so the flag needs no column. Postgres would only be required if
`bidKind` had to drive interest matching or award resolution, which it doesn't today.

Not the same concept as `Commercial.ReverseAuction.Enabled`, which is AI-inferred, means "this bid
has an RA phase", and is unset on actual reverse auctions because the AI never runs on them.

**Side effect worth knowing:** reverse auctions now arrive with real titles, so `HasPlaceholderTitle`
no longer classifies them as stubs and their award mail is no longer suppressed. Recipients are
still scoped to bidders ∪ trackers/savers, so only people who engaged with the tender get mail.

## v21 — 2026-07-16 17:55 IST

**Fix: digest channels/roles that deliver nothing are rejected at save time instead of accepted** —
`PATCH /api/tender-alert-rules/settings` assigned `notify_channels`/`notify_roles` straight onto the
row with no validation, and both columns are bare `text[]` with no CHECK. But
`MatchingService.DispatchDigestAsync` only builds recipients for `Email` and `InApp`, and resolves
people via `roles.Contains(member.Role)` — so a value neither side implements doesn't error, it
yields zero recipients and `continue`s. An org that saved `["WhatsApp"]` or `["Sms"]` — both real
`NotificationChannel` constants, so they look legitimate — **silently stopped receiving tender
digests altogether**, with no error at save time and nothing in the UI to show it. No migration; the
guard is on the write path, so rows already in `org_alert_settings` are untouched.

- **`UpdateSettingsAsync` validates both arrays** against `TenderDigestChannel.Supported` and
  `OrgAlertRole.All` (new, in `Core/DTOs/Alerts`), returning **400** through the existing
  `GlobalExceptionHandler` `ArgumentException` mapping — no controller change. The channel allowlist
  is deliberately **not** `NotificationChannel`: that class lists all five channels the notification
  subsystem knows about, so validating against it would have waved `Sms`/`WhatsApp`/`Firebase`
  through and kept the bug. It mirrors the branches dispatch actually implements.
- **Empty arrays are rejected too**, not just unknown values — `[]` is the one path the SPA can
  already reach today. The Interests tab lets you toggle both channel switches off, which saved `[]`
  and broke that org's digests on the spot. `isEnabled=false` is the real "stop sending" control,
  and the error message points at it.
- **Values are canonicalised, not merely checked.** Dispatch compares ordinally, so `"email"` would
  save happily and then match nothing — the same silent drop by another route. Casing is normalised
  to the constant (`email` → `Email`, `Owner` → `owner`), blanks dropped, duplicates collapsed.
  Channels are PascalCase while roles are lowercase, which is exactly what a hand-written call gets
  wrong.
- **Validation runs before any DB work**, so a rejected call leaves no half-applied row — a bad
  channel sent alongside a good `isEnabled` was previously prevented only by the throw happening to
  land before `SaveChangesAsync`.
- **Dispatch logs undeliverable channels instead of skipping in silence.** Save-time validation
  can't heal rows written before it, so `DispatchDigestAsync` now warns when an org's channels
  include something it doesn't implement. It's also the tripwire if `Supported` ever gains a channel
  without a matching dispatch branch.
- 24 tests in `Alerts/OrgAlertSettingsValidationTests.cs`, **19 of which fail against the previous
  code**. Full suite green at 111.

**Not in scope:** `docs/whatsapp-alerts/PLAN.md` plans to make `WhatsApp` genuinely deliverable
here. That lands by adding the dispatch branch and the `TenderDigestChannel.Supported` entry in the
same change; until then the value is rejected rather than accepted-and-dropped.

---

## v20 — 2026-07-16 21:50 IST

**Tender-match alerts are clickable again (migration `0027`)** — clicking a TENDER_MATCH row in the
Alert Center or the TopNav bell navigated nowhere. The SPA builds a notification's link purely from
`entity_type`/`entity_id` (`entityUrl()` in `NotificationsContext.tsx`); TENDER_MATCH landed with
`entity_type` NULL, so the row had no link to offer. Data-only fix — no schema change.

- **How it broke.** `0012` set the InApp template's metadata to `{"actionUrl":"/tenders?matched=1"}`,
  which nothing has ever read — `user_notifications` has no `action_url` column, and the SPA does not
  look at `actionUrl`. `0015` then overwrote the metadata wholesale with `{orgId,type}` so the digest
  would persist to the inbox at all, dropping the (already-dead) actionUrl and never adding an
  `entityType`. Every sibling seeded in `0015` carries one; TENDER_MATCH was the only tender template
  without.
- **`0027` sets `entityType: tender` + `entityId: {{EntityId}}`**, matching `TENDER_AMENDED`'s shape
  exactly — same `tender_alert` type, same entity. Keys are **lowercase deliberately**:
  `InAppNotificationSender` does a case-sensitive `md.TryGetValue("entityType")` over a plain
  `Dictionary<string,string>`, so an `EntityType` spelling would silently miss and leave
  `entity_type` NULL — i.e. reproduce this exact bug.
- **Updates the Firebase (push) row too, not just InApp.** `0020` seeds the push template by *cloning*
  InApp's metadata, so it inherited the same gap — and as an `INSERT … ON CONFLICT DO NOTHING` that
  has already run, it would never re-clone the fix. The mobile app reads `entityType`/`entityId`
  straight out of the FCM data payload (`deepLinks.ts`), so push taps needed it as well. Ordering is
  safe either way: `0020` sorts before `0027`, so a not-yet-applied `0020` still clones first and gets
  corrected in the same batch.
- **`MatchingService.DispatchDigestAsync` now supplies `EntityId`** — but only for a **single-tender**
  digest. A digest groups N tenders under one notification, so there is no single entity to name when
  N > 1; it renders empty and the clients fall back to the `/tenders` list. Empty is also what
  non-Guid-shaped `mongo_tender_id` values produce, since `user_notifications.entity_id` is `uuid` —
  the same `Guid.TryParse`-or-empty contract the other tender templates already use via
  `InternalPipelineService`. Both cases are safe: the sender's `Guid.TryParse` drops them to NULL.
- **Verified** by replaying the real template history (`0002` → `0012` → `0015` → `0020`) on a
  throwaway Postgres: reproduced `entity_type` NULL on both InApp and Firebase, applied `0027`
  (`UPDATE 2`), confirmed both rows end byte-identical to `TENDER_AMENDED`, and re-ran it to prove
  idempotency.
- **Deploy:** migrations are manual — `POST /internal/migrations` with `X-Api-Key` after the rebuild.
  Existing `user_notifications` rows are **not** backfilled; already-delivered alerts stay unclickable.

---

## v19 — 2026-07-16 21:45 IST

**Security: close a cross-tenant read in the document vault** — a member of org A could read org B's
files. `DocumentsController.RequestUploadUrl` builds the object key server-side
(`orgs/{orgId}/docs/{guid}/{file}`) precisely so a client can't choose it, but registration took
`dto.S3Key` back from the client and stored it verbatim. `POST /api/documents` with an `s3Key`
belonging to another org produced a row in *your* org that `GET /{id}/view-url` and
`/{id}/download-url` would then happily presign — the presign step only ever looks at the key on the
row, and the row passed its own org check. No migration; the guard is on the write path, so rows
already in `documents` are untouched.

- **`CreateDocumentAsync` now re-checks the org prefix**, the same guard
  `BidAttachmentService.RegisterAsync` already applies to bid attachments. Rejects a blank key, a key
  outside `orgs/{orgId}/docs/`, and — beyond the bid-attachment version — a key containing `..`,
  since `orgs/{mine}/docs/../../{theirs}/docs/x` passes a `StartsWith` test on its own.
- **`AddVersionAsync` got the same guard.** It trusted `dto.S3Key` identically. Not exploitable
  today only because nothing presigns a `document_versions` key — `GetVersionsAsync` returns the key
  string, not a URL. That's an accident of the current read surface, not a control; the obvious next
  feature ("download this version") would have made it live.
- **Folder ids are now checked against the caller's org** in `CreateFolderAsync`, `UpdateFolderAsync`,
  `CreateDocumentAsync`, and `UpdateDocumentAsync` (the last also moves a doc via `dto.FolderId`).
  Cross-org ids report **404, not 403**, so the response can't enumerate other orgs' folder ids.
- **`CreateFolderAsync` no longer swallows its own insert.** `SaveChangesAsync` was wrapped in
  `try { } catch (Exception er) { }` — an empty catch, and the solution's only build warning. A failed
  insert returned **200** with a `FolderDto` whose `Id` the SPA added to its sidebar, so the folder
  existed on screen and nowhere else until reload. Failures now surface. The likely original motive —
  an FK violation on a bogus `ParentId` — is what the ownership check above now answers cleanly.
- **`UpdateFolderAsync` rejects cycles.** A folder could be made its own parent or its own descendant,
  detaching the whole subtree from every root listing (`ListFolders` starts at `ParentId == null`) with
  no UI to recover it. The check walks up from the proposed parent, capped at
  `MaxFolderDepth = 64` — a cycle written *before* this guard would otherwise spin the walk forever, so
  the cap is load-bearing, not decorative.

Build clean (0 warnings); 87 tests green, 21 new in `Documents/DocumentServiceGuardTests.cs`. The 16
negative tests were confirmed to fail against the pre-fix service, and the 5 happy-path tests to pass.

---

## v18 — 2026-07-16 21:30 IST

**Award intelligence: market proxy, loss autopsy, and the competitor-won trigger**
(plan: root `docs/tender-results/UI-FEATURES.md`). **Needs migration `0025`.**

### Seller matching now uses the canonical key
`NormalizeSeller` stopped at casing/punctuation, so an org named "ABC Pvt Ltd" **never** matched a
ladder row printed "ABC Private Limited" — and won/lost resolution silently fell through to the
bid-value heuristic. It now delegates to the new `Services/SellerKey.cs`, the same algorithm
Services computes and indexes at ingest.
⚠ **KEEP IN SYNC** with `BiddingBuddyServices/src/BiddingBuddy.Core/Domain/SellerKey.cs` — if the two
drift, an org matches in market analytics but not in its own bid resolution.

### Loss autopsy — three failures, three different lessons
`BuildLossReason` no longer flattens everything to "Ranked Ln — awarded to X". The ladder
distinguishes:
- **disqualified** → a compliance/paperwork failure; price was never the issue, and cutting margin
  next time fixes nothing;
- **lost on preference** → L1 held an MSE/PMA advantage we don't have, so the gap may not be
  closeable on price at all;
- **outbid** → a real pricing loss, now quantified ("₹X / Y% below your bid").

Same taxonomy is exposed structurally to the SPA — see `TenderResultViewDto` below.

### `GET /api/tenders/{id}/result` now answers "where were WE?"
Returns `TenderResultViewDto` = the ladder + `YourRow` + `YourOutcome` (verdict, loss kind, rank,
gap, gap %). Resolved **server-side** by new `Services/TenderResultView.cs` because it needs
`SellerKey`: a TypeScript copy would be a third implementation that drifts, and a user's row would
highlight on one screen but not another. Identity = `gem_seller_name`, else the org name (the same
precedence the award pipeline uses, so the two never disagree about who we are).

### `MarketController` — the real intelligence surface
`pricing` (now percentile-based + MSE/RA rates), plus new `grouped`, `sellers`, `sellers/profile`,
`head-to-head` (the caller's org, 409 `SELLER_IDENTITY_NOT_SET` when unconfigured rather than a
misleading empty list), `head-to-head/{seller}`, `buyer`, `comparables`. All proxy Services' Mongo
aggregation — **no result data in Postgres**, per the standing principle.

### COMPETITOR_WON — the trigger that didn't exist
The notification catalog listed "competitor-spotted" as deferred *because no trigger existed*:
nothing in the product ever knew who won anything. The award feed is that trigger, and the
`competitors` table was always the audience. `NotifyCompetitorWinAsync` fires when a tracked
competitor is the inferred winner — matched on the normalized key, claimed once per (tender, org)
so a re-scrape can't re-send, suppressed for stub tenders (same reasoning as award mail), and
wrapped so it can never fail the pipeline. Template seeded by **migration `0025`**.

### Also
`/api/auth/me` now carries `GemSellerName` per org (like `PrimaryCategory`) — every award surface
needs it, and re-fetching the org on each would be silly.

Tests: `SellerKeyTests` + `TenderResultViewTests` (**+30**, 66 green). The seller-key suite caught a
real bug: "M S Dhoni Sports" was being mangled to "dhoni sports" because the `M/S` prefix check ran
*after* punctuation stripping, which made a Messrs prefix indistinguishable from genuine initials.
The prefix is now matched on the raw string, where the slash still disambiguates it.

---

## v17 — 2026-07-16 18:40 IST

**Bid documents: link org vault documents to a bid** — the bid's Documents tab had a real backend
(`bid_attachments`, since v?/BID-303) but no way to attach anything you'd already uploaded. The vault
(`/api/documents`) and bids were two disconnected file systems: `bid_attachments` owns its own
`storage_key` and has no `document_id`, so "attach an existing document" needed new schema either way.

- **New `bid_documents` link table** (migration `0026`) — `(org_id, bid_id, document_id, linked_by)`,
  UNIQUE on `(bid_id, document_id)`. A **link, not a copy**: `documents.folder_id` is a single FK, so
  filing a doc into a per-bid folder would move it out of GST/PAN *and* allow it on only one bid.
  Linking keeps one GST certificate in the vault serving every bid that needs it, so re-uploading it
  updates all of them. Both FKs cascade — unlinking is implicit when the bid or the document dies.
- **New endpoints on `BidsController`:**
  - `GET /api/bids/{id}/documents` — the bid's folder, newest first. A **union** of linked vault docs
    (`source: "vault"`) and task-completion attachments (`source: "attachment"`), discriminated by
    `source` because they download through different endpoints.
  - `POST /api/bids/{id}/documents` `{ documentId }` — link. Idempotent; re-linking returns the
    existing row. Scoping the document lookup to the caller's org is what blocks cross-org linking.
  - `DELETE /api/bids/{id}/documents/{documentId}` — unlink. The vault document is untouched.
- **Orphaned attachments are now reachable.** The list returns *every* `bid_attachments` row for the
  bid, not just the comment-linked ones the Notes feed shows. Only `CompleteChecklistItemAsync` ever
  sets `comment_id`, so an attachment registered without that follow-up call was previously stranded in
  R2 + Postgres with no read path at all.
- Added `idx_documents_folder` — `documents.folder_id` had never been indexed (only `org_id` and
  `expiry_date`), so every folder filter was a per-org seq scan.

Deploy: apply migration `0026` (`POST /internal/migrations`) after rolling the BFF, per the usual
manual-migration step.

---

## v16 — 2026-07-16 10:27 IST

**Award mail: narrower audience + no mail for stub tenders** — kills two sources of "Result out:" noise
reported from production. `ResolveBidsAndNotifyAsync` only; bid resolution and the `awarded` status flip
are untouched and still run for every award.

- **Interest-matched orgs no longer get award mail.** The recipient union drops `tender_matches` and is
  now **bidders ∪ tracked/saved**. A `tender_matches` row only means the tender once matched an org's
  saved interests, so every org received award mail for tenders nobody there ever opened, saved, or bid
  on — and the query had no `status` filter, so even matches that were buffered but never delivered as a
  digest still fired one. Interests continue to drive the `TENDER_MATCH` digest exactly as before.
- **Stub tenders send nothing.** New `HasPlaceholderTitle` skips the notification when a tender's title
  is blank or equals its bid number. BidProcessor's `BffTenderClient` mirrors the bid number into `title`
  when enrichment produced none (the seed-only path: no document text, or a failed/degenerate AI run,
  rescued by the portal seed floor — and GeM seeds carry no title). Those tenders reach Postgres with no
  title, description, or documents, so the mail could only say `Result out: GEM/2026/R/694493` and
  nothing else. Match is exact — a real title that merely *contains* its bid number still notifies.
- **Consequence to know:** an org that bid on a stub tender gets no won/lost *email*. The bid is still
  auto-resolved and shows in the Bids UI; only the notification is suppressed.
- No migration. Build clean; 36 tests green (6 new, pinning the placeholder-title predicate).
  `InternalsVisibleTo` added to `BiddingBuddy.Bff.Infrastructure` for the test project.

## v15 — 2026-07-12 09:30 IST

**Reliable won/lost resolution for tender awards** — closes the seller-identity gap flagged in v14.
`OnTenderAwardedAsync` now resolves each org's open bid by **matching its seller identity to the award
ladder** (won if the org is L1-Qualified, else lost with its rank), instead of the value-match heuristic.

- Seller identity = new `organizations.gem_seller_name` (explicit, exact-as-on-GeM) **or** the org name
  as a zero-config fallback — so resolution works out of the box for orgs whose registered name matches
  their GeM seller name, with the override for precision. Names are normalized (lowercase, alphanumeric,
  collapsed whitespace) before matching. Value-match remains as a last-resort fallback.
- `gem_seller_name` added via **migration `0024`** + `Organization` entity/config + `CreateOrgDto`/
  `UpdateOrgDto`/`OrgDetailDto` + `OrganizationService` (create/update/detail). Settable via the existing
  `PATCH /api/organizations/{id}` (already a partial update).
- **Go-live:** apply migrations `0023` (from v14) **and** `0024` before deploying. Build clean; 30 tests green.

## v14 — 2026-07-12 08:45 IST

**Tender results (awards) — Phase 3 of the tender-results feature** (design: root
`docs/tender-results/PLAN.md`). Generic award data stays in Mongo (BFF proxies it); only the
org-specific reactions (bid resolution + notifications) touch Postgres.

- **New internal endpoint `POST /internal/tenders/on-awarded`** (`[PipelineApiKey]`) → new
  `IInternalPipelineService.OnTenderAwardedAsync`. Platform + gem id are in the BODY (`TenderAwardedDto`
  — the bid number has slashes). It:
  1. flips the local tender's `status` → `awarded`;
  2. **resolves each org's open bid** to won/lost by matching their recorded `our_bid_value` to the award
     ladder (high-confidence exact match only; unmatched bids left for the user), writing a `bid_activities`
     audit row (actor = assignee/creator, since a pipeline change has no interactive user);
  3. **notifies trackers** — orgs with a bid ∪ tracked/saved (`org_tender_settings`) ∪ interest-matched
     (`tender_matches`), once per org (deduped via `notification_reminders` `AWARDED:{orgId}`), to bid
     assignees + owner/admin/bid_manager, via the new `TENDER_AWARDED` template (Email + InApp).
     Best-effort — never throws.
- **New `TENDER_AWARDED` notification template** seeded by **migration `0023`** (Email + InApp; InApp
  metadata carries orgId/entityId for the inbox). *No new table.*
- **Read proxy**: `GET /api/tenders/{id}/result` (resolves platform+gem-id off the raw tender, then
  proxies `GET /api/tender-results/by-tender` from Services; 404 until awarded) + new `MarketController`
  `GET /api/market/pricing?category=&state=`. New `TenderResultDto`/`MarketPricingStatsDto` + client
  methods on `IBiddingBuddyServicesClient` (with null-on-404).
- ⚠️ **Design gap flagged:** `bids` carries no seller identity, so won/lost auto-resolution is a
  value-match heuristic today; a proper org↔seller mapping is the follow-up (PLAN §9/§11).
- **Go-live:** apply migration `0023` before deploying this image. Build clean; 30 BFF tests green.

## v13 — 2026-07-08 IST

**Multi-portal tender identity — code follow-up to v12.**

- **`InternalPipelineService.UpsertTenderAsync` now keys the lookup on `(platform, gem_tender_id)`** — matches the composite uniqueness added by migration `0022`. Platform is normalized to lowercase on entry (older pipelines that send `"GeM"` no longer create duplicate rows). The update-path `Platform` overwrite was removed (identity is fixed once matched).
- **`TenderSearchQueryDto.Platforms`** — new multi-select portal filter parameter. `BiddingBuddyServicesClient.BuildSearchUrl` forwards each value as a `Platforms` query param to BiddingBuddyServices. UI can now do `?platforms=gem&platforms=eprocure` to filter by source portal.
- **Tests: `InternalUpsertPlatformTests`** (EF InMemory) — pins the write-path behaviour: default-to-gem, cross-portal no-clobber (same `gem_tender_id`, different `Platform` → 2 rows), case-insensitive same-portal upsert. Adds `Microsoft.EntityFrameworkCore.InMemory` to the test project.
- **Go-live:** ship this image before applying migration 0022. Without this code, migration 0022's composite index turns cross-portal collisions from silent misassignment into `unique_violation` errors on write.

## v12 — 2026-07-07 IST

**Multi-portal tender identity — composite uniqueness (migration 0022).**

- Migration `0022_tender_platform_uniqueness.sql` extends v10's `0021` (which added the `tenders.platform` column + index) with the identity fix: **drops** the single-column `tenders_gem_tender_id_key` and **replaces** it with a composite `ux_tenders_platform_tender_id ON (platform, gem_tender_id)`. Without this, two portals emitting the same `gem_tender_id` would silently overwrite each other's rows on upsert. The column name `gem_tender_id` stays — it now means "platform tender id"; renaming would ripple through bids/orders joins for zero behavioural gain.
- No code changes in this release; ingestion continues to use the existing key. A follow-up will move `InternalPipelineService`'s upsert to key on `(platform, gem_tender_id)` explicitly and add a `Platforms` filter param to search — separated so this migration can ship independently.
- **Go-live:** apply migration 0022 (`POST /internal/migrations`) right after deploy.

## v11 — 2026-07-04 19:27 IST

### Full-archive sitemap via keyset enumeration
- `SitemapController` now enumerates **every** tender (full archive, ~56k incl. closed) via a keyset walk of the new BiddingBuddyServices `GET /api/tenders/enumerate` endpoint (cursor by `_id`), replacing deep-`skip` pagination that returned **HTTP 400** past ~10k records (Atlas sort-memory limit). Pre-rendered `<url>` lines are cached 6h; index + chunks slice them (10k/chunk). Removes the expired-tender filter.
- `SsrController`: closed tenders are now indexable (removed `noindex`-on-closed) — the archive should be crawlable; only genuine 404s stay `noindex`.
- Adds `IBiddingBuddyServicesClient.EnumerateTendersAsync` + `TenderEnumerationDto`.
- Verified locally against Atlas: **55,909** tender URLs across 6 chunks (was 918). Requires BiddingBuddyServices enumerate endpoint deployed first.

## v10 — 2026-07-06 19:30 IST

**Per-tender source-portal provenance (migration 0021) — so IREPS/eProcure tenders can be told apart from GeM in the UI.**

- BidProcessor already sent the source portal in the `/internal/tenders` payload, but the BFF had no
  column and silently dropped it. Migration `0021_add_tender_platform.sql` adds `tenders.platform`
  (default `'gem'`, indexed); `Tender` entity + `TenderConfiguration` + `UpsertTenderDto` +
  `InternalPipelineService` (insert + update) now persist it.
- **Surfaced to the UI on the display path (Mongo-proxied):** `TenderListItemDto` + `TenderDetailDto`
  gained an optional `Platform`, threaded from the Services `TenderSource.Platform` in
  `TenderDetailsTranslator` (and from the Postgres entity in `TenderService`). No breaking DTO changes
  (trailing optional param).
- **Go-live:** apply migration 0021 (`POST /internal/migrations`), restart BFF. Ingestion for IREPS
  already works via BidProcessor's generic `PortalProfile` fallback — this change only adds the
  provenance/badge. dotnet build clean.

## v9 — 2026-07-06 18:15 IST

**Mobile app backend, phase 2 — push, Apple sign-in, account deletion, refresh grace (migration 0020).** Work items B2/B3/B4/B7/B9 from `docs/mobile-app/PLAN.md`.

- **Push device registry + FCM fan-out (B3/B4).** Migration `0020_add_user_devices.sql`: new `user_devices` table (per-FCM-token, `push_enabled` per-device switch, `revoked_at`) + Firebase `notification_templates` cloned from the InApp rows for the push-worthy money/deadline/assignment/outcome events (guarantees Handlebars variable + deep-link metadata parity). New **`/api/devices`** (org-agnostic, JWT only — added to `OrgContextMiddleware` skip list): `POST` upsert (register/refresh), `GET` list, `PATCH /push` (per-device mute), `DELETE /{token}`. **`NotificationPublisher` now fans out push centrally** — for a push-worthy template it resolves the InApp recipient's user → their newest active push-enabled device → appends one Firebase recipient (best-effort; never blocks the in-app/email delivery). No call-site changes.
- **Sign in with Apple (B2).** New `POST /api/auth/apple`: `AppleTokenVerifier` validates the identity token against Apple's JWKS (issuer, audience = `OAuth:Apple:ClientId` = the bundle id, RS256, lifetime; JWKS cached 24h), then reuses a new shared `LinkOrCreateUserAsync` (extracted from the OAuth path) to find-or-create the user + link `oauth_accounts(provider='apple')`. Handles Apple's first-auth-only email/name.
- **Account deletion (B7 — store blocker).** New `POST /api/auth/delete-account` (JWT): password users re-enter their password, OAuth/Apple-only users pass `confirm=true`; sole-owner-of-a-shared-org is blocked (409) until ownership is transferred, solo orgs are deactivated; then soft-delete + PII anonymize the user and revoke all refresh tokens + devices + oauth accounts, in one transaction.
- **Refresh-rotation grace window (B9).** A just-revoked refresh token can rotate once more within 60s (the flaky-mobile-network lost-response case) instead of forcing logout; reuse outside the window is still rejected as compromise.
- **Config:** `OAuth:Apple:ClientId` (+ optional `OAuth:Apple:ClientIds`). Firebase push activates when BidProcessor's `Notifications:Firebase:ServiceAccountJsonPath` points to a real service-account JSON.
- **Go-live:** apply migration 0020 (`POST /internal/migrations`), restart BFF, configure Firebase on BidProcessor. Plumbing: `UserDevice` entity + config + DbSet, `IDeviceService`/`DeviceService`, `AppleTokenVerifier`, DTOs (`RegisterDeviceDto`, `AppleSignInDto`, `DeleteAccountDto`), `database/schema.sql` reference updated. dotnet build clean.

---

## v8 — 2026-07-06 14:45 IST

**Mobile OAuth handoff — one-time code + PKCE exchange (migration 0019).** First backend piece of the TendersAgent mobile app (plan: `docs/mobile-app/PLAN.md`, work item B1).

- **Why:** the existing OAuth callback redirects tokens to the SPA callback URL — unusable for a native app and unsafe in a redirect URL. Mobile flows now receive a 60-second single-use code instead; tokens only ever travel in a POST response body.
- **`GET /api/auth/oauth/{provider}`** accepts optional `client=mobile` + `code_challenge` (PKCE S256, RFC 7636 shape enforced) + `redirect_uri`. Mobile redirects must match `OAuth:Mobile:RedirectAllowlist` (new config; defaults `tendersagent://auth`, `biddingbuddymobile://auth`; Development also accepts `exp://`/`exps://` for Expo Go). The extra parameters ride inside the existing signed state JWT — web flows are byte-for-byte unchanged.
- **Callback** (`/oauth/{provider}/callback`): when the validated state carries `client=mobile`, mints a one-time code (SHA-256 hashed at rest, 60 s TTL) and 302s to `{redirect_uri}?code=…&is_new=…` (or `?error=…`). Web path untouched.
- **New `POST /api/auth/oauth/exchange`** `{ code, codeVerifier }` → atomic single-use claim (`ExecuteUpdate` on `used_at`), constant-time S256 verifier check, then the standard token pair (same shape as `/login`).
- **Migration `0019_add_oauth_exchange_codes.sql`:** new `oauth_exchange_codes` table (+ `database/schema.sql` reference updated). Apply via `POST /internal/migrations` after deploy.
- **Plumbing:** `OAuthExchangeCode` entity + EF configuration + DbSet; `ITokenService` state-token methods now carry `OAuthStateData` (returnUrl/client/challenge/redirect); `AuthService` OAuth callback refactored into a shared `ResolveOAuthUserAsync` used by both web and mobile completions (WELCOME still fires once on first-time signup).

## v7 — 2026-07-06 11:30 IST

**Per-user saved tender filters (migration 0018).**

- **Migration `0018_add_user_saved_filters.sql`:** new `user_saved_filters` table — per (user, org). Two kinds: `last_used` (one auto-upserted snapshot per user+org, enforced by a partial unique index) and `named` (explicit saved views). The filter selection is stored as `jsonb` (serialized via an EF `ValueConverter` to a JSON string — matching the codebase's jsonb-as-string convention; the DbContext uses `UseNpgsql` without `EnableDynamicJson`, so a raw POCO→jsonb mapping throws on write). Apply via `POST /internal/migrations` right after deploying this image.
- **New endpoints — `/api/saved-filters` (org-scoped, JWT + X-Org-Id):**
  - `GET` → `{ lastUsed, named[] }`
  - `PUT /last-used` → upsert the last-used snapshot
  - `POST` → create a named view (capped at 50/user)
  - `DELETE /{id}` → remove a saved filter
- **Entity/plumbing:** `UserSavedFilter` + `SavedFilterState` (Core), `UserSavedFilterConfiguration` (jsonb mapping), `BffDbContext.UserSavedFilters`, `ISavedFilterService`/`SavedFilterService`, `SavedFiltersController`, DI registration. Errors surface via the existing `GlobalExceptionHandler` (400/404).
- Backs the bidding-buddy-ui Tenders "Views" menu + auto-restore of last-used filters.

## v6 — 2026-07-04 10:50 IST

### Public facet-options endpoint (guest explore filters)
- New `GET /api/public/tenders/facet-options?field=category|state&search=&limit=` on `PublicTendersController` — anonymous, IP rate-limited ("public" policy) like its siblings; pure passthrough to BiddingBuddyServices' facet values (canonical taxonomy). Invalid `field` returns 400 instead of surfacing the upstream exception as a 500.
- Powers bidding-buddy-ui v7's data-driven guest explore filters, replacing hardcoded lists that no longer matched the canonical taxonomy.

Files: `src/BiddingBuddy.Bff.Api/Controllers/PublicTendersController.cs`.

## v5 — 2026-07-04 02:20 IST

### Per-provider OAuth enable flags + GET /api/auth/providers
Facebook and GitHub sign-in can now be toggled per environment without touching the UI image:

- **New config flag `OAuth:{Provider}:Enabled`** (default **true** when absent, so existing deployments are unaffected). Toggle via env, e.g. `OAuth__GitHub__Enabled=false`, and restart.
- **New anonymous `GET /api/auth/providers`** → `{ "providers": ["google","facebook","github"] }` filtered to enabled ones (this endpoint was documented in CLAUDE.md but never existed). The SPA renders its social buttons from this list.
- **Enforcement, not just presentation**: `GET /api/auth/oauth/{provider}` and its callback return 400 for a disabled provider — hiding the button isn't the gate.
- Verified live: with `OAuth__GitHub__Enabled=false` → providers list omits github, initiation returns 400 (facebook/google still 302), and the SPA hides the button on login + signup; default config → all three present.

Files: `Controllers/AuthController.cs`, `appsettings.json` (Enabled: true stubs for Facebook/GitHub).
Consumed by: bidding-buddy-ui v6 (`useEnabledProviders` hook).
## v4 — 2026-07-04 01:05 IST

### Facebook OAuth + first-org onboarding support (social-primary signup)
Social login is becoming the primary signup path; the SPA now sends brand-new social users to a company-details onboarding page. BFF-side enablers:

- **Facebook OAuth provider**: `facebook` added to `SupportedProviders`; `OAuthProviderService` gained `BuildFacebookUrl` (Graph v19.0 dialog, scope `email,public_profile`) + `ExchangeFacebookAsync` (GET code exchange → `/me?fields=id,name,email,picture.width(200)`). Phone-only Facebook accounts with no email are rejected with a clear message (the SPA shows it on the new `/auth/error` page) — no half-created users. Config: `OAuth:Facebook:{ClientId,ClientSecret,RedirectUri}` (secrets via user-secrets/env; **prod needs `OAuth__Facebook__*` env vars + the redirect URI registered in the Meta app**).
- **`is_new` on the OAuth callback redirect**: the SPA callback URL now carries `&is_new=1|0` (account created by this call or not). Cosmetic — onboarding routing is gated on org-lessness, not this flag. `TokenResponseDto` gained `IsNewUser` (default false; unchanged for login/refresh consumers).
- **`POST /api/organizations` exempted from `OrgContextMiddleware`** (that exact route+method only): creating your *first* org is inherently pre-org — social signups have no `X-Org-Id` to send. Previously this returned 400 for org-less users, making self-service org creation impossible.
- **New invite endpoints for onboarding's "join your team" branch** (`/api/invites`, org-middleware-exempt as before):
  - `GET /api/invites/mine` (JWT) → pending unexpired invites addressed to the caller's email `[{ id, orgName, orgLogoUrl, inviterName, role, expiresAt }]` — no tokens exposed.
  - `POST /api/invites/{id}/accept` (JWT) → accepts **by id without the emailed token**; being authenticated as the invited email is the credential (same `RequireInviteeMatch` check as the token path). 404 `INVITE_INVALID` / 403 `INVITE_EMAIL_MISMATCH`.
- No schema changes, no migration.

Files: `Controllers/AuthController.cs`, `Controllers/InvitesController.cs`, `Services/OAuthProviderService.cs`, `Services/AuthService.cs`, `Services/OrganizationService.cs`, `Middleware/OrgContextMiddleware.cs`, `DTOs/Auth/TokenResponseDto.cs`, `DTOs/Orgs/OrgDtos.cs`, `Interfaces/IOrganizationService.cs`, `appsettings.json`.
Consumed by: bidding-buddy-ui v5 (social-primary auth pages + company onboarding).

## v3 — 2026-07-03 15:35 IST

### Org invites now require the invitee's confirmation (fixes broken invite emails for existing users)
Previously, inviting an email that already had an account **instantly added** them to the org and emailed a deep link to `/orgs/{id}` — a route that never existed in the SPA, so invitees landed on their old org's dashboard with no way to see the new org.

- `InviteMemberAsync` no longer grants membership for existing users. Both cases (existing + unregistered email) now create a pending `organization_invites` row; the response is always `status="invited"`. Inviting an already-active member returns **409 `ALREADY_MEMBER`** instead of silently overwriting their role.
- Existing users' TEAM_INVITATION email/in-app link now points to the SPA accept page: `{Frontend:BaseUrl}/invites/accept?token=...` (single-use 32-byte token, hash-stored, 7-day expiry — same token machinery as registration invites).
- **New controller `/api/invites`** (exempt from `OrgContextMiddleware` — the caller isn't a member of the target org yet):
  - `GET /api/invites/preview?token=` (anonymous) → `{ orgName, orgLogoUrl, inviterName, role, email, expiresAt, inviteeHasAccount }`; 404 `INVITE_INVALID` for unknown/expired/consumed tokens.
  - `POST /api/invites/accept` (JWT) → creates/reactivates the membership with the invite's role, consumes the token, returns `{ orgId, orgName, role }`. 403 `INVITE_EMAIL_MISMATCH` if the logged-in email isn't the invited one — a forwarded link can't join someone else.
  - `POST /api/invites/decline` (JWT) → consumes the token without joining (204).
- Existing-user invites now appear in `GET /organizations/{id}/invites` (pending list) and can be revoked, same as registration invites.
- Registration-link flow for unregistered emails is unchanged. **No schema changes, no migration** — reuses `organization_invites` as-is.

Files: `Services/OrganizationService.cs`, `Controllers/InvitesController.cs` (new), `Controllers/OrganizationsController.cs`, `Middleware/OrgContextMiddleware.cs`, `DTOs/Orgs/OrgDtos.cs`, `Interfaces/IOrganizationService.cs`, `Entities/OrganizationInvite.cs`.
Consumed by: bidding-buddy-ui v4 (accept page + org switcher).

## v2 — 2026-07-03 00:41 IST

### Fix: notification click 500s + DeadlineScan crash (EF mapping bugs)
Root cause of the production alerts errors — the DB schema was fine; five EF Core entity mappings were wrong or missing in `Persistence/Configurations/RemainingConfigurations.cs`:
- `UserNotification`: `Organization`/`User` relationships were never configured, so EF invented a shadow `OrganizationId` column. List worked (projected columns) but `PATCH /api/notifications/{id}/read` loaded the full entity → `42703 column u.OrganizationId does not exist` → HTTP 500. Relationships now mapped to `org_id`/`user_id`.
- `DeliveryMilestone`: entity had no configuration at all → EF queried a non-existent `"DeliveryMilestones"` table (`42P01`), crashing every DeadlineScan run and breaking Orders milestone CRUD. Now mapped to `delivery_milestones`.
- `OrderItem`: same — unmapped, now mapped to `order_items` (Orders line items were broken).
- `ComplianceRequirement` + `ComplianceDocument`: same shadow-`OrganizationId` latent bug as UserNotification, fixed preemptively.

All five verified with read-only queries against the production database (mark-read query, deadline-scan queries, and a full model sweep showing zero remaining shadow columns / unmapped tables). No schema changes, no migration needed — deploy the container and restart.

## v1 — 2026-07-03 00:03 IST

### Team management data (new)
- New endpoint `GET /api/organizations/{id}/activities?limit=20` (limit clamped 1–100): org-wide recent bid activity, newest first, with actor name and bid title. Guarded so only active members of the org can call it.
- `OrgMemberDto` extended with `activeBidsCount`, `winRate` (nullable — null until the member has won/lost bids; `dropped` excluded from the calculation), and `tasksDoneCount` (completed checklist items). Computed with grouped org-wide queries keyed by user id — no per-member loops, no schema changes or migrations.

Files: `Controllers/OrganizationsController.cs`, `DTOs/Orgs/OrgDtos.cs`, `Services/OrganizationService.cs` (+ interface).
Consumed by: bidding-buddy-ui v1 (Team page).
