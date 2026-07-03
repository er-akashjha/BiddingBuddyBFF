# BiddingBuddyBFF

ASP.NET Core 8 Backend-For-Frontend. Provides a single, client-optimized REST API surface for the React SPA. Handles OAuth 2.0 + JWT auth, multi-tenant org scoping, and exposes endpoints for all procurement features (tenders, bids, documents, compliance, orders, payments, competitors, AI analysis). Receives enriched data from the BidProcessor pipeline via internal API-key-protected endpoints.

## Solution Layout

```
BiddingBuddyBFF/
├── BiddingBuddyBFF.sln
├── CONTEXT.md                        Authoritative 43KB architecture + schema reference — READ THIS
├── database/
│   └── schema.sql                    Full PostgreSQL DDL — human reference (runtime uses DbMigrator)
└── src/
    ├── BiddingBuddy.Bff.Api/         ASP.NET Core entry point
    │   ├── Program.cs                DI wiring, middleware pipeline
    │   ├── Controllers/              Controllers (org-scoped + /internal/*)
    │   ├── Filters/PipelineApiKeyAttribute.cs  X-Api-Key gate for /internal/*
    │   ├── Middleware/OrgContextMiddleware.cs  X-Org-Id header + org-membership check
    │   └── appsettings.json
    ├── BiddingBuddy.Bff.Core/        Domain layer (no infra deps)
    │   ├── Entities/                 EF Core entity classes
    │   ├── DTOs/                     Request/response DTOs per feature
    │   ├── Options/                  Strongly-typed config (R2Options, RabbitMqOptions, …)
    │   └── Interfaces/               Service + repository contracts only
    └── BiddingBuddy.Bff.Infrastructure/  Data + external services
        ├── Persistence/
        │   ├── BffDbContext.cs              EF Core DbContext (PostgreSQL/Npgsql)
        │   ├── Configurations/              IEntityTypeConfiguration<T> per entity
        │   └── Migrations/000N_*.sql        Embedded SQL scripts applied by DbMigrator
        ├── Repositories/
        └── Services/
            ├── AuthService.cs               JWT minting, refresh token rotation
            ├── OAuthProviderService.cs      Google + Facebook + GitHub OAuth 2.0 code exchange
            ├── DbMigrator.cs                Runs embedded *.sql scripts via /internal/migrations
            ├── RabbitMqPublisher.cs         Singleton RabbitMQ producer
            ├── NotificationPublisher.cs     Insert event/deliveries then publish trigger
            ├── NotificationTemplateService.cs  Admin CRUD over notification_templates
            └── ...                          Per-feature service implementations
```

## Architecture

```
bidding-buddy-ui (React SPA)
        │ HTTPS + JWT Bearer + X-Org-Id header
        ▼
BiddingBuddyBFF  (this project)
   Controllers → Services (Core) → Repositories (Infrastructure) → PostgreSQL
        │
        │ AMQP publish (notification.{channel} queues)
        ▼
   RabbitMQ (13.233.138.227:5672, DLX bid.dlx)
        │
        │ consumed by BidProcessor's notification workers
        ▼
   BidProcessor (notification + enrichment workers)
        ▲
        │ POST /internal/* + X-Api-Key header
BidProcessor's enrichment workers → BFF
```

BFF is both a REST surface for the SPA and a RabbitMQ producer for the notification
subsystem (publisher inserts rows in Postgres then publishes thin triggers).

**Clean Architecture layers:**
- **API** — Controllers, middleware (presentation only, no business logic)
- **Core** — Entities, interfaces, DTOs (no infrastructure dependencies)
- **Infrastructure** — EF Core DbContext, repositories, JWT/OAuth service impls

## API Endpoints

### Public (no auth)
| Method | Path | Purpose |
|---|---|---|
| GET | `/api/auth/oauth/{provider}` | Initiate OAuth (Google/Facebook/GitHub) |
| GET | `/api/auth/oauth/{provider}/callback` | OAuth code exchange |
| POST | `/api/auth/refresh` | Rotate refresh → new access + refresh token |
| GET | `/api/auth/providers` | List enabled OAuth providers (`OAuth:{Provider}:Enabled` flags, default true; disabled providers also 400 on initiation) |
| GET | `/api/invites/preview?token=` | Invite details for the SPA accept page (token = credential) |

### Authenticated (Bearer JWT + `X-Org-Id` header required for org-scoped routes)
| Controller | Base Route | Key Operations |
|---|---|---|
| Auth | `/api/auth` | `GET /me`, `POST /logout` |
| Organizations | `/api/organizations` | CRUD, member management, role assignment. Inviting a member NEVER adds them directly — always creates a pending invite (`status="invited"`; 409 `ALREADY_MEMBER` for active members) that the invitee must accept |
| Invites | `/api/invites` | `POST /accept`, `POST /decline` (JWT, **no X-Org-Id** — exempt from org middleware since the caller isn't a member yet). Accept validates the logged-in email matches the invited email, then creates/reactivates the membership |
| Tenders | `/api/tenders` | List/filter, `GET /paged` (paginated wrapper over BiddingBuddyServices), get detail, save, track, documents, AI analysis |
| Bids | `/api/bids` | List, create, update, stage progression (7 stages), activities, **comments**, checklist, `GET /by-tender?tenderIds=` (batched already-in-pipeline lookup for tender list/detail) |
| Compliance | `/api/compliance` | Requirements, documents, health score |
| Documents | `/api/documents` | List, upload (presigned S3), download, folder management, versioning |
| Orders | `/api/orders` | CRUD, line items, delivery milestones |
| Payments | `/api/payments` | EMD (bid deposits), invoices, payment summary |
| Competitors | `/api/competitors` | List, detail, market summary |
| Analysis | `/api/analysis` | Dashboard KPIs, recommendations, performance, market trends |
| Notifications | `/api/notifications` | In-app inbox: list, mark read, channel preferences (backed by `user_notifications` since the rename — see Notification subsystem below) |
| Integrations | `/api/integrations` | GeM portal config, sync trigger, sync status |
| Tender alert rules | `/api/tender-alert-rules` | Client "interests" CRUD + `/settings` (digest size, channels, roles) — see Tender-match digests below |

### Internal (API-key only — `X-Api-Key` header, bypasses org middleware)
| Method | Path | Purpose |
|---|---|---|
| POST | `/internal/tenders` | Upsert enriched tender from BidProcessor (matching is decoupled — see Tender-match digests) |
| POST | `/internal/tenders/{gemTenderId}/documents` | Store extracted document text |
| POST | `/internal/competitors` | Record competitor bid observation |
| POST | `/internal/analysis` | Store AI analysis results |
| GET  | `/internal/migrations` | List embedded migration scripts + applied status |
| POST | `/internal/migrations` | Apply all pending migrations (idempotent — see DbMigrator below) |
| GET/POST/PATCH/DELETE | `/internal/notification-templates[/{id}]` | Admin CRUD over `notification_templates` (global config — see Notification subsystem) |
| POST | `/internal/notifications` | Trigger a notification dispatch from outside the BFF (BidProcessor, admin tools). In-BFF flows call `INotificationPublisher` directly. |
| POST | `/internal/digests/flush` | Legacy time-fallback flush of any still-`pending` tender matches (now a no-op safety net — see Tender-match digests) |
| POST | `/internal/matching/scan` | Run the tender-alert scan now: evaluate not-yet-scanned tenders → one digest per matched org. `?backfill=true` re-arms all tenders first. Same logic as the scheduled `TenderMatchScanWorker`. |

## Auth Design

### OAuth → JWT flow
```
Browser → GET /api/auth/oauth/google?returnUrl=/dashboard
  ↓ StateToken JWT (nonce + returnUrl, short-lived CSRF protection)
Google OAuth consent → code=...
  ↓ GET /api/auth/oauth/google/callback?code=...&state=...
OAuthProviderService.ExchangeCodeAsync() → access_token
  ↓ GET userinfo → { email, name, avatar }
AuthService.HandleOAuthCallbackAsync()
  1. Upsert user + oauth_accounts row
  2. Mint access JWT (15 min, HS256)
  3. Generate refresh token (30 day, hashed in DB)
  ↓ 302 redirect
Frontend /auth/callback?access_token=...&refresh_token=...
```

### JWT claims
```json
{ "sub": "user-uuid", "email": "...", "name": "...", "jti": "...", "iss": "BiddingBuddyBFF", "aud": "BiddingBuddyClients" }
```

### Token rotation (`POST /api/auth/refresh`)
- Validates refresh token hash from DB
- Revokes old refresh token (`revoked_at` timestamp)
- Issues new access JWT + new refresh token

### Org context middleware
Applied to all routes except `/api/auth/*`, `/api/public/*`, `/api/invites/*`, `/internal/*`, `/swagger`, `/health`, `/sitemap`:
1. Reads `X-Org-Id` header → validates UUID
2. Extracts `sub` claim from JWT
3. Checks `organization_members` table — 403 if not a member
4. Sets `HttpContext.Items["OrgId"]` for downstream controllers

### Role-based access
Roles (stored in `organization_members.role`): `owner`, `admin`, `bid_manager`, `finance`, `sales`, `viewer`

## Database

**PostgreSQL** (Npgsql + EF Core 8). No MongoDB.

- **Dev connection:** `Host=13.233.138.227;Port=5432;Database=biddingbuddy;Username=postgres;Password=Fiserv@123`
- Full DDL in `database/schema.sql` (the runtime source of truth for the BFF is now `Persistence/Migrations/*.sql` applied by `DbMigrator` — see below)
- Most tables have an `updated_at` trigger via `set_updated_at()`
- `pgcrypto` extension for UUID generation

Key table groups:

| Group | Tables |
|---|---|
| Auth | `users`, `oauth_accounts`, `refresh_tokens` |
| Multi-tenancy | `organizations`, `organization_members` |
| Procurement | `tenders`, `saved_tenders`, `tender_documents`, `tender_analysis` |
| Bids | `bids`, `bid_activities`, `bid_checklists`, `bid_comments` |
| Compliance | `compliance_requirements`, `compliance_documents` |
| Documents | `documents`, `document_versions`, `document_folders` |
| Fulfillment | `orders`, `order_items`, `order_milestones` |
| Finance | `emd_deposits`, `invoices` |
| Intelligence | `competitors`, `competitor_observations` |
| Platform | `user_notifications`, `gem_integrations`, `analysis_results` |
| Notification dispatch | `notifications`, `notification_deliveries`, `notification_templates`, `notification_logs` |
| Tender-match digests | `tender_alert_rules`, `org_alert_settings`, `tender_matches` (migration `0004`) |
| Schema | `schema_migrations` (DbMigrator state) |

**Naming-rename note:** what used to be `notifications` (the in-app inbox the
React SPA reads) is now `user_notifications`. The `notifications` name was reclaimed
by the notification dispatch subsystem (handoff with the BidProcessor team) as the
logical event row. Existing controller URL `/api/notifications` is unchanged — only
the backing table + entity (`UserNotification`) were renamed.

## Configuration

```json
// appsettings.json
{
  "Jwt": {
    "Secret": "CHANGE_ME_32+_chars",
    "Issuer": "BiddingBuddyBFF",
    "Audience": "BiddingBuddyClients",
    "AccessTokenExpiryMinutes": "15",
    "RefreshTokenExpiryDays": "30"
  },
  "OAuth": {
    "Google": { "ClientId": "...", "ClientSecret": "...", "RedirectUri": "https://localhost:7100/api/auth/oauth/google/callback" },
    "GitHub": { "Enabled": true, "ClientId": "...", "ClientSecret": "...", "RedirectUri": "https://localhost:7100/api/auth/oauth/github/callback" },
    "Facebook": { "Enabled": true, "ClientId": "...", "ClientSecret": "...", "RedirectUri": "https://localhost:7100/api/auth/oauth/facebook/callback" }
  },
  "Frontend": { "BaseUrl": "http://localhost:3000", "AuthCallbackPath": "/auth/callback" },
  "Pipeline": { "ApiKey": "pipeline_internal_secret_CHANGE_ME" },
  "RabbitMq": {
    "HostName": "13.233.138.227", "Port": 5672,
    "Username": "...", "Password": "...",
    "VirtualHost": "/", "DeadLetterExchange": "bid.dlx",
    "ClientName": "BiddingBuddyBFF"
  },
  "BiddingBuddyServices": { "BaseUrl": "http://localhost:5273", "Username": "admin", "Password": "admin123" }
}
```

## NuGet Dependencies

| Package | Version | Purpose |
|---|---|---|
| Microsoft.AspNetCore.Authentication.JwtBearer | 8.0.11 | JWT middleware |
| Npgsql.EntityFrameworkCore.PostgreSQL | 8.0.11 | PostgreSQL ORM |
| Microsoft.EntityFrameworkCore.Design | 8.0.11 | EF Core tooling (not used for migrations — see DbMigrator) |
| System.IdentityModel.Tokens.Jwt | 7.6.3 | JWT parsing/validation |
| Microsoft.Extensions.Http | 8.0.1 | HttpClientFactory (OAuth + BiddingBuddyServices) |
| Swashbuckle.AspNetCore | 6.6.2 | Swagger UI |
| AWSSDK.S3 | 3.7.413.3 | Cloudflare R2 (S3-compatible) presign |
| RabbitMQ.Client | 6.8.1 | RabbitMQ producer for notification subsystem |
| BCrypt.Net-Next | 4.0.3 | Local password hashing (if/when used) |

## Running

```bash
cd src/BiddingBuddy.Bff.Api
dotnet run
# Listens on https://localhost:7100
```

### Schema migrations (DbMigrator — NOT EF migrations)

This project does **not** use EF Core migrations. Schema changes ship as raw SQL
scripts embedded in the Infrastructure assembly and are applied on demand via
`POST /internal/migrations`.

```
Persistence/Migrations/
├── 0001_add_bid_comments.sql     ← applied in filename order
├── 0002_add_notifications.sql    ← next one
└── ...
```

How it works:
- Scripts are marked `<EmbeddedResource>` in `BiddingBuddy.Bff.Infrastructure.csproj`,
  so they travel inside the DLL — no extra deploy artefacts.
- `DbMigrator` (`Services/DbMigrator.cs`) ensures a `schema_migrations` table,
  reads applied names, then runs each missing script in ascending filename order.
- Each script + its tracking insert run in **one transaction**. A failure rolls
  back fully and is not recorded → retried next call. The endpoint returns 500
  with the underlying Postgres error so failures surface immediately.
- Scripts must be **idempotent** (use `IF NOT EXISTS`, `ON CONFLICT DO NOTHING`,
  `DO $$ ... IF NOT EXISTS ...` for triggers). The transaction + tracking row is
  belt-and-suspenders so you can't double-apply.

Endpoints (both `[PipelineApiKey]`):
```
GET  /internal/migrations    → [{ name, applied, appliedAt }]
POST /internal/migrations    → { applied: [...], alreadyApplied: [...], totalScripts }
```

Adding a new migration:
1. Drop `000N_short_name.sql` into `src/BiddingBuddy.Bff.Infrastructure/Persistence/Migrations/`.
2. Rebuild + restart the BFF.
3. `curl -X POST http://localhost:5124/internal/migrations -H "X-Api-Key: <Pipeline:ApiKey>"`.

`database/schema.sql` is the human-readable reference. Keep it in sync with the
migrations folder when you add/change tables — but the runtime applies the
migrations, not `schema.sql`.

## How BidProcessor Connects

BidProcessor's `EnrichBidWorker` and `ProcessBidDocumentsWorker` POST to `/internal/*` with:
```
X-Api-Key: {Pipeline:ApiKey from appsettings}
Content-Type: application/json
```

This is the integration seam between the async pipeline and the BFF's read/query surface.

## Cloudflare R2 Storage

### Purpose & bucket separation rule

| Bucket | Owner | Content |
|---|---|---|
| `bidding-buddy` (**R2**) | BiddingBuddyBFF | Org-uploaded documents (GST certs, PAN, bid docs, etc.) |
| `bidding-buddy-dev` (**AWS S3**) | BidProcessor / Downloader | Tender PDFs scraped from GeM |

**Never cross the buckets.** R2 is for org documents; AWS S3 is for pipeline tender files.

### Upload flow

```
UI                          BFF                          R2
 │                           │                            │
 │─ POST /api/documents/     │                            │
 │  upload-url               │                            │
 │  { fileName, mimeType,    │─ validate & build key ──▶  │
 │    fileSizeKb }           │─ CreatePresignedPutAsync ─▶│
 │                           │◀─ { uploadUrl, objectKey } │
 │◀── 200 { uploadUrl,       │                            │
 │         objectKey,        │                            │
 │         headers,          │                            │
 │         expiresAt }       │                            │
 │                           │                            │
 │─────── PUT uploadUrl ─────────────────────────────────▶│ (direct, no BFF)
 │◀──────────────────────────────────────── 200 ETag ─────│
 │                           │                            │
 │─ POST /api/documents      │                            │
 │  { s3Key: objectKey, … }  │─── register in PostgreSQL  │
 │◀─── 201 DocumentDto ──────│                            │
```

### Config keys

| Key | Where | Value |
|---|---|---|
| `R2:AccountId` | `appsettings.json` | Cloudflare account ID |
| `R2:BucketName` | `appsettings.json` | `bidding-buddy` |
| `R2:Endpoint` | `appsettings.json` | `https://{AccountId}.r2.cloudflarestorage.com` |
| `R2:PresignTtlSeconds` | `appsettings.json` | `900` (15 min) |
| `R2:MaxUploadSizeKb` | `appsettings.json` | `102400` (100 MB) |
| `R2:AccessKeyId` | **user-secrets / env var** | R2 API token — never commit |
| `R2:SecretAccessKey` | **user-secrets / env var** | R2 API token — never commit |

Local dev: `dotnet user-secrets set "R2:AccessKeyId" "..." --project src/BiddingBuddy.Bff.Api`

### Bucket setup (one-time)

1. Create bucket `bidding-buddy` in Cloudflare R2.
2. Create an R2 API token: **Object Read & Write** scoped to that bucket only.
3. Apply CORS policy to the bucket:

```json
[
  {
    "AllowedOrigins": ["http://localhost:3000", "https://tendersagent.com"],
    "AllowedMethods": ["PUT", "GET"],
    "AllowedHeaders": ["Content-Type"],
    "ExposeHeaders": ["ETag"],
    "MaxAgeSeconds": 3600
  }
]
```

### Presign endpoint

`POST /api/documents/upload-url`

**Request**
```json
{ "fileName": "gst_cert.pdf", "mimeType": "application/pdf", "fileSizeKb": 245 }
```

**Response 200**
```json
{
  "uploadUrl": "https://…r2.cloudflarestorage.com/bidding-buddy/orgs/{orgId}/docs/{uuid}/gst_cert.pdf?X-Amz-…",
  "objectKey": "orgs/{orgId}/docs/{uuid}/gst_cert.pdf",
  "headers": { "Content-Type": "application/pdf" },
  "expiresAt": "2026-06-03T10:15:00Z"
}
```

**Validation rules**
- `fileName` non-empty; path separators, control chars stripped (`FileNameSanitizer.Sanitize`)
- `mimeType` on the server allowlist (PDF, common images, Office formats)
- `fileSizeKb` between 1 and `R2:MaxUploadSizeKb`
- Object key always server-generated: `orgs/{orgId}/docs/{Guid}/{sanitizedFileName}`

## Notification subsystem

The BFF is the **publisher** for a fan-out notification pipeline. The BidProcessor
team's notification workers are the **consumers** (rendering + sending + retries
+ audit log). They've shipped + their tests are green; BFF inserts rows and
publishes thin RabbitMQ triggers.

### Tables

| Table | Owner | Purpose |
|---|---|---|
| `notification_templates` | BFF | Handlebars templates, one row per (code, channel). Admin CRUD via `/internal/notification-templates`. |
| `notifications` | BFF inserts only | One logical event per call (category + template_code + payload + correlation_id). |
| `notification_deliveries` | BFF inserts only — processor owns every column after insert | One per channel for a notification. BFF sets `status='Pending'`, `max_retries` per category. |
| `notification_logs` | Processor-owned | One row per send attempt (audit). BFF read-only. |
| `user_notifications` | BFF | The in-app inbox the SPA reads. The processor's InApp handler inserts here when channel=InApp. |

### Publisher flow (`INotificationPublisher.SendAsync`)

```
1. DB transaction
     INSERT INTO notifications (category, template_code, user_id, payload, correlation_id)
     INSERT INTO notification_deliveries  (one row per recipient, status=Pending, max_retries=per-category)
   commit

2. For each delivery, publish to RabbitMQ
     queue:  notification.{email|sms|whatsapp|firebase|inapp}
     body:   { deliveryId, channel, correlationId }   ← thin trigger, no content
     props:  Persistent + ContentType=application/json + AMQP CorrelationId
```

Critical rules baked into the publisher:
- **No content/recipient/template data in the RabbitMQ message.** Just the ids.
- **`max_retries` per category:** Transactional=5, Information=3, Marketing=1.
- **`recipient_address` format per channel:** Email → `user@example.com`; Sms/WhatsApp → E.164 `+9198…`; Firebase → FCM token; InApp → user-id string.
- **Same `correlation_id` across all deliveries of one notification** → forwarded into every RabbitMQ message so the processor's Serilog enricher threads logs end-to-end.
- **BFF never touches `notification_deliveries.{status, retry_count, next_retry_at, locked_*, processed_at, failed_at, last_error, version}` or `notification_logs` after insert** — every state column is processor-owned.
- If RabbitMQ publish fails, the Pending row stays Pending; the processor's
  pending-grace poller (60s) picks it up. Self-healing — no outbox table needed.

### RabbitMQ queues

`notification.email`, `notification.sms`, `notification.whatsapp`, `notification.firebase`, `notification.inapp` —
declared durable on first publish with `x-dead-letter-exchange=bid.dlx` (shared
with the rest of the BidProcessor pipeline).

### Templates (Handlebars)

- Engine: **Handlebars.Net** in the processor — syntax is `{{FirstName}}`, not Razor `@Model.X`.
- `subject` and `body` are both Handlebars. String values inside the `metadata` JSONB are also rendered (useful for InApp `actionUrl`, Firebase FCM data payload).
- Model = whatever the publisher puts in `notifications.payload`. Keys are case-sensitive.
- Cache invalidation on the processor side is automatic via `updated_at`.

Seeded by migration `0002`: `WELCOME` (Email+InApp), `TEAM_INVITATION` (Email+InApp), `PASSWORD_RESET` (Email), `EMAIL_VERIFICATION` (Email). Edit via `/internal/notification-templates` or add new ones in a future migration.

### Calling the publisher from a service

```csharp
public class AuthService(INotificationPublisher publisher, ...)
{
    public async Task HandleOAuthCallbackAsync(...)
    {
        ...
        await publisher.SendAsync(new SendNotificationDto(
            Category:     NotificationCategory.Transactional,    // 5 retries
            TemplateCode: "WELCOME",
            UserId:       user.Id,
            Payload:      new Dictionary<string, object>
            {
                ["FirstName"]        = user.Name,
                ["OrganizationName"] = org.Name,
            },
            Recipients: new[]
            {
                new NotificationRecipientDto(NotificationChannel.Email, user.Email),
                new NotificationRecipientDto(NotificationChannel.InApp, user.Id.ToString()),
            }), ct);
    }
}
```

**Wired triggers (in-BFF):**
| Event | Service | Template | Channels |
|---|---|---|---|
| Password signup start (`POST /api/auth/register`) | `AuthService.StartRegistrationAsync` | `EMAIL_VERIFICATION` (6-digit OTP) | Email |
| Email verified (`POST /api/auth/verify-email`) → account created | `AuthService.VerifyEmailAsync` → `CreateVerifiedAccountAsync` | `WELCOME` | Email + InApp |
| First-time OAuth signup | `AuthService.HandleOAuthCallbackAsync` (only when `isNewUser`) | `WELCOME` | Email + InApp |
| Org member invite | `OrganizationService.InviteMemberAsync` | `TEAM_INVITATION` | Email + InApp (existing users: link to SPA `/invites/accept?token=` — membership only on explicit accept; unregistered: registration link, Email only) |
| Forgot password (`POST /api/auth/forgot-password`) | `AuthService.RequestPasswordResetAsync` | `PASSWORD_RESET` (6-digit OTP) | Email |

**Verify-first signup:** password signup no longer creates the account directly.
`register` stashes BCrypt-hashed credentials in `pending_registrations` (migration
`0006`) + emails a 6-digit OTP; the `User`/`Organization`/`OrgMember` are created
only by `verify-email` once the code is confirmed (`resend-verification` re-issues).
This applies to invite signups too (the invite token is carried on the pending row
and consumed at verify time). OAuth is unchanged. In Development the `register`
response includes a `devCode` so the flow is testable without a mailbox.

**Password reset (OTP):** `forgot-password` emails a 6-digit OTP whose hash is stored
in `password_reset_codes` (migration `0007`); `reset-password` verifies the code, sets
the new BCrypt password, and **revokes all of the user's refresh tokens** (existing
sessions die). `forgot-password` always returns 200 with the same shape (no
enumeration) and sends nothing for unknown emails or OAuth-only users; in Development
it includes a `devCode`. Same OTP helpers/expiry/attempt-cap as verify-first signup.

Each call is wrapped in a try/catch with `ILogger.LogWarning` — notification
failures NEVER fail the parent flow (the user was created / the membership was
persisted regardless). RabbitMQ hiccups self-heal via the processor's
pending-grace poller.

**External triggers:** `POST /internal/notifications` (API-key) for sources outside
the BFF (BidProcessor, admin tools).

### Config

```json
"RabbitMq": {
  "HostName":           "13.233.138.227",
  "Port":               5672,
  "Username":           "<set in secrets>",
  "Password":           "<set in secrets>",
  "VirtualHost":        "/",
  "DeadLetterExchange": "bid.dlx",
  "ClientName":         "BiddingBuddyBFF"
}
```

`RabbitMqPublisher` is a singleton holding one `IConnection`; it opens a fresh
channel per publish (cheap), declares the target queue idempotently, and sends
persistent JSON.

## Tender-match digests

Matches freshly-enriched tenders against each org's saved "interests" and delivers
them as a **batched digest** (a group of tenders per email, not one-per-tender).

### Tables (migration `0004_add_tender_matching.sql`)

| Table | Purpose |
|---|---|
| `tender_alert_rules` | Per-org interest. Optional `categories[]`, `states[]`, `keywords[]`, `min_value`/`max_value`, `min_ai_score`, `is_active`. Empty constraint = ignored (AND of the set ones). |
| `org_alert_settings` | Per-org delivery prefs: `is_enabled`, `min_send_interval_minutes` (cooldown, default 360 = 6 h; migration `0011`), `last_digest_sent_at` (server-managed), `notify_channels[]` (default Email,InApp), `notify_roles[]` (default owner,admin,bid_manager). `digest_size` is retained for back-compat but no longer gates sending — the cooldown does. |
| `tender_matches` | Buffer + dedup. `status` pending→sent→expired, `batch_id`, `sent_at`. UNIQUE `(org_id, tender_id)` so a tender is queued once per org even across rules / re-enrichment. |

### Flow

Alerting is **decoupled from ingestion** and driven by a scheduled scan (migration
`0008` adds the `tenders.alerts_scanned_at` marker). The old inline on-upsert
matching + count-trigger digest was removed.

```
Ingestion (no longer triggers matching):
POST /internal/tenders (from EnrichBidWorker)
  └─ InternalPipelineService.UpsertTenderAsync — upsert only; new rows get alerts_scanned_at = NULL.

Alerting (primary path — the scheduled scan):
TenderMatchScanWorker  (every Matching:ScanIntervalSeconds, default 15m) ─┐
POST /internal/matching/scan  (manual / external scheduler)             ─┴─ MatchingService.ScanNewTendersAsync
     1. pull tenders WHERE alerts_scanned_at IS NULL (oldest first), in batches
     2. test each LIVE tender against every active rule → org → matched tenders
        (deduped per org across rules; disabled orgs skipped)
     3. insert tender_matches (status 'pending', deduped by UNIQUE(org,tender)) — buffered, NOT sent yet
     4. stamp the batch alerts_scanned_at = now()  → never re-picked (idempotent)
     5. flush each org with a pending backlog whose cooldown has elapsed
        (now − last_digest_sent_at ≥ min_send_interval_minutes): group ALL its pending
        matches into ONE digest → INotificationPublisher.SendAsync("TENDER_MATCH")
        (Email + InApp, soonest-closing first, recipients = active org members in notify_roles),
        then stamp last_digest_sent_at. This is what prevents the one-email-per-tick flood.

POST /internal/digests/flush  (manual force-drain)
  └─ MatchingService.FlushAllDueAsync — flushes every org's 'pending' matches IGNORING the
     cooldown (force). Use to drain immediately; normal sends are cooldown-gated by the scan.
```

Dedup is layered: per-org in a run (a tender matching 2 rules is queued once) ·
`alerts_scanned_at` (a tender is evaluated once, ever) · `tender_matches`
UNIQUE(org,tender) (DB guard). Concurrent runs are gated by an in-process semaphore.

- **Template:** `TENDER_MATCH` (Email + InApp), seeded by `0004`, redesigned by `0012` (deadline-first layout, TendersAgent brand + logo). Payload is `{ FirstName, Count, One, FirstTitle, LogoUrl, SummaryLine, ShowTotal, TotalValue, AllUrl, Tenders[]{Rank,Title,Url,Category,State,Value,ClosingDate,DaysLeftLabel,IsUrgent} }` — the Email body iterates `{{#each Tenders}}`. `Url` is built from `tenders.mongo_tender_id` (migration `0010`) so links resolve to the right tender for any source portal.
- **Why in the BFF:** rules (Postgres), org/user data, and `INotificationPublisher` all live here, so matching is one in-process step off the existing `/internal/tenders` upsert — no extra pipeline plumbing.
- **Matching semantics:** within a field = OR (`categories [laptop, server]`), across fields = AND. Categories/states match **exactly** (case-insensitive); keywords are substring over title/description/summary/tags. Multiple rules per org = OR. Surfaced in the Interests-tab copy.
- **Services:** `ITenderAlertRuleService` (CRUD + settings), `IMatchingService` (`ScanNewTendersAsync` scheduled scan + `FlushAllDueAsync` legacy fallback). Worker: `TenderMatchScanWorker` (config `Matching:*`). UI: SettingsPage → **Interests** tab.
- **Go-live:** apply migrations `0004` + `0008`; the scan runs in-process via `TenderMatchScanWorker` — no external cron required (or drive `POST /internal/matching/scan` instead). `0008` marks all existing tenders scanned so the first run won't blast the backlog; use `?backfill=true` for a deliberate one-time backfill. Ensure BidProcessor `BffInternalApi:ApiKey` matches `Pipeline:ApiKey` so tenders actually reach Postgres to be matched.

## Key Reference

**Read `CONTEXT.md` for:** complete schema with all column definitions, index strategy, full API request/response examples, and RBAC rules. It is the single authoritative design document for this project.
