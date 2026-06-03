# BiddingBuddyBFF

ASP.NET Core 8 Backend-For-Frontend. Provides a single, client-optimized REST API surface for the React SPA. Handles OAuth 2.0 + JWT auth, multi-tenant org scoping, and exposes endpoints for all procurement features (tenders, bids, documents, compliance, orders, payments, competitors, AI analysis). Receives enriched data from the BidProcessor pipeline via internal API-key-protected endpoints.

## Solution Layout

```
BiddingBuddyBFF/
├── BiddingBuddyBFF.sln
├── CONTEXT.md                        Authoritative 43KB architecture + schema reference — READ THIS
├── database/
│   └── schema.sql                    Full PostgreSQL DDL (28 tables, indexes, triggers)
└── src/
    ├── BiddingBuddy.Bff.Api/         ASP.NET Core entry point
    │   ├── Program.cs                DI wiring, middleware pipeline
    │   ├── Controllers/              14 controllers (see Endpoints section)
    │   ├── Middleware/
    │   │   └── OrgContextMiddleware.cs   X-Org-Id header validation + membership check
    │   └── appsettings.json
    ├── BiddingBuddy.Bff.Core/        Domain layer (no infra deps)
    │   ├── Entities/                 28 EF Core entity classes
    │   ├── DTOs/                     Request/response DTOs per feature
    │   └── Services/Repositories/   Interfaces only
    └── BiddingBuddy.Bff.Infrastructure/  Data + external services
        ├── Data/
        │   ├── BiddingBuddyDbContext.cs   EF Core DbContext (PostgreSQL/Npgsql)
        │   └── Repositories/             IRepository implementations
        └── Services/
            ├── AuthService.cs            JWT minting, refresh token rotation
            ├── OAuthProviderService.cs   Google + GitHub OAuth 2.0 code exchange
            └── ...                       Per-feature service implementations
```

## Architecture

```
bidding-buddy-ui (React SPA)
        │ HTTPS + JWT Bearer + X-Org-Id header
        ▼
BiddingBuddyBFF  (this project)
   Controllers → Services (Core) → Repositories (Infrastructure) → PostgreSQL
        ▲
        │ POST /internal/* + X-Api-Key header
BidProcessor pipeline (async enrichment workers)
```

**Clean Architecture layers:**
- **API** — Controllers, middleware (presentation only, no business logic)
- **Core** — Entities, interfaces, DTOs (no infrastructure dependencies)
- **Infrastructure** — EF Core DbContext, repositories, JWT/OAuth service impls

## API Endpoints

### Public (no auth)
| Method | Path | Purpose |
|---|---|---|
| GET | `/api/auth/oauth/{provider}` | Initiate OAuth (Google/GitHub) |
| GET | `/api/auth/oauth/{provider}/callback` | OAuth code exchange |
| POST | `/api/auth/refresh` | Rotate refresh → new access + refresh token |
| GET | `/api/auth/providers` | List enabled OAuth providers |

### Authenticated (Bearer JWT + `X-Org-Id` header required for org-scoped routes)
| Controller | Base Route | Key Operations |
|---|---|---|
| Auth | `/api/auth` | `GET /me`, `POST /logout` |
| Organizations | `/api/organizations` | CRUD, member management, role assignment |
| Tenders | `/api/tenders` | List/filter, get detail, save, track, documents, AI analysis |
| Bids | `/api/bids` | List, create, update, stage progression (7 stages), activities, checklist |
| Compliance | `/api/compliance` | Requirements, documents, health score |
| Documents | `/api/documents` | List, upload (presigned S3), download, folder management, versioning |
| Orders | `/api/orders` | CRUD, line items, delivery milestones |
| Payments | `/api/payments` | EMD (bid deposits), invoices, payment summary |
| Competitors | `/api/competitors` | List, detail, market summary |
| Analysis | `/api/analysis` | Dashboard KPIs, recommendations, performance, market trends |
| Notifications | `/api/notifications` | List, mark read, preferences |
| Integrations | `/api/integrations` | GeM portal config, sync trigger, sync status |

### Internal Pipeline (API-key only — `X-Api-Key` header)
| Method | Path | Purpose |
|---|---|---|
| POST | `/internal/tenders` | Upsert enriched tender from BidProcessor |
| POST | `/internal/tenders/{gemTenderId}/documents` | Store extracted document text |
| POST | `/internal/competitors` | Record competitor bid observation |
| POST | `/internal/analysis` | Store AI analysis results |

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
Applied to all routes except `/api/auth/*`, `/internal/*`, `/swagger`, `/health`:
1. Reads `X-Org-Id` header → validates UUID
2. Extracts `sub` claim from JWT
3. Checks `organization_members` table — 403 if not a member
4. Sets `HttpContext.Items["OrgId"]` for downstream controllers

### Role-based access
Roles (stored in `organization_members.role`): `owner`, `admin`, `bid_manager`, `finance`, `sales`, `viewer`

## Database

**PostgreSQL** (Npgsql + EF Core 8). No MongoDB.

- **Dev connection:** `Host=13.233.138.227;Port=5432;Database=biddingbuddy;Username=postgres;Password=Fiserv@123`
- **28 tables** — full DDL in `database/schema.sql`
- All tables have `updated_at` trigger (auto-updated)
- `pgcrypto` extension for UUID generation

Key table groups:

| Group | Tables |
|---|---|
| Auth | `users`, `oauth_accounts`, `refresh_tokens` |
| Multi-tenancy | `organizations`, `organization_members` |
| Procurement | `tenders`, `saved_tenders`, `tender_documents`, `tender_analysis` |
| Bids | `bids`, `bid_activities`, `bid_checklists` |
| Compliance | `compliance_requirements`, `compliance_documents` |
| Documents | `documents`, `document_versions`, `document_folders` |
| Fulfillment | `orders`, `order_items`, `order_milestones` |
| Finance | `emd_deposits`, `invoices` |
| Intelligence | `competitors`, `competitor_observations` |
| Platform | `notifications`, `gem_integrations`, `analysis_results` |

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
    "GitHub": { "ClientId": "...", "ClientSecret": "...", "RedirectUri": "https://localhost:7100/api/auth/oauth/github/callback" }
  },
  "Frontend": { "BaseUrl": "http://localhost:3000", "AuthCallbackPath": "/auth/callback" },
  "Pipeline": { "ApiKey": "pipeline_internal_secret_CHANGE_ME" }
}
```

## NuGet Dependencies

| Package | Version | Purpose |
|---|---|---|
| Microsoft.AspNetCore.Authentication.JwtBearer | 8.0.11 | JWT middleware |
| Npgsql.EntityFrameworkCore.PostgreSQL | 8.0.11 | PostgreSQL ORM |
| Microsoft.EntityFrameworkCore.Design | 8.0.11 | EF migrations tooling |
| System.IdentityModel.Tokens.Jwt | 7.6.3 | JWT parsing/validation |
| Microsoft.Extensions.Http | 8.0.1 | HttpClientFactory (OAuth calls) |
| Swashbuckle.AspNetCore | 6.6.2 | Swagger UI |

## Running

```bash
cd src/BiddingBuddy.Bff.Api
dotnet run
# Listens on https://localhost:7100
```

EF Migrations:
```bash
dotnet ef migrations add <Name> --project ../BiddingBuddy.Bff.Infrastructure
dotnet ef database update
```

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
| `bidding-buddy-org-docs` (**R2**) | BiddingBuddyBFF | Org-uploaded documents (GST certs, PAN, bid docs, etc.) |
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
| `R2:BucketName` | `appsettings.json` | `bidding-buddy-org-docs` |
| `R2:Endpoint` | `appsettings.json` | `https://{AccountId}.r2.cloudflarestorage.com` |
| `R2:PresignTtlSeconds` | `appsettings.json` | `900` (15 min) |
| `R2:MaxUploadSizeKb` | `appsettings.json` | `102400` (100 MB) |
| `R2:AccessKeyId` | **user-secrets / env var** | R2 API token — never commit |
| `R2:SecretAccessKey` | **user-secrets / env var** | R2 API token — never commit |

Local dev: `dotnet user-secrets set "R2:AccessKeyId" "..." --project src/BiddingBuddy.Bff.Api`

### Bucket setup (one-time)

1. Create bucket `bidding-buddy-org-docs` in Cloudflare R2.
2. Create an R2 API token: **Object Read & Write** scoped to that bucket only.
3. Apply CORS policy to the bucket:

```json
[
  {
    "AllowedOrigins": ["http://localhost:3000", "https://app.biddingbuddy.com"],
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
  "uploadUrl": "https://…r2.cloudflarestorage.com/bidding-buddy-org-docs/orgs/{orgId}/docs/{uuid}/gst_cert.pdf?X-Amz-…",
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

## Key Reference

**Read `CONTEXT.md` for:** complete schema with all column definitions, index strategy, full API request/response examples, and RBAC rules. It is the single authoritative design document for this project.
