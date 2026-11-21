# ChubbClaims: Claims Platform (Chubb APAC take-home)

Motor and property claims across six APAC markets (MY, SG, HK, TH, ID, AU).

- **Claimants** report an incident (first notice of loss, FNOL), track their claim, answer questions and receive the decision.
- **Claims officers** pick up claims in their market, set the reserve, ask for information, approve and settle, or reject.
- **Managers** see team workload and a near-real-time view of outstanding liability exposure.

**Status:** the backend is complete (all P0 and P1 items). The Angular frontend ([FRONTEND.md](ChubbClaims_FE/FRONTEND.md)) is the next step.

| Document | What it covers |
|---|---|
| [docs/architecture.md](docs/architecture.md) | Components, event flow, claim lifecycle, data model (diagrams) |
| [docs/decisions.md](docs/decisions.md) | Key decisions, the alternatives and when I would revisit them |
| [ChubbClaims_BE/BACKEND.md](ChubbClaims_BE/BACKEND.md) | The build spec the backend was built from, updated as decisions changed |
| [AI_JOURNAL.md](AI_JOURNAL.md) | How I worked with AI: what I asked for, accepted, challenged and overrode, per phase |

---

## Run it

Prerequisites: **Docker Desktop**. Nothing else is needed to run it.

```bash
git clone https://github.com/aslam6161/ChubbClaims.git
cd ChubbClaims
cp .env.example .env        # SQL Server sa password; change it if you like
docker compose up --build   # first run: ~2-3 min (image pulls and build)
```

The API waits for SQL Server, applies migrations, seeds demo data and starts publishing events to Kafka.

| What | URL |
|---|---|
| API reference (Scalar) | http://localhost:5080/scalar |
| OpenAPI document | http://localhost:5080/openapi/v1.json |
| Health | http://localhost:5080/health |
| Kafka UI (topic `claims.events.v1`) | http://localhost:8085 |
| SQL Server | `localhost,1433`, user `sa`, password from `.env` |

### Sign in (development login)

There is no identity provider (out of scope). `POST /api/auth/dev-login` with a seeded email returns a JWT.
In Scalar: call **dev-login**, copy `accessToken`, click **Authorize** and paste it.

| Email | Role | Market |
|---|---|---|
| `claimant.my@demo.local` | Claimant | (policies in MY, incl. one expired) |
| `claimant.sg@demo.local` | Claimant | (policies in SG) |
| `officer.my@demo.local` | Claims officer | MY |
| `officer.sg@demo.local` | Claims officer | SG |
| `manager.my@demo.local` | Claims manager | MY (exposure covers all markets) |

Seed data: 13 claims across all six markets and every status, including overdue claims and one waiting on the claimant.
Only MY and SG have staff, so the HK, TH, ID and AU claims stay unassigned. They are there so exposure covers six currencies.

### A five-minute tour

1. **Claimant** (`claimant.my`): `GET /api/policies/mine` → `POST /api/claims` on `POL-MY-M-0001` → 201 with a claim number.
   Try `POL-MY-M-0000` (expired, 422) or `POL-SG-M-0001` (someone else's, 404).
2. **Officer** (`officer.my`): `GET /api/work-queue` (unassigned, SLA order) → `POST /api/claims/{id}/pick-up` → `PUT …/reserve` → `POST …/approve` → `POST …/settle`.
3. **Manager** (`manager.my`): `GET /api/reports/exposure`. The new claim appears about a second after the FNOL, via Kafka. Also try `GET /api/team/workload`.
4. **Kafka UI**: open `claims.events.v1` to see each event, its `claimId` key and its `correlation-id` header.

### Run the API outside Docker (Visual Studio / CLI)

```bash
docker compose up -d sqlserver kafka kafka-ui
cd ChubbClaims_BE
dotnet run --project ChubbClaims.Presentation   # http://localhost:5080/scalar, connects to Kafka on localhost:9092
dotnet test ChubbClaims.slnx
```

Stop the `api` container first (`docker compose stop api`), since both use port 5080.

---

## Tests

| Suite | How | Count |
|---|---|---|
| Unit tests (xUnit, Shouldly, NSubstitute) | `dotnet test ChubbClaims_BE/ChubbClaims.slnx` | 203 |
| End-to-end smoke tests against the running stack (real SQL Server, Kafka, SignalR) | [scripts/smoke](scripts/smoke/README.md) | 80 checks in 4 scripts |

Unit tests cover the claim state machine and every business rule, plus:
- service orchestration and visibility
- the Kafka event contract, checked from both sides without a broker
- the exposure and notification rules
- the outbox publisher's ordering

The smoke tests also cover things unit tests can't:
- **10 simultaneous pick-ups of the same claim:** each time exactly one 204 and one 409, caught by the RowVersion check
- **Replaying the whole topic** (consumer offsets reset to 0): every event is skipped as a duplicate and exposure is unchanged
- **Stopping Kafka:** claims are still accepted, and events drain from the outbox when Kafka returns
- **Push isolation:** another market's officer and another claimant receive nothing

---

## Architecture in one picture

```mermaid
flowchart LR
    UI[Angular / Scalar] -- REST + JWT --> API
    API -. SignalR push .-> UI
    subgraph API[ChubbClaims API: one deployable]
        C[Controllers] --> S[Services] --> D[Claim aggregate<br/>business rules]
        S --> R[(EF Core)]
        P[Outbox publisher]
        X[Exposure projector]
        N[Realtime notifier]
    end
    R -- "claim + outbox row,<br/>one transaction" --> DB[(SQL Server)]
    P -- polls outbox --> DB
    P -- "key = claimId" --> K[[Kafka<br/>claims.events.v1]]
    K -- group exposure-projector --> X -- "apply once" --> DB
    K -- group realtime-notifier --> N -- "notifications, apply once" --> DB
```

- **Commands and queries go over REST; facts go over Kafka.** The user waits only for what can be rejected (FNOL validation, a 409). Exposure and notifications react to events afterwards.
- **One deployable (a modular monolith), with seams at Kafka.** The projector and notifier only consume events, so they can move to their own services without touching the claims write side.
- **The transactional outbox avoids dual writes.** A claim change and its event commit together. Publishing is at least once; consumers apply each event exactly once.

More detail, including the sequence and state diagrams: [docs/architecture.md](docs/architecture.md).

---

## What's built, and what I cut

| Priority | Item | Status |
|---|---|---|
| P0 | Claim aggregate, lifecycle state machine and business rules, with unit tests | ✅ |
| P0 | SQL Server + EF Core 10, migrations, seed data across six markets | ✅ |
| P0 | Claimant and officer REST endpoints | ✅ |
| P0 | Dev JWT, with role-based and resource-based authorization | ✅ |
| P0 | Transactional outbox → Kafka; idempotent exposure projection | ✅ |
| P0 | Scalar, Serilog (correlation id end to end), ProblemDetails | ✅ |
| P0 | `docker compose up` for the whole stack | ✅ |
| P1 | Realtime notifier → SignalR and in-app notifications | ✅ |
| P1 | Team workload and performance endpoint | ✅ |
| P1 | `/health` (database) | ✅ |
| P2 | Approval authority limit (manager must approve above a threshold) | ✂️ cut |
| P2 | Integration tests with WebApplicationFactory + Testcontainers | ✂️ cut (end-to-end smoke scripts instead) |
| P2 | Dead-letter topic for poison messages | ✂️ cut (invalid messages are logged and skipped; handler failures retry) |

**Out of scope by design:** real identity provider, document and photo uploads, payments integration, fraud scoring, real policy-admin integration (policies are seeded), FX conversion (exposure is reported per currency), email and SMS, multiple languages, per-market regulatory rules.

**Known limitations I would fix next:**
- Paging for the work queue and notifications (capped at 200 and 50 rows).
- The SLA clock keeps running while the claim waits on the claimant.
- A SignalR backplane (Redis) and outbox row locking, both needed before running more than one API instance.
- A job to rebuild the read models.

All of these are in [docs/decisions.md](docs/decisions.md).

---

## Repository layout

```
docker-compose.yml           SQL Server 2022, Kafka (KRaft), Kafka UI, API
docs/                        architecture.md, decisions.md
scripts/smoke/               end-to-end checks against the running stack
ChubbClaims_BE/
  ChubbClaims.Presentation   host: controllers, auth, SignalR hub, middleware, OpenAPI/Scalar
  ChubbClaims.Service        use cases, DTOs, mapping (no EF, no Kafka)
  ChubbClaims.Domain         claim aggregate, state machine, rules, events (no package references)
  ChubbClaims.Repository     EF Core, migrations, seed, transactional outbox
  ChubbClaims.Messaging      outbox publisher, Kafka consumers, event contract
  ChubbClaims.Tests
ChubbClaims_FE/              Angular frontend (next)
```

## Time and AI use

The backend was built in about 1 h 45 min of implementation, from the first code commit (10:15) to the last backend merge (11:55). The specs were planned before that.
Development used Claude Code throughout. [AI_JOURNAL.md](AI_JOURNAL.md) records each phase: what I asked for, what I accepted, and where I challenged or overrode the AI. Examples include the read-modify-write race in the exposure projector, a nullable enum that leaked into the OpenAPI contract, and "overdue" flagging claims that were already decided.
Every phase was built on its own branch, reviewed, and merged with `--no-ff`, so `git log --graph` shows the steps.
