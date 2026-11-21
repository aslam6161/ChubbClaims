# BACKEND.md — ChubbClaims API (Chubb APAC take-home)

> **For Claude Code:** this is the build spec for the backend. Work **one phase at a time** (see §12).
> After each phase: build, run tests, stop, and wait for review before the next phase.
> Do not add packages, projects, or features that are not listed here without asking first.

---

## 1. Goal and scope

Build the backend for a claims platform covering **motor and property claims across six APAC markets**.

- **Claimants:** report an incident (FNOL, first notice of loss), track their claim, answer requests for information, and receive the decision.
- **Claims staff (officers):** pick up incoming claims, assess them (set reserve, request info), and move them to settlement or rejection.
- **Managers:** team workload and performance, plus a near-real-time view of **outstanding liability exposure**.

This is a **2–3 hour sprint (5 h hard cap)**. Build a small, correct, defensible slice, not a big shallow one.

### Priorities (MoSCoW)

| Priority | Item |
|---|---|
| **P0 Must** | Claim aggregate + lifecycle state machine with business rules, plus unit tests |
| **P0 Must** | SQL Server + EF Core 10 + migrations + seed data (users, policies, sample claims in all 6 markets) |
| **P0 Must** | REST endpoints for the claimant flow and the officer flow |
| **P0 Must** | Dev authentication (JWT) + role- and resource-based authorization |
| **P0 Must** | Transactional outbox → Kafka publisher (`claims.events.v1`) |
| **P0 Must** | Kafka consumer: exposure projection (read model), idempotent |
| **P0 Must** | Scalar API reference, Serilog structured logging, ProblemDetails errors |
| **P0 Must** | `docker compose up` starts SQL Server + Kafka + API; README run steps |
| **P1 Should** | Kafka consumer: realtime notifier → SignalR hub + in-app notifications |
| **P1 Should** | Team workload & performance endpoint (officers and managers; the brief lists it as a claims-staff need) |
| **P1 Should** | Health checks (`/health`) |
| **P2 Could** | Approval authority limit (above threshold, manager must approve) |
| **P2 Could** | Integration tests with WebApplicationFactory + Testcontainers |
| **P2 Could** | Dead-letter topic for poison messages |

### Explicitly out of scope (say so in the README)

Real identity provider, document/photo uploads, payments integration, fraud scoring, real policy-admin integration (stubbed), FX conversion, email/SMS delivery, multi-language content, per-market regulatory rules.

---

## 2. Tech stack (decided)

| Concern | Choice | Why / notes |
|---|---|---|
| Runtime | **.NET 10 (LTS)**, C# 14 | Current LTS. EF Core pinned to **10.0.x** (EF Core 11 targets .NET 11 only) |
| API style | **Controllers** (`[ApiController]`, one controller per feature, deriving from `BaseApiController`) in `ChubbClaims.Presentation`. Use **explicit kebab-case route attributes** so URLs match §8 exactly (e.g. `[Route("api/work-queue")]`, `[HttpPost("{id:guid}/pick-up")]`) | Matches my existing project structure (Bento_BE); attribute routing + `[Authorize(Policy=…)]` per action; built-in OpenAPI works with controllers |
| API docs | **Microsoft.AspNetCore.OpenApi** (`AddOpenApi`/`MapOpenApi`) + **Scalar.AspNetCore** (`MapScalarApiReference`) | Swashbuckle is no longer in the templates; built-in generator emits OpenAPI 3.1; Scalar reads `/openapi/v1.json` |
| Logging | **Serilog** (`Serilog.AspNetCore`), console sink, request logging, correlation id enrichment | Structured logs; correlation id flows into Kafka headers |
| Database | **SQL Server 2022** (Docker) + **EF Core 10** | Relational fits claims; RowVersion for concurrency |
| Messaging | **Kafka** (`apache/kafka` image, KRaft single node) + **Confluent.Kafka** client | Required by brief |
| Realtime to browser | **SignalR** (P1) | Browsers can't consume Kafka |
| Validation | DataAnnotations on request DTOs; `[ApiController]` automatic model validation returns 400 `ValidationProblemDetails` | No extra package |
| Mapping | Manual mapping (extension methods) | No AutoMapper (commercial license since 2025; also hides logic) |
| Mediator | **None**: plain application services | No MediatR (commercial license since 2025); fewer moving parts to defend |
| Tests | **xUnit** + **Shouldly** + **NSubstitute** | Not FluentAssertions (v8 is commercially licensed) |

---

## 3. Architecture

### 3.1 One deployable, modular (modular monolith) + Kafka

```
Angular ──REST──▶  ChubbClaims.Presentation ──EF Core──▶ SQL Server
   ▲                 │   ▲                    (Claims, Outbox, ReadModels)
   │ SignalR         │   │
   └─────────────────┘   │
                     OutboxPublisher (BackgroundService) ──▶ Kafka topic claims.events.v1 (key = claimId)
                                                                  │
                         ┌────────────────────────────────────────┤
                         ▼                                        ▼
              ExposureProjector (consumer group            RealtimeNotifier (consumer group
              "exposure-projector") → ExposureByMarket      "realtime-notifier") → SignalR + Notifications
```

**Decision: one deployable, not microservices.** Reasons to give in the walkthrough:
- One team, one bounded context (claims handling) and a 3-hour budget. Splitting now adds network hops, distributed transactions and deployment overhead for no benefit yet.
- The seams are already real: everything downstream reacts to **Kafka events**, not to direct calls. Exposure/reporting and notifications can be lifted into their own services later **without changing the Claims write side**.
- **When I'd split:** reporting has a different scaling or read profile (it does at scale), notifications need their own release cadence, or separate teams own them.

### 3.2 REST vs Kafka (the decision they will ask about)

| Use **REST** (synchronous) when… | Use **Kafka** (async events) when… |
|---|---|
| The user is waiting for an answer: submit FNOL (needs a claim number), view my claims, load the work queue, perform an action that can be **rejected** (invalid transition, 409 conflict) | Something **has happened** and other parts of the system must react, but the user should not wait for them: exposure projection, notifications, audit/analytics, future fraud/SLA services |

Rule of thumb: **commands and queries over REST; facts (domain events) over Kafka.**

### 3.3 Reliability rules (must be implemented, must be explainable)

1. **Transactional outbox:** domain events are written to an `OutboxMessages` table **in the same `SaveChanges` transaction** as the claim change. Never publish to Kafka inside the HTTP request (dual-write problem). If Kafka is down, claims are still accepted and events go out when it recovers.
2. **Ordering:** message key = `claimId`, so all events for one claim land on one partition, in order.
3. **At-least-once + idempotent consumers:** consumers commit offsets **after** processing (`EnableAutoCommit = false`). Each consumer records `(ConsumerGroup, EventId)` in `ProcessedEvents` in the same DB transaction as its side effect, and skips duplicates.
4. **Producer:** `Acks = All`, `EnableIdempotence = true`.
5. **Optimistic concurrency:** `Claim.RowVersion`. Two officers picking up the same claim means one gets **409 Conflict**.
6. **Correlation:** correlation id (from `X-Correlation-Id` or generated) goes into the log context, the outbox row, and the Kafka message headers, then back into the consumer log scope.

---

## 4. Solution layout

> **Naming convention (decided):** solution `ChubbClaims.slnx` (the .NET 10 SDK default for `dotnet new sln -n ChubbClaims`; needs VS 2022 17.13+ / Rider 2024.3+), projects `ChubbClaims.<Layer>`.
> Folder and file names are aligned with the structure of `D:\Chromatics\Bento_BE` (structure only, **no code copied**). Where Bento_BE conflicts with a rule in this spec, the spec wins; see §4.1.

```
ChubbClaims/                                   (repo root)
  docker-compose.yml
  .env.example
  README.md
  AI_JOURNAL.md
  docs/                                        architecture.md  decisions.md
  ChubbClaims_BE/
    BACKEND.md                                 (this file)
    ChubbClaims.slnx
    Dockerfile                                 publishes ChubbClaims.Presentation
    ChubbClaims.Presentation/                  HTTP host + composition root (Web API, controllers)
      Program.cs
      Controllers/                             BaseApiController, AuthController, PoliciesController, ClaimsController
                                               (claimant + officer actions), WorkQueueController, TeamController,
                                               ReportsController, NotificationsController (P1)
      Hubs/                                    ClaimsHub (SignalR, P1)
      Auth/                                    JwtSettings, AppClaimTypes, DevTokenIssuer, CurrentUser, AuthorizationPolicies
      Middleware/                              CorrelationIdMiddleware, ExceptionToProblemDetailsHandler
      Extensions/                              BearerSecuritySchemeTransformer (Scalar "Authorize"),
                                               ClaimsPrincipalExtensions, CorsExtension
      appsettings.json / appsettings.Development.json
    ChubbClaims.Service/                       use cases (application layer), no EF, no Kafka
      Interfaces/                              IClaimService, IWorkQueueService, ITeamService, IExposureService, ICurrentUser
      Services/                                ClaimService (FNOL + actions), WorkQueueService, TeamService, ExposureService
      DTOs/                                    Claims/, WorkQueue/, Team/, Reports/, Auth/ (request/response records)
      Exceptions/                              NotFoundException, ForbiddenException, ConflictException (application-level)
      Helper/                                  manual mapping extension methods (ClaimMappings, …; no AutoMapper)
      DependencyInjection.cs                   AddServices()
    ChubbClaims.Repository/                    persistence (EF Core 10 + SQL Server)
      DataContext.cs                           DataContext (EF Core DbContext, claim number sequence, conventions)
      Outbox/                                  OutboxMessage, OutboxMessageFactory (builds the §6 event envelope)
      Configurations/                          IEntityTypeConfiguration<T> per entity
      Data/                                    Seed.cs (DevDataSeeder, seeds through the domain model)
      Interfaces/                              IClaimRepository, IPolicyRepository, IUserRepository,
                                               IOutboxRepository, IExposureReadRepository, IProcessedEventRepository, IUnitOfWork
      Repositories/                            implementations of the above
      Interceptors/                            DomainEventsToOutboxInterceptor (same transaction as SaveChanges)
      Migrations/
      DependencyInjection.cs                   AddRepository(connectionString)
    ChubbClaims.Domain/                        pure C#, no package references
      Entity/                                  BaseEntity, AggregateRoot, Claim (aggregate root), ClaimChildren (InformationRequest,
                                               ClaimStatusChange, ReserveChange, ClaimNote), Policy, User
      ValueObjects/                            Actor, IncidentReport, MotorDetails, PropertyDetails, ClaimNumber
      Enums/                                   ClaimStatus, ClaimType, ClaimAction, UserRole, DamageType, Priority, Market
      Constants/                               Markets (currency + time zone per market)
      Events/                                  IDomainEvent + ClaimSubmitted, ClaimPickedUp, ReserveAdjusted, …
      Settings/                                ClaimRulesSettings (bound from ClaimRules; per-market reserves and thresholds)
      ReadModels/                              ExposureByMarket, Notification, ProcessedEvent (Phase 6/7)
      Exceptions/                              DomainException, InvalidClaimTransitionException (409), DomainValidationException (422),
                                               ClaimAccessDeniedException (403)
      Rules/                                   ClaimTransitions (allowed transition table), ClaimSubmissionRules
    ChubbClaims.Messaging/                     Kafka (Confluent.Kafka)
      Settings/                                KafkaSettings
      Contracts/                               EventEnvelope, event payload records (the Kafka contract, §6)
      Producers/                               KafkaEventProducer (Acks=All, idempotent; key = claimId; headers event-type, event-id, correlation-id)
      Publishers/                              OutboxPublisher (one pass, stops at first failure to keep order) + OutboxPublisherService (BackgroundService)
      Projections/                             ExposureProjection (pure delta rules, unit tested; here, not in Service, because
                                               Messaging may only reference Repository and Domain)
      Consumers/                               KafkaConsumerBase (commit-after-process, idempotency),
                                               ExposureProjectorConsumer, RealtimeNotifierConsumer (P1)
      Topics/                                  KafkaTopicInitializer (creates claims.events.v1 with 3 partitions; retries, never blocks API startup)
      DependencyInjection.cs                   AddMessaging(config)
    ChubbClaims.Tests/                         one test project, folders per area (Bento_BE style)
      Domain/                                  state machine + business rules
      Services/                                services with NSubstitute fakes, exposure projection idempotency
      Integration/                             (P2) WebApplicationFactory + Testcontainers
  ChubbClaims_FE/                              see FRONTEND.md
```

**Project references (dependency rule):**
```
ChubbClaims.Presentation ──▶ ChubbClaims.Service ──▶ ChubbClaims.Repository ──▶ ChubbClaims.Domain
       └──────────▶ ChubbClaims.Messaging ─▶ ChubbClaims.Repository, ChubbClaims.Domain
ChubbClaims.Tests ─▶ Domain, Service (and API for P2 integration tests)
```
- `Domain` references nothing. `Service` never references EF Core or Confluent.Kafka directly.
- Each layer exposes one `DependencyInjection.cs` extension (`AddServices()`, `AddRepository()`, `AddMessaging()`); `Program.cs` only calls these.
- Business rules live in the **Domain** (the `Claim` aggregate + `ClaimTransitions`), not in controllers, services or EF configuration. Services orchestrate: load → call domain method → save.

**Messaging is a separate project.** It is the seam where the exposure projector or notifier could be lifted into its own deployable later. Today it runs inside the API host as background services.

### 4.1 Alignment with Bento_BE (what was adopted, what was not)

| Area | Bento_BE pattern | Decision for ChubbClaims |
|---|---|---|
| Solution file | `Mustard.sln` | **Not adopted:** `ChubbClaims.slnx` (the .NET 10 SDK default; same projects, XML format, fewer merge conflicts) |
| Project names | `.Domain`, `.Repository`, `.Service`, `.Tests` | **Adopted** |
| Domain folders | `Entity/` (singular), `Enums/`, `Constants/`, `Settings/` | **Adopted**; spec-only folders added (`ValueObjects/`, `Events/`, `Rules/`, `Exceptions/`, `ReadModels/`) |
| DbContext | `DataContext.cs` at project root, `Data/Seed.cs` | **Adopted** (class `DataContext`, seeder `DevDataSeeder` in `Data/Seed.cs`) |
| Outbox entity | (none) | `OutboxMessage` in `Repository/Outbox`, not `Domain/ReadModels`: it is a persistence mechanism, not a business concept; the envelope is built where the row is written |
| Service folders | `DTOs/`, `Interfaces/`, `Services/`, `Exceptions/`, `Helper/` | **Adopted** (mappings live in `Helper/`) |
| API project | `Mustard.Presentation` with `Controllers/` + `BaseApiController` | **Adopted:** `ChubbClaims.Presentation` |
| API support folders | `Middleware/`, `Logging/`, `Extensions/` | **Adopted** `Middleware/` and `Extensions/` (CorrelationIdMiddleware, BearerSecuritySchemeTransformer). No `Logging/` enricher: the middleware pushes CorrelationId into Serilog's `LogContext` |
| Tests | One `.Tests` project with area folders | **Adopted** (replaces separate Domain.Tests / Service.Tests projects) |
| Mapping | `Helper/AutoMapperProfiles.cs` | **Not adopted**: AutoMapper is commercially licensed since 2025; manual mapping |
| Errors | `ExceptionMiddleware` + `Errors/ApiException` | **Not adopted**: spec requires built-in `AddProblemDetails()` + `IExceptionHandler` |
| DI registration | `Extensions/ApplicationServiceExtensions` in the API | **Not adopted**: one `DependencyInjection.cs` per layer keeps layer boundaries explicit |
| Exception placement | All exceptions in `Service/Exceptions` | **Split**: app exceptions (NotFound/Forbidden/Conflict) in Service; business-rule exceptions in Domain |

### 4.2 Decision: Presentation + Controllers

API host is **`ChubbClaims.Presentation`** with **Controllers** (`Controllers/` + `BaseApiController`), matching Bento_BE. Rationale to give in the walkthrough: consistent with the structure I use in production; controllers group a feature's actions with per-action `[Authorize]` policies and attribute routes. Minimal APIs were the alternative (less ceremony); either works with the built-in OpenAPI generator and Scalar.

---

## 5. Domain model

### 5.1 Entities

**Claim** (aggregate root)
- `Id` (Guid), `ClaimNumber` (e.g. `CLM-MY-2026-000123`, from a SQL sequence)
- `Market` (`MY`, `SG`, `HK`, `TH`, `ID`, `AU`; seed values are illustrative), `Currency` (ISO 4217, **derived from the policy**, never from user input)
- `Type` (`Motor` | `Property`), `PolicyNumber` (reference to an external policy system), `ClaimantId`
- `IncidentDate`, `ReportedAt`, `IncidentLocation`, `Description`, `EstimatedLossAmount?`
- `MotorDetails?` (VehicleRegistration, ThirdPartyInvolved, PoliceReportNumber?) / `PropertyDetails?` (PropertyAddress, DamageType: Fire/Water/Storm/Theft/Other). Use EF **owned types** (or a JSON column) and explain the choice.
- `Status`, `AssignedOfficerId?`, `Priority` (Normal/High; High if estimated loss is above a market threshold)
- `ReserveAmount` (decimal(18,2)), `SettlementAmount?`, `PaidAmount` (default 0)
- `SlaDueAt` (ReportedAt + configured days), `DecisionAt?`, `ClosedAt?`, `LastStatusChangeAt`
- `RowVersion`
- Children: `InformationRequests`, `StatusHistory`, `ReserveHistory`, `Notes`
- Computed: `OutstandingReserve = IsClosed ? 0 : ReserveAmount - PaidAmount`

**InformationRequest**: Id, ClaimId, RequestedById, Question, RequestedAt, Response?, RespondedAt?, `IsOpen`
**ClaimStatusChange** (audit): From, To, ActorId, ActorRole, At, Reason?
**ReserveChange**: OldAmount, NewAmount, ActorId, At, Reason
**ClaimNote**: AuthorId, Text, CreatedAt
**Policy** (stub for an external system, seeded): PolicyNumber, HolderUserId, Market, Currency, ProductType (Motor/Property), EffectiveFrom, EffectiveTo, IsActive. Accessed only through `IPolicyLookup`.
**User** (seeded, dev only): Id, Name, Email, Role (`Claimant` | `ClaimsOfficer` | `ClaimsManager`), Market (staff only)

**Money rule:** all amounts are `decimal` with an explicit currency. **Never `double`/`float`.**
No separate `Money` type: a claim has exactly one `Currency` (from its policy) and every amount on it is in that currency, so the aggregate enforces the single-currency rule; a `Money` wrapper would add mapping cost without preventing any bug here.

**Actor:** every aggregate method takes an `Actor(UserId, Role, Market)` so ownership (claimant owns claim), market scoping and assigned-officer rules are enforced in the domain, not only in endpoints. Check order in every method: transition (409) → actor (403) → input (422).

**Incident date vs "today":** compared with the **market's local date** (`Markets.LocalDate`), not UTC. All APAC markets are ahead of UTC, so a same-day claim in Sydney at 08:00 would otherwise be rejected as "in the future".

### 5.2 Lifecycle state machine

```
Submitted ──PickUp──▶ UnderReview ──RequestInformation──▶ AwaitingInformation
                          ▲   │  ◀──────ProvideInformation──────┘
                          │   ├──Approve──▶ Approved ──Settle──▶ Settled  (closed)
                          │   └──Reject───▶ Rejected                       (closed)
```

| From | Action | To | Who | Rules |
|---|---|---|---|---|
| Submitted | `PickUp` | UnderReview | Officer, same market | Not already assigned. Sets `AssignedOfficerId`. Concurrent pick-up returns 409 |
| UnderReview | `RequestInformation` | AwaitingInformation | Assigned officer | Question required; only one open request at a time |
| AwaitingInformation | `ProvideInformation` | UnderReview | Claimant who owns the claim | Must answer the open request |
| UnderReview / AwaitingInformation / Approved | `AdjustReserve` | (unchanged) | Assigned officer | Amount ≥ 0, reason required, history row written |
| UnderReview | `Approve` | Approved | Assigned officer (P2: manager if above authority limit) | SettlementAmount > 0; reserve set to SettlementAmount (writes a `ReserveChange` row; the event carries old/new reserve) |
| Approved | `Settle` | Settled | Assigned officer | PaidAmount = SettlementAmount; outstanding becomes 0; `ClosedAt` set |
| UnderReview | `Reject` | Rejected | Assigned officer | Reason required; outstanding becomes 0; `ClosedAt` set |

- Any other transition throws `InvalidClaimTransitionException` and maps to **409 Conflict** (ProblemDetails).
- Rule violations (e.g. negative reserve) throw `DomainValidationException` and map to **422**.
- Each transition writes a `ClaimStatusChange` row and raises a **domain event**.

**Initial ("formula") reserve:** at FNOL, `ReserveAmount = EstimatedLossAmount` if provided, else a configured default per market + claim type (`ClaimRules:Markets:<market>:DefaultMotorReserve / DefaultPropertyReserve`, in the market's currency). This makes exposure meaningful from the moment a claim is reported.

### 5.3 FNOL validation (business logic)

- The policy exists, **belongs to the caller**, is active, and covers `IncidentDate`.
- `IncidentDate` is not in the future and not older than a configured window (e.g. 365 days).
- `ClaimType` matches the policy `ProductType`.
- Motor needs `MotorDetails`; Property needs `PropertyDetails`.

### 5.4 Relationships

```
User (Claimant) 1 ──< Policy 1 ──< Claim >── 0..1 User (ClaimsOfficer, AssignedOfficerId)
Claim 1 ──< InformationRequest      (at most one open at a time)
Claim 1 ──< ClaimStatusChange       (append-only audit)
Claim 1 ──< ReserveChange           (append-only audit)
Claim 1 ──< ClaimNote
```
`Claim` is the only aggregate root; children are loaded and changed only through it. `Policy` and `User` are referenced by id (they belong to other systems in reality).

---

## 6. Events (Kafka contract)

Topic: **`claims.events.v1`**, 3 partitions, **key = claimId**. Headers: `event-type`, `correlation-id`.

Envelope (JSON):
```json
{
  "eventId": "guid",
  "eventType": "ClaimSubmitted",
  "eventVersion": 1,
  "occurredAt": "2026-11-21T10:15:00Z",
  "claimId": "guid",
  "claimNumber": "CLM-MY-2026-000123",
  "market": "MY",
  "currency": "MYR",
  "claimType": "Motor",
  "data": { }
}
```

| eventType | `data` |
|---|---|
| `ClaimSubmitted` | claimantId, initialReserve, slaDueAt |
| `ClaimPickedUp` | officerId |
| `ReserveAdjusted` | oldAmount, newAmount, reason |
| `InformationRequested` | requestId, question |
| `InformationProvided` | requestId |
| `ClaimApproved` | settlementAmount, oldReserve, newReserve |
| `ClaimSettled` | paidAmount, releasedReserve |
| `ClaimRejected` | reason, releasedReserve |

`releasedReserve` = the claim's **outstanding reserve immediately before** the closing transition (`ReserveAmount − PaidAmount`).

Every event carries what consumers need, so **consumers never call back into the API** (no temporal coupling).
Contract changes: additive fields only within v1; breaking change means a new `eventVersion`/topic. (More time: schema registry + Avro/Protobuf, AsyncAPI doc.)

---

## 7. Read models

**ExposureByMarket** (written only by `ExposureProjector`)
`Market, Currency, ClaimType, OpenClaims, OutstandingReserve, LastEventAt` (PK: Market + Currency + ClaimType)

Delta rules:
- `ClaimSubmitted`: OpenClaims +1, Outstanding + initialReserve
- `ReserveAdjusted`: Outstanding + (newAmount − oldAmount)
- `ClaimApproved`: Outstanding + (newReserve − oldReserve) (approval resets the reserve to the settlement amount; without this delta the projection drifts)
- `ClaimSettled` / `ClaimRejected`: OpenClaims −1, Outstanding − releasedReserve

Deltas are **only safe because the consumer is idempotent** (`ProcessedEvents`). Be ready to explain this.
**Exposure is reported per currency. There is no FX conversion** (deliberate; more time: reporting currency + dated FX rate snapshots).
More time: a rebuild job that replays the topic or recomputes from the Claims table to fix drift.

**Implementation notes (Phase 6):**
- Idempotency: `ExposureRepository.ApplyOnceAsync` runs in one transaction: skip if `(ConsumerGroup, EventId)` exists, else atomic `UPDATE ... SET x = x + delta` (no read-modify-write, safe with several projector instances), insert the row if missing, insert `ProcessedEvents`. The `ProcessedEvents` primary key is the final guard.
- Consumer failures: a message that is not a valid envelope is logged and skipped (it can never succeed); a handler failure (e.g. DB down) is retried with backoff and blocks that partition, so nothing is lost or applied out of order. Dead-letter topic is P2.
- Publisher: at-least-once. If marking a message as published fails after Kafka acknowledged it, it is sent again and consumers drop the duplicate. Several API instances would double-publish (still correct, just wasteful); row locking (`UPDLOCK, READPAST`) is a "more time" item.
- Kafka down: FNOL and officer actions keep working (201/204); events wait in the outbox and drain when Kafka returns (verified).

**Notifications** (written by `RealtimeNotifierConsumer`, group `realtime-notifier`): Id, UserId, ClaimId, ClaimNumber, Message (500), CreatedAt, ReadAt?; index (UserId, CreatedAt).
- Claimant is told: received (with decision target date), being reviewed, information needed (with the question), approved (amount), paid (amount), not approved (reason). **Never reserve changes** (internal).
- Assigned officer is told: the claimant answered.
- Stored exactly once (same `ProcessedEvents` guard as exposure), then a `ClaimUpdated` push. Events older than `Kafka:NotificationMaxAgeMinutes` (24 h) are skipped: no notification storm after a replay or for seeded history.
- Event contract additions (additive, still v1): envelope `claimantId`; `InformationProvided.data.assignedOfficerId`. Consumers treat both as optional (older messages lack them).

**Why a projection for exposure but plain indexed queries for the work queue?**
- The work queue is a filtered, per-officer read on a few indexed columns, so the write DB with indexes is fine.
- Exposure is a **portfolio-wide aggregate** that managers want continuously; re-aggregating every open claim on every dashboard refresh doesn't scale, so it is maintained incrementally from events.

**Read/write profile per concern** (drives the design):

| Concern | Writes | Reads | Design |
|---|---|---|---|
| Claim lifecycle | Low volume, must be consistent | By id | Aggregate + RowVersion; one transaction per command |
| Work queue | (none, reads write model) | Very frequent, per officer, filtered | Indexed query on `Claims` |
| Claimant tracking | (none) | Frequent, by claimant | Indexed query on `Claims(ClaimantId)` |
| Exposure | Every reserve-affecting event | Frequent, portfolio-wide aggregate | Event-fed projection `ExposureByMarket` |
| Audit / history | Append-only on every transition | Rare, per claim | Child tables, loaded only on claim detail |

Indexes: `Claims(Status, Market)`, `Claims(AssignedOfficerId, Status)`, `Claims(ClaimantId)`, `Claims(SlaDueAt)`, `OutboxMessages(ProcessedAt, Sequence)` (Sequence = identity column: strict publish order).

---

## 8. REST API

Base path `/api`. All errors are **ProblemDetails**. Auth: `Authorization: Bearer <jwt>`.

### Auth (dev only)
- `POST /api/auth/dev-login` `{ email }` returns `{ accessToken, user }`. Issues a JWT for a seeded user. **Development environment only; clearly labelled as a shortcut.**
- `GET /api/auth/dev-users`: the seeded users for the login picker. **Development only** (404 otherwise).
- `GET /api/me`

### Claimant (`Claimant` role)
- `GET  /api/policies/mine`: the caller's policies (to pick on FNOL)
- `POST /api/claims`: submit FNOL. Returns **201** + `{ id, claimNumber, status }`
- `GET  /api/claims/mine`
- `GET  /api/claims/{id}`: owner or staff in the claim's market. **404 (not 403) for anyone else**, including staff from another market. Staff additionally get `reserveAmount`, `outstandingReserve`, `priority`, `assignedOfficerId`, `reserveHistory`, `notes` (null for claimants)
- `POST /api/claims/{id}/information-requests/{requestId}/response` `{ response }`
- `GET  /api/notifications/mine` (P1)

### Officer (`ClaimsOfficer`, `ClaimsManager`)
- `GET  /api/work-queue?view=unassigned|mine&status=&sort=sla|reported`: scoped to the officer's market. Each row has claimNumber, type, status, reportedAt, ageDays, slaDueAt, isOverdue, waitingOn (`Officer` | `Claimant`), outstandingReserve, currency
  - `unassigned` = claims in my market with no assignee; `mine` = my claims, **open only** unless `status` is given (so closed claims can still be found). Max 200 rows (paging is a "more time" item)
  - `isOverdue` = SLA date passed **without a decision** (approved claims awaiting payment are not overdue); the SLA clock keeps running while waiting on the claimant (pausing it is a "more time" item)
  - `waitingOn` = `Officer` (Submitted, UnderReview, Approved), `Claimant` (AwaitingInformation), `Nobody` (closed). Rules live in `Domain/Rules/ClaimProgress`
  - Projected in SQL (no aggregate loading); outstanding reserve computed in the query with the same rule as `Claim.OutstandingReserve`
- All officer actions return **204**; the UI reloads the claim (the server can reject, so no optimistic updates)
- `POST /api/claims/{id}/pick-up`
- `PUT  /api/claims/{id}/reserve` `{ amount, reason }`
- `POST /api/claims/{id}/information-requests` `{ question }`
- `POST /api/claims/{id}/approve` `{ settlementAmount, note? }`
- `POST /api/claims/{id}/settle`
- `POST /api/claims/{id}/reject` `{ reason }`
- `POST /api/claims/{id}/notes` `{ text }`

Task-based endpoints (not `PATCH status`) because each action has its own rules, its own authorization, and its own event. Defend this.

### Team and reporting
- `GET /api/team/workload` (P1, `Staff`: officers and managers, scoped to their market): per officer in the market: open count by status, awaiting-claimant count, overdue count, oldest open age, decisions in the last 7 days, average days to decision (decisions in the last 30 days); plus the market's unassigned open count. Officers with overdue work first. Computed from a narrow query (assigned claims that are open or decided in 30 days); would become a projection at scale
- `GET /api/notifications` (any signed-in user: latest 50 + unread count), `POST /api/notifications/{id}/read` (404 if not yours), `POST /api/notifications/read-all`
- `GET /api/reports/exposure` (`ManagerOnly`): from `ExposureByMarket`, grouped by market/currency/type, with `lastEventAt` so the UI can show freshness

### Realtime (P1)
- SignalR hub `/hubs/claims` (JWT via `access_token` query string for WebSockets)
- Server-to-client messages: `ClaimUpdated { claimId, claimNumber, market, eventType, status, occurredAt }` (to the claimant who owns it and to staff in that market; `status` null when unchanged, e.g. reserve adjusted), `ExposureUpdated { market, currency, claimType, occurredAt }` (to managers)
- Groups are set by the server from the JWT on connect: `user:{id}` (everyone), `staff:{market}` (officers, managers), `managers`. No client-callable hub methods
- Single instance only: with several API instances SignalR needs a backplane (Redis / Azure SignalR), because the instance whose consumer handles an event can only reach its own connections
- Clients **re-fetch over REST** when notified. Push is a hint, REST is the source of truth.

### Authorization
- Every endpoint requires an authenticated user (fallback policy) unless marked `[AllowAnonymous]` (dev login, OpenAPI, Scalar).
- **400 vs 422:** request shape errors (missing field, unknown enum, too long) are 400 `ValidationProblemDetails` from `[ApiController]`; business-rule violations are 422 from the domain.
- Policies: `ClaimantOnly`, `Staff` (Officer or Manager), `ManagerOnly`.
- Resource checks in the application layer: claimant owns the claim; officer is in the claim's market; only the **assigned** officer can act on a claim.
- Angular route guards are UX only. **The API enforces everything.**

---

## 9. Cross-cutting

- **Serilog:** configured from `appsettings`; `UseSerilogRequestLogging()`; enrich with CorrelationId, UserId, ClaimId (when known). Pretty console locally, compact JSON in the container. Log every state transition at Information and every consumer skip/duplicate at Debug.
- **ProblemDetails:** `AddProblemDetails()` + exception handler mapping NotFound→404, InvalidTransition→409, Concurrency→409, DomainValidation→422, Forbidden→403.
- **Scalar:** `MapOpenApi()` + `MapScalarApiReference()` (Development). Add a document transformer for the Bearer security scheme so you can "Authorize" in Scalar. Title "Claims Platform API", version v1.
- **Time:** inject `IClock` (`TimeProvider` in .NET 10). No `DateTime.Now` in the domain. Store UTC.
- **Config:** `ClaimRules` (`SlaDaysToDecision`, `MaxIncidentAgeDays`, and per market `DefaultMotorReserve`, `DefaultPropertyReserve`, `HighPriorityThreshold`; validated on start so every market is configured), `Kafka:BootstrapServers`, `Kafka:Topic`, `Jwt:*`, connection string; overridable via env vars in compose.
- **CORS:** allow `http://localhost:4200` (Angular dev server).
- **Health (P1):** `/health` with DB check.

---

## 10. Seed data (Development)

- Users: 2 demo claimants used for login (`claimant.my@demo.local`, `claimant.sg@demo.local`), plus one background claimant per other market (HK, TH, ID, AU) who own the seeded claims there; 1 officer each in MY and SG (`officer.my@demo.local`, …), 1 manager in MY (`manager.my@demo.local`).
- Policies: Motor + Property for every claimant in their own market (a claim's market and currency always come from its policy), plus one expired policy for `claimant.my` (to demo FNOL validation).
- Only MY and SG have staff, so HK/TH/ID/AU claims stay in the unassigned queue of no one; they exist so exposure covers all six markets. Say this in the README.
- Claims: about 12 spread across all six markets and all statuses (some overdue, one awaiting information), so the queue and exposure screens look real immediately.
- Seed **through the domain model** so seeded claims also produce outbox events and the exposure read model is populated by the real pipeline.

---

## 11. Tests (xUnit + Shouldly + NSubstitute)

**Domain (P0):** these are the ones to show in the walkthrough.
- Every allowed transition succeeds and raises the right event.
- Every disallowed transition throws (table-driven `[Theory]` over status × action).
- Only the assigned officer can act; a claimant can only provide information on their own claim.
- Reserve cannot go negative; history row written; settle/reject set outstanding to 0.
- One open information request at a time.
- FNOL validation: expired policy, policy of another user, future incident date, type mismatch.

**Service (P0):**
- Exposure projector applies deltas correctly; **the same event delivered twice is applied once**.
- Saving a claim change writes an outbox row in the same unit of work.

**Integration (P2):** WebApplicationFactory + Testcontainers (SQL Server): FNOL → pick up → approve → settle happy path; concurrent pick-up returns 409; claimant reading another claimant's claim returns 404.

Tests must assert behaviour. No tests that only check "not null".

---

## 12. Build phases (commit after each; one journal entry per phase minimum)

**Git workflow:** one branch per phase off `main`, named `feature/<phase>` (`feature/scaffold`, `feature/domain`, `feature/persistence`, `feature/claimant-api`, `feature/officer-api`, `feature/messaging`, `feature/realtime`, `feature/docs`). Several small commits per branch (not one per phase). Merge into `main` with `--no-ff` (never squash, so the step-by-step history survives) only after build + tests pass and the phase is reviewed. `main` always builds.

| # | Phase | Done when | Suggested commit |
|---|---|---|---|
| 1 | `git init` + `.gitignore`, `ChubbClaims.slnx` + projects (§4), references, Serilog, OpenAPI + Scalar, ProblemDetails, compose with SQL Server + Kafka | `docker compose up` brings infra up; API starts; `/scalar` loads | `chore: scaffold solution, logging, OpenAPI/Scalar, docker compose` |
| 2 | Domain: Claim aggregate, state machine, events, value objects + **domain tests** (`ChubbClaims.Tests/Domain`) | All domain tests green | `feat(domain): claim lifecycle state machine with business rules` |
| 3 | ChubbClaims.Repository: DbContext, configurations, migration, seed, repositories, outbox interceptor | DB created and seeded on startup | `feat(infra): persistence, seed data and transactional outbox` |
| 4 | Dev auth + claimant endpoints (FNOL, mine, detail, respond) | Claimant flow works in Scalar | `feat(api): claimant FNOL and tracking endpoints` |
| 5 | Officer endpoints (queue, pick-up, reserve, request info, approve, settle, reject) + concurrency | Officer flow works; double pick-up returns 409 | `feat(api): officer work queue and claim actions` |
| 6 | ChubbClaims.Messaging: outbox publisher, exposure projector consumer, topic init + exposure endpoint + projector tests | Events visible in Kafka; exposure updates | `feat(events): kafka publisher and idempotent exposure projection` |
| 7 (P1) | SignalR hub + realtime notifier + notifications + team workload | UI-ready push; workload endpoint | `feat(realtime): signalr notifications and team workload` |
| 8 | README (run steps, decisions, what I cut), architecture diagram, journal tidy | A fresh clone runs from the README | `docs: architecture, decisions and run instructions` |

**Time budget for the backend: about 2–2.5 h.** If behind at the end of phase 6, skip phase 7 and say so in the README.

---

## 13. docker-compose (backend part)

Services: `sqlserver` (mcr.microsoft.com/mssql/server:2022-latest, with a healthcheck), `kafka` (`apache/kafka`, KRaft single node), `kafka-ui` (optional, kafbat/kafka-ui on :8085, handy for showing events in the demo), `api` (built from `ChubbClaims_BE/Dockerfile` (publishes ChubbClaims.Presentation), :5080).

Gotchas to handle:
- Kafka needs **two listeners**: an internal one for containers (`kafka:29092`) and an external one for the host (`localhost:9092`) so `dotnet run` from Visual Studio also works.
- Create the topic on startup (an admin client in the API, or an init container) with 3 partitions.
- `api` waits for SQL Server and Kafka to be healthy; the API also retries the DB connection on startup.
- SQL Server SA password must meet complexity rules; keep it in `.env` (commit `.env.example`, not `.env`).
- Windows 11 + Docker Desktop (WSL2), 16 GB RAM: fine, but cap Kafka heap (`KAFKA_HEAP_OPTS=-Xmx512m -Xms512m`).

Ports: API 5080, Scalar `http://localhost:5080/scalar`, SQL 1433, Kafka 9092, Kafka UI 8085.

---

## 14. Rules for the AI (Claude Code) and things the human must check

Claude Code must:
- Follow this spec; ask before adding packages, projects or patterns not listed.
- Keep business rules in `Domain`; keep controllers thin (bind → call service → map result). No logic in `BaseApiController` beyond shared helpers (e.g. `CurrentUserId`).
- Never publish to Kafka from a request handler; always go through the outbox.
- Use `decimal` for money, UTC for time, `TimeProvider` for "now".
- Use current .NET 10 APIs (built-in OpenAPI, not Swashbuckle).
- Produce small diffs per phase and list what it changed.

Reviewer checklist (human) for every phase. These are good journal "challenged/overrode" entries:
- [ ] Any business rule leaking into controllers or EF config?
- [ ] Any missing resource-ownership check (claimant reading others' claims)?
- [ ] Any N+1 queries or `ToList()` before filtering?
- [ ] Any `double` money, local time, or `DateTime.Now`?
- [ ] Any generated code I can't explain? Delete or simplify it.
- [ ] Tests actually assert behaviour?

---

## 15. AI working journal (deliverable)

`AI_JOURNAL.md` at the repo root, appended **during** the work (not reconstructed at the end), committed with each phase. One entry per prompt that mattered:

```
### <date time> · Phase N · <topic>
- Asked: what I asked the AI to do
- Accepted: what I kept as is
- Challenged: what I questioned, and the outcome
- Overrode: what I changed or rejected, and why
```
First entries to write in Phase 1 (from the planning session): AI suggested `.sln` to match Bento_BE, I kept `.slnx` (SDK default); AI suggested Minimal APIs, I overrode to Controllers in `ChubbClaims.Presentation`; AI's original exposure rules ignored the reserve change on approval (drift bug), caught in spec review.
