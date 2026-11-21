# AI Working Journal

Running log of how I directed the AI (Claude Code): what I asked, what I accepted, what I challenged, what I overrode. Appended during the work, one entry per prompt that mattered. Not polished on purpose.

---

**If you only read a few entries**, these are the moments where the AI's first answer was wrong or weaker and I changed it:

| Phase | What changed |
|---|---|
| 3 | EF Core 11 was "latest" but targets .NET 11; pinned 10.0.x. High-severity advisory in a transitive package, pinned the fix. Domain-generated ids would have made EF UPDATE new child rows. |
| 4 | A nullable request enum leaked into the OpenAPI contract (every generated TS enum nullable). Someone else's policy/claim returns 404, not 403. |
| 5 | "Overdue" flagged claims already decided in time. A missing amount silently became 0. Concurrency proven with real races, not asserted. |
| 6 | Read-modify-write in the projector loses updates with two instances: switched to atomic SQL increments. Publish order needed an identity column. |
| 7 | Consumers reading the write model vs an additive contract change. Status stamped at save time would be wrong for multi-change saves. A new consumer group would flood users with notifications about history. |
| 8 | The docs claimed a priority rule the code doesn't implement; checked every claim against the code and a fresh clone. |

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

### 2026-11-21 · Phase 7 · SignalR push, in-app notifications, team workload
- Asked: SignalR hub, realtime-notifier consumer with stored notifications, notifications API, team workload endpoint (BACKEND.md §8 realtime/team, §12 phase 7).
- Accepted: hub is server-to-client only, groups decided from the JWT on connect (user / staff:market / managers); Messaging defines an `IRealtimePush` port and the API implements it with `IHubContext`, so Messaging has no ASP.NET Core dependency; push after the notification commit and only for new events; a failed push is logged, never retried (REST is the source of truth).
- Challenged: the envelope only named the claimant on `ClaimSubmitted`, and `InformationProvided` didn't say who asked. Rather than have the notifier read the Claims table (coupling a consumer to the write model), added `claimantId` to the envelope and `assignedOfficerId` to `InformationProvided`, both additive so the topic stays v1; consumers treat them as optional.
- Challenged: putting the claim's current status on the envelope. The outbox is written at SaveChanges, so a claim changed several times in one save (the seeder does this) would stamp the final status on every event. Derived "status after" from the event type instead (each event is exactly one transition), with a test that walks a lifecycle.
- Challenged: a new consumer group reads from the start of the topic, so the notifier would have created notifications for all history (and the topic outlives dev DB resets). Added a 24 h max age. Verified: 54 stale events skipped, only the 4 expected notifications stored.
- Workload numbers checked by hand on seed data: Hafiz avg 6.0 days = (7 + 5) / 2 (the seeded approved and settled claims).
- Verified with the real SignalR JS client (`scripts/smoke/realtime_flow.mjs`, the client Angular will use): 23/23, incl. unauthenticated hub refused, claimant push ~0.7 s after FNOL, SG claimant and SG officer receive nothing about an MY claim, officers get no exposure pushes, claimant never notified of reserve changes, 404 on someone else's notification.
- Regression on the Docker container with a fresh DB: officer 31/31, claimant 22/22, realtime 23/23. Exposure smoke failed once: its baseline was read while the previous script's FNOL was still in flight (eventual consistency in the test, not a product bug: projection matched the Claims table, lag 0). Fixed the script to wait for a settled baseline; added scripts/smoke/README.md with the run order.
- Tests: 203 passing (notification rules through the wire format, status-after mapping, workload calculator, notification ownership, hub groups).
- More time: SignalR backplane for >1 instance; notification preferences/email; paging notifications.

### 2026-11-21 · Phase 8 · README, architecture, decisions, fresh-clone check
- Asked: final README (run steps, tour, what's built and cut), docs/architecture.md (diagrams), docs/decisions.md, journal tidy (BACKEND.md §12 phase 8).
- Found while preparing: the P1 `/health` endpoint had never been built. Added it (database only, no new package) as its own commit rather than listing it as cut. Kafka is deliberately not part of readiness: the API works while Kafka is down.
- Challenged: the AI's architecture draft said high priority is set "when the reserve is at or above the threshold". The code sets it once, at FNOL, from the estimate, strictly above. Fixed the doc and listed recalculation as a follow-up instead. Every other rule in the docs was checked against the code (transition table, who may add notes, reserve states).
- Diagrams in Mermaid (GitHub renders them, they diff in review, no image files to go stale); the state diagram is drawn from the transition table, not from the spec.
- Verified the README the way a reviewer would use it: cloned the committed branch into a temp folder, `cp .env.example .env`, `docker compose up --build` under a separate project name (own empty database): healthy after 52 s (warm build cache), seeded, 27 events published; every URL in the README returns 200; all four smoke suites 80/80 on that fresh stack; `dotnet test` on the clean checkout 203/203. Then removed only the test project's containers, volume and image and restarted my stack.
- Time, from git: first code commit 10:15, backend complete 11:55 (planning before that).

### 2026-11-21 · Web phase 1 · Angular 22 scaffold
- Asked: `ng new` with v22 defaults, Material, dev proxy, app shell, API types generated from OpenAPI (FRONTEND.md §10 phase 1).
- Checked the generated defaults before writing code (the spec asked for this): v22 writes `login.ts` / `class Login` and services with `@Service()`, not `.component.ts` / `@Injectable`. That conflicts with FRONTEND.md §4 (my IMSAnuglar naming). **I chose the Angular 22 defaults** so generated and hand-written files match; spec updated. Change detection isn't stated in the generated code, so OnPush is set as the generator default and written explicitly on every component.
- Challenged: `openapi-typescript` (peer TypeScript 5) wouldn't install next to Angular 22's TypeScript 6. Rejected `--legacy-peer-deps` (it hides every peer conflict, not just this one); the `api:types` script runs a pinned version through `npx` in isolation.
- Wrong turn, corrected: the type generator failed with ECONNREFUSED on `::1` and the AI's first diagnosis was IPv6. Real cause: Docker Desktop wasn't running. Kept `127.0.0.1` in the proxy and script anyway (harmless, avoids the real Node `localhost` -> `::1` pitfall).
- **Found two backend contract bugs by generating the client** (exactly what the generated-types workflow is for):
  1. `GET /api/claims/{id}` has no 200 response in OpenAPI: once an action has any `[ProducesResponseType]`, ASP.NET stops inferring the success type from `Task<T>`, and that action only declared the 404. Every other GET checked by script: only this one.
  2. Every number is typed `number | string` (e.g. `openClaims: number | string`), because the default web JSON options accept numbers in quotes (`AllowReadingFromString`) and the OpenAPI generator documents that.
  Both are backend fixes; per FRONTEND.md §11 I stopped and asked rather than working around them in the UI.
- Verified: build clean (67 kB initial transfer), Vitest 1/1, `ng serve` on :4200, proxy reaches the API (`/health`, `/api/auth/dev-users` -> 9 users).

### 2026-11-21 · Fix · OpenAPI contract bugs found by the generated client
- Asked: fix the two backend contract bugs found in web phase 1, on their own branch, with a guard test.
- `GET /api/claims/{id}`: added `[ProducesResponseType<ClaimDetailResponse>(200)]`.
- Numbers: one `ApiJson.Configure` now sets the JSON format for controllers, the OpenAPI generator and SignalR (enums as strings + `NumberHandling.Strict`). Before, the three were configured separately, which is how they could drift. A quoted number is now a 400 that names the field (`$.estimatedLossAmount`), checked live.
- Guard tests: a reflection test fails if any action with a typed body declares response types but no 2xx. **Proved it works by removing the fix: it failed naming exactly `ClaimsController.GetById`**, then restored. Plus tests that controllers and the OpenAPI generator share the strict format, and that `"5000"` is rejected while `5000` is accepted.
- Verified: 206 unit tests; regenerated TS types have 0 `number | string` and a typed claim detail; all four smoke suites 80/80 on a fresh seed (stricter numbers broke no real client); frontend builds.
- Lesson for the walkthrough: generating the client from the contract found two bugs that 200+ backend tests and 80 HTTP checks did not, because both were about what the API *documents*, not what it *does*.

### 2026-11-21 · Web phase 2 · Dev login, interceptors, role-based routing
- Asked: AuthStore, dev login page, auth + ProblemDetails interceptors, `canMatch` role guards, role redirects, tests (FRONTEND.md §10 phase 2).
- Accepted: session as signals in `sessionStorage` (refresh keeps you signed in, a new tab doesn't); token attached only to `/api/` and `/hubs/` URLs, never a third-party URL; every HTTP failure becomes one `AppError` (status, plain message, camelCase field errors, correlation id); a shared `PageState` component for loading / error / empty.
- **Challenged: interceptor order.** The AI first registered auth before problem, with a comment claiming auth would still see the raw 401. Wrong: responses pass back through interceptors in reverse, so problem must be outermost. Fixed, and **proved the test guards it: swapping the order makes the 401 test fail**.
- Changed the spec: one `area-layout` (links from route data) instead of `claimant-layout` + `staff-layout`, which would have been identical apart from their links.
- **Challenged my own browser check.** The first version checked that a claimant never downloads "staff-routes" by chunk name, but dev chunks are hashed, so that check could never fail. Rewrote it to match the component source paths the dev server reports, with positive controls (the officer *must* load staff code).
- Verified in headless Chromium (Playwright, scratchpad only; Playwright e2e in the repo stays P2): 15/15. A protected URL redirects to login; 9 seeded users listed; each role lands on its home; a claimant typing /staff/queue is sent home and **never downloads staff code** (and vice versa); refresh keeps the session; sign-out; forged token -> 401.
- Tests: Vitest 14/14 (guard: allowed / other role / signed out / expired / login page; interceptors: bearer only on API URLs, 401 sign-out, 409 and 400 ProblemDetails mapping, network failure; store: persistence across refresh, expiry, sign-out).
- Area pages are placeholders, replaced in phases 3-5.

### 2026-11-21 · Web phase 3 · Claimant: my claims, claim detail, report an incident
- Asked: My claims, Claim detail (timeline, decision, answer an information request), FNOL stepper, tests (FRONTEND.md §10 phase 3).
- Accepted: "what happens next" and status labels as pure functions in `_helpers/claim-status.ts`; FNOL form logic (structure, type switching, validators, request mapping, server-error mapping) in a pure `fnol-form.ts` so the rules are tested without rendering; commands await the server and then reload (no optimistic updates).
- **Bug found by a unit test: `resource.value()` throws in Angular 22 while the resource is in an error state.** Every screen read `value()` in its template, so a failed load would have crashed the page instead of showing PageState's error and retry. Added `valueOr(resource, fallback)` and moved every read behind a `computed`.
- **Bug found only in the browser: the answer form did a native submit and reloaded the page.** `(ngSubmit)` only exists with `[formGroup]`/NgForm; a lone `[formControl]` inside `<form>` falls back to the browser's submit. The database confirmed answers were being lost. Wrapped it in a FormGroup.
- **Bug found only in the browser: the FNOL page crashed on load** (`this.selectedPolicy is not a function`). Class fields initialise top to bottom and the form runs its validators when built, so the date validator called a field that didn't exist yet. Moved the policy signal above the form (also removed a circular toSignal).
- **Domain misunderstanding corrected:** I first disabled "expired" policies. In the API `isActive` means *not cancelled*, and an expired policy is still claimable for incidents inside its cover period (server rule `Covers(date)`). Disabling it would block legitimate late claims. Now only cancelled policies are disabled; ended cover is labelled; the date validator enforces the period.
- My browser script had its own bugs (an instant check before render, counting hidden "Next" buttons in a vertical stepper, reading the review step mid-animation, a wait loop matching an old log line). Fixed each before trusting a result.
- Verified in Chromium against the real API on a fresh seed: 19/19 (list with next steps and settlement in currency; answer flow incl. client-side empty check and the server's new status; expired policy labelled and recent date rejected; future date rejected; motor vs property steps; review; submit creates CLM-MY-2026-000014 and opens it; failed load shows error + retry; no console errors). Vitest 29/29.

### 2026-11-21 · Web phase 4 · Officer work queue and claim workbench
- Asked: work queue (Unassigned / Mine, SLA order, overdue, waiting on, outstanding + currency, Pick up with 409 handling) and the claim workbench (reserve, request info, approve, settle, reject, notes, history), plus tests (FRONTEND.md §10 phase 4).
- Accepted: `allowedActions(status, role, isAssignedToMe)` as a pure function that mirrors the backend's transition table and who-may-act rules; one `run()` path for every workbench command (validate → call → reload; 409 explains and reloads; other errors stay on screen); confirmation dialogs for approve, settle and reject (reject in the error colour); approve pre-fills the settlement with the current reserve.
- Tested the action table exhaustively: every status × officer/manager × assigned/not, plus claimants get nothing (26 cases). If it ever drifts from the backend, the UI either offers a button the API rejects or hides a valid one.
- **Bug found only in the browser screenshot:** after adding a note, the empty "Add a note" field turned red. `FormGroup.reset()` keeps the form *submitted*, and Material shows errors on submitted forms. The other action forms close after success so they never showed it; the note form stays open and now resets through `FormGroupDirective.resetForm()`. Added a browser check for it.
- My test bugs (fixed before trusting results): `whenStable()` waited forever on the queue reload I hadn't flushed yet; button selectors included the icon ligature although `mat-icon` is aria-hidden; a text selector matched both the header and the actions hint.
- Verified in Chromium against the real API on a fresh seed: 20/20, incl. the **stale-queue race in the UI**: the manager picks up a claim through the API while the officer's queue still shows it; the officer's Pick up gets "already picked up by another officer" and the row disappears on reload. Also: overdue first and highlighted, reserve change audited, question moves the claim to Awaiting information and hides approve/reject, approve → settle → outstanding 0, cancel on the confirm changes nothing, reject with reason, manager on another officer's claim sees notes only. Vitest 56/56.

### 2026-11-21 · Web phase 5 · Realtime updates and team dashboard
- Asked: SignalR connection, screens refresh on `ClaimUpdated`, team dashboard with workload for staff and exposure for managers (FRONTEND.md §10 phase 5).
- Accepted: push is a hint, REST is the truth: a push only triggers `resource.reload()`; claim screens filter to their own claim id, lists reload on any update the server chose to send. Connection lives with the area layout; toolbar shows Live / Connecting / Live updates paused; while paused, screens poll every 30 s. Exposure shows "updated X ago" per row and per currency total, never a cross-currency sum. Officers never request exposure (the API would 403 anyway).
- Kept SignalR message types hand-written and labelled: the hub isn't in OpenAPI. A contract gap worth naming (AsyncAPI for the hub and Kafka is a "more time" item).
- **Caught by comparing a browser screenshot with a unit test:** the money pipe printed `IDR 20,000,000.00` in Chromium but `IDR 20,000,000` in Node. `Intl` takes minor units from the runtime's ICU data, so output depended on where it ran. Now always two decimals, matching the API's `decimal(18,2)`, tested for IDR.
- **Challenged my own check:** "officer Team page: no exposure request" passed only because the officer never opened the Team page in that script. Replaced it with a real browser check (officer opens Team: only `/api/team/workload` is called, no exposure shown).
- Verified with several browsers at once against the real stack (API -> outbox -> Kafka -> SignalR): 13/13 + the officer Team check. The claimant's open claim page switched to Approved ~1.1 s after the officer clicked Approve in another browser, via a push-triggered re-fetch, no reload; a new FNOL appeared in the officer's queue (~0.3 s) and moved the manager's MYR exposure 86,300.00 -> 87,534.50 with "updated just now" (~0.3 s, numbers checked by hand against the seed); unassigned count updated; with the hub blocked the toolbar said "Live updates paused" and the screen still worked over REST. Vitest 62/62 (reload filtering, polling only while paused and stopping once live, cleanup on destroy, time-ago, officer vs manager dashboard).

### 2026-11-21 · Web phase 6 · README, screenshots, polish
- Asked: README section, screenshots, polish; done when a fresh clone runs with `npm ci && npm start` (FRONTEND.md §10 phase 6).
- Fresh-clone check: cloned `main` into a clean folder, `npm ci` (36 s), then build, tests (62/62 at the time) and `npm start`. The app served, deep links worked, and the proxy reached the API (dev users, `/health`). npm 11 warned that it skipped install scripts (esbuild, lmdb); everything still worked, so the README says so instead of telling people to approve scripts.
- **Polish found by measuring, not by looking:** FRONTEND.md promises "responsive basics", so I added a check that each screen at 390 px has no sideways page scroll. 4 of 7 screens failed: tables up to 1094 px wide, a staff toolbar that didn't fit, and claim numbers breaking into four lines with overlapping heading lines. Fixed with a scroll box per table, one-line claim numbers, a toolbar that shows only icons on phones (labels visually hidden, still read by screen readers) and a safer heading line height.
- **A cause I wouldn't have guessed:** the work queue still overflowed (550 px) inside its scroll box. Its visually-hidden text is absolutely positioned, and an absolutely positioned element escapes an `overflow` box unless the box itself is positioned. `position: relative` on `.table-scroll` fixed it, and the check went 6/7 → 7/7 with only that change.
- **Overrode the obvious phone layout** for My claims: rather than let the "What happens next" column scroll off-screen, below 480 px the status badge column is hidden instead. The next-step text already says the status in words, and it's what a claimant needs.
- **A flaky check led to a real bug.** Re-running the earlier browser checks showed one SignalR "failed to negotiate" console error. A small repro confirmed the cause: a full page reload during negotiation makes the browser cancel the request, which is harmless, and the next page goes live. But reading `connect()` showed a real race: after a quick sign-out and sign-in, the *old* connection's late failure or close could set the state to "paused" while the new one was live, so the toolbar would be wrong and screens would poll for nothing. Callbacks from a superseded connection are now ignored. Added a test seam (`createConnection()`) instead of `vi.mock`, which is unreliable under Angular's bundling test builder. The new test fails without the guard.
- Generated `schema.d.ts` added to `.prettierignore`, so a full format check is clean and regenerating the types never fights the formatter.
- Verified: Vitest 64/64; production build 94.42 kB initial transfer; browser checks again, on fresh seeds: login 15/15, claimant 19/19, officer 20/20, realtime 13/13, officer Team 2/2, phone width 7/7. Screenshots taken on a fresh seed. Backend tests 206/206 (the README said 203; corrected).
- Not done, and said so in the README: a committed Playwright suite, an nginx container for the web app, the notification bell. I didn't put a frontend duration in the README: commit times show when phases were reviewed and merged, not when work started.

### 2026-11-21 · Web theme · Brand colour and logo
- Asked (after seeing it run): the UI looked plain; apply an orange brand colour and add the Chubb logo, with a triangle instead of a bar as the accent.
- Accepted: orange Material 3 theme with overrides. Filled buttons use a deeper burnt orange (#c2410c, white text 5.2:1, WCAG AA); the bright orange (#f97316) is only for accents, since white text on it would be 2.8:1. Dark toolbar with an orange edge; the logo is kept beside the product name, and the sign-in page still says it's a development sign-in, so it reads as a demo app.
- **Caught a side effect:** the "Under review" badge used the primary container colour, so it would have turned orange and looked like "Awaiting information". It's now an explicit blue.
- **Two styling bugs found from screenshots, not by guessing:** (1) toolbar nav labels went dark-on-dark because Material resets button colours inside a toolbar from the toolbar's own token, so I set `--mat-toolbar-container-text-color`; (2) the brand name turned orange because the new global link colour outranked the toolbar's rule, so the global rule is now wrapped in `:where()` (zero specificity).
- Logo: black and white 2x PNGs (about 3.5 KB each) made from the supplied image's transparency, not a CSS invert. Decorative in the toolbar (the link is labelled), alt text "Chubb" on sign-in. The phone-width check caught the staff toolbar 2 px too wide with the logo, so it's smaller below 640 px; 7/7 again.
- Verified: Vitest 64/64, production build 94.54 kB initial transfer, Prettier clean; README screenshots retaken on a fresh seed.

