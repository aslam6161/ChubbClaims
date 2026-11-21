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

