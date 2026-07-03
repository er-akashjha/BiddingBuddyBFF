# Release Notes — BiddingBuddyBFF

Current version: **v3**

Convention: every change lands as a new `## vN — YYYY-MM-DD HH:mm IST` entry at the top (newest first). The counter increments by 1 per release, per repo.

---

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
