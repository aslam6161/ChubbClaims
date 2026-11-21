# AI Working Journal

Running log of how I directed the AI (Claude Code): what I asked, what I accepted, what I challenged, what I overrode. Appended during the work, one entry per prompt that mattered. Not polished on purpose.

---

### 2026-11-21 · Planning · Specs from the brief
- Asked: turn the Senior/Lead brief into two build specs (BACKEND.md, FRONTEND.md) with priorities, phases and the decisions the panel will probe.
- Accepted: modular monolith + Kafka for downstream reactions; transactional outbox; idempotent consumers; task-based endpoints; exposure as an event-fed projection; Angular role areas with `canMatch` guards.
- Challenged: nothing yet at this stage; reviewed in the next entries.
- Overrode: n/a.

### 2026-11-21 · Planning · Align structure with my own projects
- Asked: align the backend and frontend folder structure with my existing projects (Bento_BE for .NET, IMSAnuglar for Angular), structure only, no code copied; solution named `ChubbClaims` with `ChubbClaims.Domain`, `ChubbClaims.Repository`, …
- Accepted: Bento_BE folder names (`Entity/`, `Constants/`, `Settings/`, `DataContext.cs`, `Data/Seed.cs`, `Service/Exceptions`, `Helper/`, single `.Tests` project); IMSAnuglar folder names (`_services`, `_guards`, `_interceptors`, `_models`, `_layout`, `.component.ts/html/css`).
- Challenged: the AI flagged that IMSAnuglar is Angular 12 with NgModules and `*ngIf`. I agreed to take only folder naming and keep Angular 22 standalone + signals.
- Overrode: kept the spec over my own project where it matters: no AutoMapper (commercial licence), ProblemDetails instead of my custom `ExceptionMiddleware`, `DependencyInjection.cs` per layer.

### 2026-11-21 · Planning · .sln vs .slnx
- Asked: why `.slnx`?
- Accepted (after checking): the AI first recommended `.sln` to match Bento_BE. I checked the installed SDK (10.0.402): `dotnet new sln` produces `.slnx` by default in .NET 10.
- Overrode: kept `.slnx`, the SDK default; nothing in this project needs the old format.

### 2026-11-21 · Planning · API project name and style
- Asked: `ChubbClaims.API` with Minimal APIs (AI recommendation) or Bento-style.
- Overrode: chose `ChubbClaims.Presentation` with Controllers + `BaseApiController`, the structure I use in production and can defend line by line.
- Challenged: the AI pointed out that `[Route("api/[controller]")]` gives `/api/WorkQueue`, not the spec's `/api/work-queue`; agreed on explicit kebab-case routes per controller.

### 2026-11-21 · Planning · Spec review against the three briefs
- Asked: re-check both specs against every brief question and deliverable.
- Accepted fixes:
  - **Exposure drift bug:** approval resets the reserve to the settlement amount, but the projection ignored `ClaimApproved`, so outstanding exposure would be wrong after every approved claim. `ClaimApproved` now carries old/new reserve and the projector applies the delta; `releasedReserve` is defined precisely.
  - **Team workload was manager-only**, but the brief says claims staff see team workload and performance. Workload is now open to officers (own market); exposure stays manager-only.
  - **Seed data contradiction:** claims in six markets but policies only in two, while a claim's market comes from its policy. Added a background claimant per market.
  - Added the read/write profile table, entity relationships, the journal format, `git init` in Phase 1, and missing config keys.
- Decided: my role is Senior full stack → Senior/Lead brief → real backend first, partial frontend, no mock backend.

### 2026-11-21 · Planning · Git workflow
- Asked (my idea): a feature branch per phase, commit there, merge into `main`.
- Accepted from the AI: several small commits per branch, `--no-ff` merges (not squash) so the panel sees each phase as a block with its steps, merge only when green.
- Overrode: the AI proposed numbered branch names (`feature/01-scaffold`); I dropped the numbers (`feature/scaffold`), the merge order already shows the sequence.

### 2026-11-21 · Phase 1 · Scaffold
- Asked: Phase 1 per BACKEND.md §12: solution, projects, references, Serilog, OpenAPI + Scalar, ProblemDetails, docker compose.
- Accepted: two-stage Serilog bootstrap; text logs locally, compact JSON in the container (`LogFormat=Json`); built-in OpenAPI 3.1 + Scalar (Development only); Bearer scheme transformer so Scalar can "Authorize"; CORS for `localhost:4200` with credentials (needed for SignalR later).
- Challenged: correlation id is client input that goes into logs and Kafka headers, so the middleware only accepts `[A-Za-z0-9._-]{1,64}` and replaces anything else (log-injection guard). Covered by tests.
- Challenged: unknown exceptions must not leak details; the 500 ProblemDetails has no `detail`. Covered by a test that a secret in the exception message never reaches the response.
- Overrode: removed the template's WeatherForecast sample and HTTPS redirection (HTTP only behind compose/dev proxy). Did not add a Serilog `CorrelationIdEnricher` class (in the spec layout): `LogContext.PushProperty` in the middleware already covers every log line in the request, so the extra class would be dead weight.

### 2026-11-21 · Phase 2 · Domain: claim aggregate and state machine
- Asked: Claim aggregate, lifecycle state machine, events, value objects and domain tests per BACKEND.md §5 and §11.
- Accepted: transition table as data (`ClaimTransitions`) instead of if/else per method; one aggregate method per task (PickUp, RequestInformation, …); audit rows (status, reserve) written inside the aggregate; events carry everything consumers need; time passed in, never read from the clock.
- Challenged: who-may-act rules. The spec put them in the application layer; I asked for them in the aggregate (`Actor` passed to every method) so an endpoint can never forget a check. Added `ClaimAccessDeniedException` → 403.
- Challenged: check order. Transition first (409), then actor (403), then input (422): a second officer picking up an already picked-up claim must get 409 "already picked up", not 403.
- Challenged: "incident date not in the future" compared to the UTC date would reject a legitimate same-morning claim in Sydney (UTC+11). Compared against the market's local date instead; test covers Sydney morning and KL late evening.
- Challenged: do the tests bite? Ran two mutations: (1) re-introduced the exposure drift bug in `Approve`, (2) removed the market check. Each was caught by 2 failing tests; restored, 147/147 green.
- Overrode: dropped the `Money` value object from the spec: one currency per claim (from the policy) makes it redundant. Dropped Domain `Settings/` for now: config POCOs arrive with the phases that bind them.
- Added: an "exposure consistency" test: the sum of event deltas a claim emits must equal its outstanding reserve (zero once closed). It locks in the fix for the drift bug found in spec review.


### 2026-11-21 · Phase 3 · Persistence, seed data, transactional outbox
- Asked: EF Core 10 + SQL Server: DbContext, configurations, migration, seed through the domain, repositories, outbox interceptor (BACKEND.md §12 phase 3).
- Accepted: owned types for Motor/Property details (columns on Claims, not JSON: two small fixed shapes); RowVersion as an EF shadow property (concurrency stays out of the domain); enums stored as strings; decimal(18,2) by convention; claim numbers from a SQL sequence; outbox rows added in a SaveChanges interceptor so claim + events commit in one transaction; events serialised into the §6 envelope at write time.
- Challenged: EF 11 was the "latest" package but targets .NET 11 only; pinned EF Core 10.0.13 to stay on the .NET 10 LTS runtime the spec chose.
- Challenged: NuGet flagged a high-severity advisory (GHSA-846f-f764-76mx) in System.Security.Cryptography.Pkcs 9.0.11, pulled in by Microsoft.Data.SqlClient. Pinned 10.0.15 directly with a comment; `dotnet list package --vulnerable` now clean.
- Challenged: Domain-generated Guid keys + EF default "generated on add" would make EF UPDATE (not INSERT) new child rows added to a tracked claim. Set ValueGeneratedNever for all entity ids.
- Bug found by running it: `SELECT NEXT VALUE FOR` failed (SQL error 11719) because `.SingleAsync()` made EF wrap the raw SQL in a subquery. Fixed with `ToListAsync()` + `Single()`, comment explains why.
- Overrode: per-market reserve config instead of one global default: amounts are in local currency (5,000 MYR vs 5,000 IDR). Config validated at startup so a missing market fails fast.
- Overrode: OutboxMessage lives in Repository/Outbox, not Domain/ReadModels as the spec said; it is plumbing, not domain.
- Verified against Docker SQL Server: 9 users, 13 policies, 13 claims across 6 markets and all statuses (3 overdue, 1 awaiting info); 27 outbox events; audit rows add up (26 status, 16 reserve); one envelope checked field by field against §6. Containerised API starts, logs JSON, skips seeding on an existing DB.
- Ops: C: drive hit 0 GB during package restore; cleared npm, NuGet and temp caches (with approval) to continue.

### 2026-11-21 · Phase 4 · Dev auth and claimant endpoints
- Asked: JWT dev login, role policies, claimant endpoints (policies/mine, FNOL, claims/mine, claim detail, answer information request) per BACKEND.md §8.
- Accepted: fallback policy "authenticated" so a new endpoint is never accidentally public; coarse role policies on controllers (ClaimantOnly/Staff/ManagerOnly) + resource checks in the service (owner / same market) + who-may-act in the aggregate; `MapInboundClaims = false` so `sub`/`role`/`market` arrive as issued; claimant detail hides reserve and notes (staff-only fields null).
- Challenged: someone else's policy and someone else's claim return 404 with the same message as a missing one, so ids cannot be probed. Covered by service tests and the smoke script.
- Challenged: the AI made the request enum `ClaimType?` to get `[Required]` working; that made the shared OpenAPI `ClaimType` schema nullable, so every generated frontend type would have allowed null. Switched to non-nullable enums with `[EnumDataType]` (missing value -> 400 naming the field). Verified in /openapi/v1.json.
- Accepted with a note: a claim-number sequence value is consumed even if FNOL validation then fails, so numbers can have gaps. Normal for sequences; uniqueness matters, not continuity.
- Added (not in spec): `GET /api/auth/dev-users` (Development only) so the Angular login page can list seeded users without hard-coding them. Spec updated.
- Verified end to end against SQL Server: `scripts/smoke/claimant_flow.py` runs 22 checks (401 without token; FNOL 201 + Location; 422 expired policy / future date / missing vehicle details; 400 missing fields; 404 other claimant's claim and policy; 204 answer then 409 on a second answer; 403 officer on claimant endpoints; 404 for an SG officer on an MY claim). 22/22. Correlation id from the request header reached the outbox row and every log line.
- Tests: 166 passing (service tests with NSubstitute fakes; JWT issue -> validate -> CurrentUser round trip; wrong signing key rejected).

### 2026-11-21 · Phase 5 · Officer work queue and claim actions
- Asked: work queue (unassigned / mine, status filter, SLA or reported sort) and officer actions (pick up, reserve, request info, approve, settle, reject, notes) with 409 on concurrent pick-up.
- Accepted: work queue as a SQL projection on indexed columns (no read model: per-officer filtered read); one `ActAsync` shape for every officer action (load visible claim -> domain method -> save) so no action can skip the visibility check; all actions 204 and the UI reloads.
- Challenged: "overdue" was first defined as `SlaDueAt < now`. That would flag approved claims waiting for payment as overdue although the decision was made in time. Changed to "SLA passed without a decision" and put it, with "waiting on", in `Domain/Rules/ClaimProgress` (business rules, unit tested).
- Challenged: request amounts as `decimal` would turn a missing amount into 0, and a 0 reserve is valid. Made them `decimal?` + `[Required]` (missing -> 400), while negative/zero limits stay domain rules (422).
- Proved concurrency, not just claimed it: `scripts/smoke/officer_flow.py` fires two pick-ups on the same new claim at the same instant (thread barrier), 10 times. Every time exactly one 204 and one 409, and all 10 conflicts were caught by the RowVersion check in SQL Server (both had loaded the claim before either saved), not by the status check.
- Verified: officer smoke 31/31 (queue scoping per market, SLA order with overdue first, waiting-on, 403/404/409/422/400 cases, full lifecycle with history and notes, claimant sees decision but not notes); claimant smoke still 22/22; 181 unit tests.
- More time: paging for the queue, SLA pause while waiting on the claimant, claimant name in queue rows (needs a users join or a denormalised column).

### 2026-11-21 · Phase 6 · Kafka: outbox publisher and idempotent exposure projection
- Asked: outbox publisher to claims.events.v1, exposure projector consumer, exposure endpoint (BACKEND.md §6, §7, §12 phase 6).
- Accepted: Acks=All + idempotent producer, key = claimId; commit offsets only after the side effect; `ProcessedEvents (group, eventId)` written in the same transaction as the read-model change; consumers parse their own `EventEnvelope` contract instead of referencing domain classes.
- Challenged: the first projector design was read-modify-write of the `ExposureByMarket` row through EF tracking. With two projector instances that loses updates. Changed to an atomic SQL increment (`ExecuteUpdate`, `x = x + delta`) inside the idempotency transaction, wrapped in the retrying execution strategy.
- Challenged: publishing order. `OccurredAt` ties and SQL Server's uniqueidentifier sort order don't give a reliable sequence, so I added an identity `Sequence` column to the outbox and the publisher stops at the first failed message (publishing later ones would reorder a claim's events).
- Challenged: what a consumer does on failure. Not-an-envelope = skip with an error log (retrying can't help); handler failure = retry with backoff and block the partition (skipping would silently corrupt exposure). Dead-letter topic left as P2.
- Overrode: `ExposureProjection` lives in Messaging, not Service as the spec said: Messaging may only reference Repository and Domain.
- Verified on the real stack (fresh DB): 27 seed events published (partitions 4/16/7), consumer lag 0; read model equals the truth recomputed from the Claims table for all 8 market/currency/type rows; live FNOL visible in exposure after ~1 s.
- Verified idempotency by replay: reset the consumer group to offset 0 and restarted: all 19 money events were redelivered, all 19 skipped as duplicates, read model byte-for-byte identical. (Learned: offsets can only be reset when the group is Empty; a killed process keeps it Stable until the 45 s session timeout.)
- Verified the outbox under failure: stopped Kafka, 3 FNOLs still returned 201, outbox held 3 unpublished events; Kafka back -> drained in ~9 s, exposure matched the Claims table again.
- Verified in Docker (internal listener kafka:29092): the request's correlation id appears in the API log, the service log and the consumer log (travels in the Kafka header).
- Tests: 190 passing, incl. a producer/consumer contract test (domain events -> outbox envelope -> consumer parse -> deltas; open claim sums to its outstanding reserve, closed to zero; unknown extra fields ignored) and publisher ordering/failure tests.

