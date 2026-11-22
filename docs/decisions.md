# Decisions

Each entry gives the decision, why I made it, what it costs, and when I would change it.
The phase-by-phase story of how each one came about, including the AI suggestions I rejected, is in [AI_JOURNAL.md](../AI_JOURNAL.md).

## Architecture

### 1. One deployable (modular monolith), not microservices
- **Why:** one team, one bounded context (claims handling), and a few hours of budget. Separate services would add network hops, distributed failure modes and deployment work, and buy nothing yet.
- **The seams are already real.** The projector and notifier react only to Kafka events, never to direct calls. Each has its own consumer group, so either can move to its own process without changing the claims write side.
- **When I'd split:**
  - reporting needs to scale differently (it will at volume)
  - notifications need their own release cadence
  - separate teams own the parts

### 2. REST for commands and queries, Kafka for facts
- The user waits only for what can be rejected or what they need right away: a claim number, a 409, a 422.
- Anything that *reacts* to a change (exposure, notifications, and later audit, fraud or SLA alerts) consumes events afterwards.
- **Cost:** exposure and notifications are eventually consistent, about 1 s in testing. The exposure response carries `lastEventAt` so the UI can show how fresh it is.

### 3. Transactional outbox instead of publishing in the request
- Publishing from the request is a dual write. A crash between the commit and the publish loses the event; publishing first can emit an event for a rolled-back change.
- The outbox row commits with the claim change, and a background publisher sends it later.
- **Cost:**
  - publishing is at least once, so consumers must be idempotent (decision 4)
  - events go out up to about 1 s later
- **Benefit:** the API keeps working while Kafka is down. Tested.

### 4. Idempotent consumers: `ProcessedEvents` in the same transaction as the effect
- Offsets are committed only after processing, so a crash causes redelivery, not loss.
- The `(ConsumerGroup, EventId)` primary key makes the effect exactly once.
- Tested by resetting the consumer group to offset 0: all 19 money-moving events were skipped as duplicates and the read model was unchanged.

### 5. The exposure projection applies deltas with atomic SQL increments
- Deltas mean the projector never reads a claim back. That's safe only because of decision 4.
- `UPDATE … SET x = x + delta` instead of load-modify-save through EF. **The first design did load-modify-save, and it loses updates with two projector instances.**
- **Cost:** if a bug ever corrupts the totals, deltas don't self-heal. The fix is a rebuild job: replay the topic or recompute from `Claims`. It's not built.

### 6. A read model for exposure, a plain indexed query for the work queue
- **Exposure is portfolio-wide.** Aggregating every open claim on each request gets slower as the book grows, while a projection stays one small table.
- **The work queue is a filtered read of one officer's claims.** Indexed columns on the write table serve it well, and it must be up to date immediately: an officer who just picked up a claim must see it.
- **Team workload** is a narrow query too. At scale it would become a projection like exposure.

### 7. Ordering: key = claimId, an outbox `Sequence` column, and the publisher stops at the first failure
- Timestamps can tie, and SQL Server sorts GUIDs in its own order, so neither gives a reliable sequence. An identity column does.
- Skipping a failed message and publishing later ones would reorder that claim's events.

## Domain and API

### 8. Rules live in the aggregate; the state machine is one table
- Every transition, every money rule and every "who may act" check is in `Claim`. Services only orchestrate, so no endpoint can bypass a rule.
- A transition missing from the table is a 409.
- **Unit tests cover the whole table**, including every invalid combination.

### 9. Task-based endpoints (`/pick-up`, `/approve`, …) instead of `PATCH status`
- Each action has its own rules, input, authorization and event.
- A generic status PATCH would push that branching into one handler and make the API say less about what's allowed.

### 10. Optimistic concurrency (RowVersion) rather than locks
- Real conflicts are rare: two officers picking up the same claim.
- A RowVersion check costs nothing until a conflict happens. Pessimistic locks would hold connections and could deadlock.
- **Tested:** 10 races with two simultaneous pick-ups, each producing exactly one 204 and one 409.

### 11. 404, not 403, for other people's resources
- Someone else's claim, policy or notification looks exactly like one that doesn't exist, so ids can't be probed.
- 403 is used only when the endpoint itself isn't allowed for the role, for example an officer calling the claimant FNOL endpoint.

### 12. 400 for request shape, 422 for business rules, 409 for state
- **400:** `[ApiController]` validation of the request shape. Request enums are non-nullable, because nullable ones made every generated TypeScript enum nullable. Missing amounts are `decimal?` with `[Required]`, because a missing amount must not silently become 0.
- **422:** domain rules, such as an expired policy, a future incident date or a negative reserve.
- **409:** the wrong state for the action, or a concurrency conflict.

### 13. Money: `decimal(18,2)`, per currency, no FX
- Amounts are always in the claim's own currency, which comes from the policy.
- Exposure totals are per currency and never summed across currencies. Adding MYR to IDR is meaningless without dated FX rates (out of scope).
- Default reserves and priority thresholds are configured **per market**, because 5,000 MYR and 5,000 IDR are very different amounts. Startup fails if any market is missing.

### 14. "Overdue" means the SLA passed without a decision
- The first version flagged approved claims waiting for payment as overdue, although the decision had been made on time.
- The SLA keeps running while waiting on the claimant. Pausing it is a policy choice I left open.

## Realtime

### 15. SignalR push is a hint; REST is the source of truth
- Clients refetch over REST when told something changed, so a lost push only delays a refresh.
- Groups (user, staff per market, managers) are assigned by the server from the JWT.
- **Limitation:** single instance only. More instances need a backplane (Redis or Azure SignalR), because the instance whose consumer handles an event can reach only its own connections.

### 16. Additive event-contract changes instead of having consumers read the write model
- The notifier needed the claimant id and the officer who asked the question.
- Reading `Claims` from the consumer would couple it to the write model. Instead the envelope gained `claimantId`, and `InformationProvided` gained `assignedOfficerId`.
- Both are optional, so the topic stays v1.
- "Status after the event" is derived from the event type, not stamped at save time. A claim changed several times in one save would otherwise stamp its final status on every event.

### 17. The notifier ignores events older than 24 h
- A new consumer group reads from the start of the topic, and the topic outlives database resets.
- Without the limit, the notifier would flood users with notifications about old changes. In testing it skipped 54 stale events.

### 21. Claim photos are private: the API streams them, storage has no public URL
- Claim evidence is personal data (damage photos, police reports, addresses). Files are stored as **private** Cloudinary assets, or on a local-disk volume when Cloudinary isn't configured. Nobody gets a storage URL: `GET /api/claims/{id}/documents/{documentId}/content` checks the caller may see the claim (404 otherwise) and streams the file. The web app fetches it with the user's token and shows it from an object URL.
- **What the server trusts:** nothing the uploader says. The type is detected from the first bytes (JPEG, PNG, WebP, PDF; SVG and HTML are refused because they can carry script). The size is counted while reading, after a fast 413 from the declared length. The storage key is chosen by the server, and the file name is cleaned and used for display only. A SHA-256 of the bytes is stored so the evidence can later be shown unchanged.
- **Order:** check the claim is open and the caller may attach → store → attach and save in one transaction with the `DocumentAttached` outbox event → if that save fails, delete the stored file. Storage and the database can't share a transaction; this order means a refusal never leaves a file behind.
- **No credentials needed to run it:** with no `CLOUDINARY_*` in `.env`, files go to a Docker volume, so `docker compose up` can attach photos. The Cloudinary adapter is written and compiles but was **not** run against a real account; everything was verified with the local-disk fallback.
- **Cost:** every view goes through the API. At volume I'd hand out short-lived signed CDN links after the same check, make thumbnails a storage transformation, and scan uploads for malware before they become visible.

## Tooling

### 18. No MediatR, AutoMapper or FluentAssertions
- All three have commercial licences since 2025.
- Plain services, hand-written mapping and Shouldly are also simpler to read and debug.

### 19. Dev JWT login instead of an identity provider
- A labelled, Development-only shortcut.
- The API depends only on the token's claims, so moving to Entra ID or Cognito changes where tokens come from, not the authorization code.

### 20. End-to-end smoke tests instead of Testcontainers integration tests (P2)
- For the time available, tests against the real Docker stack gave the most confidence per minute. They cover the concurrency race, the Kafka read models, SignalR isolation and uploads. Kafka replay and a Kafka outage were checked by hand.
- They began as Python and Node scripts and were later ported to an xUnit project (`ChubbClaims.SmokeTests`). Now a reviewer needs only the .NET SDK. Each check is a named test in `dotnet test`. Each test creates its own claims, so no fresh seed is needed. The project is black-box: HTTP and the .NET SignalR client, no reference to the API's code, so it sees the same JSON a client does.
- **Cost:** they need the stack running (skipped, with the reason, when it isn't), and they run one at a time because some read whole queues and totals.
- With more time I'd move the critical paths into WebApplicationFactory + Testcontainers tests that start their own stack in CI.

### 22. Browser tests drive the real app against the real stack (Playwright)
- **What they cover that unit tests can't:** what a user sees across two browsers. A decision showing in the claimant's open page, a pop-up with a chime, a 409 from a stale queue, a photo previewed through the token, every screen at phone width. Unit tests mock HTTP, so none of this is covered there.
- **Setup through the API, behaviour through the browser.** Each test creates its claims over HTTP (fast, no clicking through setup), then does the thing being tested in the UI, then closes its claims. No test depends on the seed's exact state, so they run in any order, any number of times.
- **Kept from flaking for the right reasons:**
  - Assertions wait for the screen, not for fixed delays.
  - A pop-up is checked against a record of every pop-up shown, because a newer one replaces it.
  - Unread counts start from a settled, empty bell, because Kafka can still be delivering the previous test's clean-up.
  - A "stale queue" is made stale on purpose by freezing the queue response, instead of racing the live update.
- **Console errors fail a test** unless it expects them. That caught nothing new, but it would catch the next unhandled error.
- **Cost:** they need the stack running and take about 2½ minutes, one test at a time.
- **Not done:** running them in CI. That needs the stack in CI (docker compose in the job), which is the same next step as #20.

## What I'd do next, in order

1. **CI:** build, test, and a container build on every PR. Then Testcontainers integration tests for the outbox and the consumers.
2. **Rebuild job for read models** and a **dead-letter topic**.
3. **Before running more than one instance:** outbox row locking (`UPDLOCK, READPAST`) and a SignalR backplane.
4. **Product:**
   - manager approval above an authority limit
   - pausing the SLA while waiting on the claimant
   - paging for notifications (the claim lists and work queue are paged)
   - reassigning a claim to another officer
