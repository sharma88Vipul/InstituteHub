# InstituteHub

Multi-tenant SaaS for coaching institutes: students, batches, attendance, fees and parent reminders.
Stack: .NET 10, Blazor Web App (Interactive Server) + MudBlazor, EF Core + PostgreSQL, ASP.NET Core Identity.

See `InstituteHub_Technical_Design.docx` for the full design. This repository currently contains the
**week 1 foundation** (design doc section 8) plus the full domain model from section 4.

## Prerequisites

- .NET 10 SDK
- Docker Desktop
- EF Core CLI: `dotnet tool install --global dotnet-ef`

## First-time setup

```powershell
# 1. Start PostgreSQL 16 and Seq
docker compose up -d

# 2. Restore packages (creates packages.lock.json files – commit them)
dotnet restore
dotnet build

# 3. Create the first migration (all tables from design doc section 4)
./scripts/add-migration.ps1 InitialCreate

# 4. Secrets for seed users (never commit passwords)
dotnet user-secrets --project src/InstituteHub.Web set "Seed:PlatformAdminEmail" "you@example.com"
dotnet user-secrets --project src/InstituteHub.Web set "Seed:PlatformAdminPassword" "<strong password>"
dotnet user-secrets --project src/InstituteHub.Web set "Seed:DemoOwnerPassword" "<strong password>"

# 5. Run – in Development the app applies migrations and seeds data on start-up
dotnet run --project src/InstituteHub.Web --launch-profile https
```

- App: https://localhost:7227 (demo owner: `owner@demo.local`)
- Health: https://localhost:7227/health
- Logs (Seq): http://localhost:5341

## Tests

```powershell
dotnet test tests/InstituteHub.UnitTests
dotnet test tests/InstituteHub.IntegrationTests   # needs Docker (Testcontainers PostgreSQL)
```

Integration tests start a real PostgreSQL container, apply the migrations and check tenant isolation.
They need the `InitialCreate` migration from step 3.

## Solution layout

| Project | Responsibility |
| --- | --- |
| `InstituteHub.Domain` | Entities, enums and domain rules (no EF, no ASP.NET) |
| `InstituteHub.Application` | Use-case services, DTOs, validators, abstractions (`ITenantProvider`, `IClock`, ...) |
| `InstituteHub.Infrastructure` | `AppDbContext`, configurations, migrations, audit interceptor, Identity, seeding |
| `InstituteHub.Web` | Blazor pages, Identity UI, endpoints, DI and middleware |

## What is in place (week 1)

- Solution skeleton, `Directory.Build.props` (nullable, implicit usings, warnings as errors, lock files), `global.json`
- `docker-compose.yml` (postgres:16, Seq) and GitHub Actions CI (`build/ci.yml` – move it to `.github/workflows/ci.yml`)
- Domain model for every table in section 4.3 (UUID v7 keys, audit columns, soft delete)
- `AppDbContext` = Identity + business data on PostgreSQL, snake_case names, money as `numeric(12,2)`,
  enums as strings, indexes/constraints from section 4.4, `xmin` concurrency on `fee_dues` and `payments`
- Tenant isolation: global query filters (tenant + soft delete) and a SaveChanges interceptor that stamps
  `tenant_id`, blocks cross-tenant writes, sets audit columns, converts deletes to soft deletes and writes `audit_logs`
- Identity moved to Infrastructure (`AppUser`/`AppRole` with Guid keys), `tenant_id` claim at sign-in,
  roles, lockout after 5 failed logins, rate limiting on `/Account` posts
- Serilog (console + Seq) enriched with TenantId/UserId, `/health` check, authorization policies from section 5.2
- Seed data: roles, subscription plans, system message templates, platform admin from config, demo tenant (dev)

## What is in place (week 2)

- Institute sign-up at `/signup`: tenant (14-day trial) + Owner user + GROWTH trial subscription in one
  transaction, receipt prefix from the institute's initials, phone numbers stored in E.164.
  `/Account/Register` now forwards to `/signup`.
- Global interactive routing (Blazor Server) with a MudBlazor mobile-first layout: app bar, responsive drawer,
  role-aware navigation built from the authorization policies. Login/sign-up pages use a simple centred layout.
- `/dashboard` (institute, trial days left, setup progress, quick actions) and `/onboarding` checklist.
  Modules from later weeks show a "coming soon" page.
- `IAppDbContext` so Application services query the tenant-filtered database; `TenantService`.
- Data Protection keys can be persisted with `DataProtection:KeysPath` (use a volume in Docker).
- Tests: phone/validator/slug unit tests; integration tests for sign-up, rollback, duplicate email and
  "two institutes cannot see each other".

## What is in place (week 3)

- **Students** (`/students`): search by name, admission no. or phone; filter by status and batch; mobile card view.
- **Admission** (`/students/new`): student + guardian + batch + fee plan in one form. Typing a guardian phone that
  already exists offers to link that guardian (siblings). Admission numbers are generated (prefix + 0001) when left blank.
  The subscription plan's student limit is checked.
- **Student profile** (`/students/{id}`): profile, guardians (add, edit, make primary, WhatsApp link), batches
  (add to batch, remove), status (Active / Inactive / Left – Left ends active enrollments), edit page.
- **Batches** (`/batches`, `/batches/new`, `/batches/{id}`): schedule (days + times), teacher, capacity, default fee plan,
  enrolled students, activate/deactivate.
- **Fee plans** (`/fees/plans`): basic create/edit (one-time, instalments, monthly). Dues are generated in week 5.
- Permissions are checked inside the services: Owner/Staff manage; a Teacher only sees students and batches they teach.
- Tests: validators/formatting unit tests; integration tests for admission into a batch, sibling guardian,
  plan limit, double enrollment / full batch, and teacher scope.

## Next (week 4)

Attendance: sessions and records, mobile marking page, history on the student and batch pages.
