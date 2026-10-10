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

## What is in place (week 4)

- **Mark attendance** (`/attendance`): pick a date and a batch (today's batches first, ticked when already marked);
  every enrolled student starts as Present, tap a student to switch Present/Absent, menu for Late/Leave,
  sticky "Save attendance" button for phones. Saving again updates the same register (one per batch per day).
- Only students enrolled in the batch on that date are listed (joining and leaving dates are respected).
- Teachers see only their own batches and can change attendance for the last 7 days; Owner/Staff any date.
  Future dates are blocked.
- **Student profile → Attendance tab**: last 90 days, percentage (Late counts as present, Leave not counted), recent entries.
- **Batch page → Attendance this month**: classes marked, average, each student's percentage (lowest first).
- Tests: attendance summary unit tests; integration tests for marking and correcting, history and percentages,
  date rules and the register roster.

## What is in place (week 5)

- **Fee dues are created automatically** when a student joins a batch (admission or "Add to batch"), in the same save
  as the enrollment (`FeeDueGenerator`, design doc 6.3):
  - One-time: one due for the full fee on the joining date.
  - Instalments: whole-rupee split, last instalment takes the remainder; first due in the joining month on the plan's
    due day (never before joining), then every N months; the discount comes off the last instalments first.
  - Monthly: the joining month is created immediately (period key `yyyy-MM`); the discount applies to every month.
- Discount cannot exceed the fee (total for one-time/instalments, monthly fee for monthly plans).
- **Student profile → Fees tab**: every due with amount, discount, paid, balance, overdue status and totals.
  The owner can waive an unpaid due.
- **Fee plans → "Create missing dues"**: creates dues for enrollments that have none (e.g. students added before
  week 5) and monthly dues up to the current month. Safe to run any time; since week 7 a daily job does this automatically.
- Tests: generator unit tests for all three billing types; integration tests for dues on admission, catch-up,
  waiving and discount limits.

## What is in place (week 6)

- **Collect fee** (`/payments/new`, or "Collect fee" on a student's Fees tab / pending dues list): find the student,
  see total due, overdue and advance credit; enter amount, mode (Cash, UPI, Card, Bank transfer, Cheque, Online),
  reference and date. The page previews how the money is applied (oldest due first) and allows adjusting the split.
  Extra money is kept as advance.
- Saving runs in one database transaction: open dues are reloaded with their concurrency tokens, dues are updated,
  the next receipt number is taken atomically per financial year (`PREFIX/2026-27/00001`, `receipt_counters` upsert)
  and payment + allocations are stored. If someone else changed the same dues at the same moment, the user is asked
  to reload and try again.
- **Receipt** (`/payments/{id}`): on-screen receipt, **Print / PDF** (`/receipts/{id}.pdf`, A5, QuestPDF Community
  licence), amount in words (lakh/crore), balance due now, and **Share on WhatsApp** (opens WhatsApp with a ready message).
- **Cancel payment** (Owner only): reverses the amounts on the dues and keeps the receipt marked CANCELLED with a reason
  (audit log entry "Cancel"). Payments are never edited.
- **Payments** (`/payments`): date range, mode, search; total collected (cancelled excluded).
- **Pending dues** (`/fees/dues`): Overdue / Due this week / All pending, batch filter, search, days late,
  call / WhatsApp the parent, Collect button.
- **Dashboard** shows today's and this month's collection and total pending dues (Owner/Staff).
- Tests: allocator, receipt number format and amount-in-words unit tests; integration tests for oldest-first
  allocation and receipt numbering, manual split and advance, cancellation, validation/permissions, PDF output and
  the pending dues list.

## What is in place (week 7)

- **Background jobs** with Hangfire on PostgreSQL (tables in the `hangfire` schema, created on first start):
  - `MonthlyDueJob` – daily 00:30 IST, creates the new month's dues for monthly plans (and any missing dues) for every
    institute on trial or active.
  - `FeeReminderJob` – daily 10:00 IST: dues due in 3 days, due today, or overdue (repeated every 7 days); one message
    per primary guardian with the total for all their children; never twice on the same day.
  - `SendReceiptJob` – queued after each payment: receipt message with a public link `/r/{token}` (signed with ASP.NET
    Data Protection, valid 90 days, opens the PDF without signing in).
  - `AbsentAlertJob` – queued when today's attendance is saved with absent students; once per student per register.
  - Each job sets the institute on `JobTenantContext` in its own scope, so tenant filters apply. Jobs are safe to retry.
  - Dashboard: `/hangfire`, platform admin only ("Background jobs" in the menu).
- **Messaging**: `IMessageSender` with `FakeMessageSender` (development) – messages are written to the log/Seq
  instead of being sent. WhatsApp when the guardian opted in, otherwise SMS when the plan allows it (trials: yes).
  Templates from `message_templates` (institute template first, then the system default; SMS copies are seeded).
  Every message is stored in `message_logs`.
- **Reminders page** (`/reminders`): message log with date/type/status filters, search, counts, and
  **Send today's reminders now**. In Development a button marks fake messages as delivered.
- **Receipt page**: shows the receipt message status and **Send again**.
- **Delivery webhook** `POST /webhooks/whatsapp`: `{"messageId":"…","status":"delivered|read|failed"}` (or an array),
  header `X-Signature: sha256=<HMAC-SHA256 of the body with Messaging:WebhookSecret>`; without a secret it only
  works in Development. Statuses never move backwards. Try it with `src/InstituteHub.Web/InstituteHub.Web.http`.
- Settings: `App:PublicBaseUrl` (links in messages), `Messaging:Provider` (only `Fake` for now), `Messaging:ApiKey`,
  `Messaging:WebhookSecret`, `Hangfire:ServerEnabled` (false in integration tests).
- Tests: reminder rules, template rendering, status rules and formatting (unit); reminders once a day, delivery
  updates, receipt message + public link, SMS fallback, absent alerts and tenant isolation (integration).

## What is in place (week 8) – ready for a pilot institute

- **Dashboard**: today's and this month's collection, pending and overdue dues (with student counts), today's classes
  with attendance status and a Mark button, quick actions by role, trial and plan-limit warnings, setup progress.
- **Reports** (`/reports`): collections by payment mode, batch and day; dues ageing (not due / 1–30 / 31–60 / 61–90 /
  90+ days late) with the most overdue students; attendance per batch and students below 75 %. Date presets (this
  month, last month, financial year) and CSV downloads (`/reports/collections.csv`, `/reports/dues.csv`,
  `/reports/attendance.csv`, Excel-friendly UTF-8). Teachers see attendance for their own batches only.
- **CSV import** (`/students/import`, "Import CSV" on the student list): template download, upload, row-by-row check
  with plain-language problems, then import through the normal admission (plan limit, enrollment and fee dues).
  Guardians with an existing phone number are linked (siblings).
- **Settings** (`/settings`, owner): institute details and receipt prefix, logo upload (printed on receipt PDFs), users
  (add Staff/Teacher with a temporary password, reset password, deactivate), plan usage and features.
- **Onboarding checklist**: details, logo, fee plan, batch, students (import), staff and teachers.
- **Plan limits**: `IFeatureService` reads the plan (cached 5 minutes per institute) – max active students on admission,
  import and re-activation; SMS and absent-alert features (trials get everything).
- **Deployment**: `Dockerfile` (app + EF migration bundle), `deploy/docker-compose.prod.yml` (app, PostgreSQL, Caddy
  HTTPS, Seq), `deploy/.env.example`, CI job that pushes the image to GitHub Container Registry.
- **Backups**: `deploy/backup.sh` (daily, AES-256 encrypted database + files, 30 days, optional off-site copy) and
  `deploy/restore.sh`.
- **Monitoring**: `/health` and `/health/ready` (database + background jobs, JSON), `/health/live`; Seq in production;
  see `deploy/README.md` for the uptime monitor and the pilot checklist.
- Tests: CSV, import parsing, ageing buckets, plan features, profile validation (unit); import with siblings, plan
  limit, reports, user management and profile (integration).

## Next

Connect a real WhatsApp/SMS provider behind `IMessageSender`; platform admin pages (`/admin`: institutes, trials,
plans); online subscription payments (Razorpay, phase 3); bulk reminders from the pending dues page.
