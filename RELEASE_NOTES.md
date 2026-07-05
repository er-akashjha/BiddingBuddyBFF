# Release Notes — BiddingBuddyBFF

Current version: **v7**

Convention: every change lands as a new `## vN — YYYY-MM-DD HH:mm IST` entry at the top (newest first). The counter increments by 1 per release, per repo.

---

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
