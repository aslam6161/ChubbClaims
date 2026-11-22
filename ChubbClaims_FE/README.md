# ChubbClaims web (Angular 22)

One Angular app for both user types: **claimants** report and track claims, and **claims staff** work the queue and run the team.
It talks only to the real API in [`ChubbClaims_BE`](../ChubbClaims_BE). There is no mock backend.
The build spec, and the decisions behind it, are in [FRONTEND.md](FRONTEND.md).

## Run it

Prerequisites: **Node 22.22+ or 24.15+** (tested with Node 24 and npm 11) and the backend running.

```bash
# 1. Backend: from the repository root (see the root README)
docker compose up --build

# 2. Frontend
cd ChubbClaims_FE
npm ci
npm start            # http://localhost:4200
```

Pick any demo user on the sign-in page. There's no password: the page stands in for a real identity provider and only exists in Development.
The dev server proxies `/api`, `/hubs` (WebSockets) and `/health` to the API on `127.0.0.1:5080` ([proxy.conf.json](proxy.conf.json)), so there's no CORS setup and the URLs match a production reverse proxy.

`npm ci` may warn that it skipped install scripts (esbuild, lmdb). The app builds, tests and serves without them; this was checked from a fresh clone.

| Command | What it does |
|---|---|
| `npm start` | Dev server on :4200 with the API proxy |
| `npm test` | Unit tests (Vitest), 64 tests |
| `npm run build` | Production build into `dist/` |
| `npm run api:types` | Regenerates `src/app/_api/schema.d.ts` from the running API's OpenAPI document |

## What's in it

| User | Screens |
|---|---|
| Claimant | **My claims** (what happens next, in plain words) · **Report an incident** (FNOL stepper, policy-aware validation) · **Claim detail** (timeline, decision, answer an information request) |
| Claims officer | **Work queue** (unassigned / mine, SLA order, overdue highlighted, pick up with 409 handling) · **Claim workbench** (reserve, request information, approve, settle, reject, internal notes) · **Team** (workload in their market) |
| Claims manager | Everything an officer has, plus **outstanding exposure** across all six markets, per currency, with how fresh each figure is |

Claim pages, lists and the Team page update themselves over SignalR when something they show changes. If the WebSocket is down, the toolbar says "Live updates paused" and screens poll every 30 s instead.

## How one codebase serves two user types

- `/claimant/**` and `/staff/**` are separate lazy-loaded route trees behind **`canMatch` role guards**, so a claimant's browser never even downloads the staff code.
- Each role has its own home after sign-in: claimant → My claims, officer → Work queue, manager → Team.
- Shared code is written once: session (`AuthStore`), HTTP interceptors, API types, the realtime service, and UI pieces (status badge, money, page states).
- **Guards and hidden buttons are UX, not security.** The API re-checks every request; the UI only mirrors its rules (`allowedActions` copies the backend's transition table) so people aren't offered actions that would fail.

## Decisions worth knowing

- **Types are generated from the API's OpenAPI document** and committed. A breaking API change becomes a compile error, not a runtime bug. Generating the client exposed two backend contract bugs, both fixed in the API (see [AI_JOURNAL.md](../AI_JOURNAL.md)).
- **Signals and `httpResource` for reads; small services with `HttpClient` for commands.** Commands are never optimistic: the UI waits for the server, then reloads. A 409 ("someone else got there first") or 422 is shown in plain words.
- **A push is a hint, REST is the truth.** A SignalR message only triggers a re-fetch, so the UI never trusts data the push carried.
- **No global store (NgRx).** Almost all state is server state, held per screen; only the session is global.
- **Money is always shown per currency, with 2 decimals.** Amounts are never added across currencies.
- **Brand colours with contrast in mind:** filled buttons use a deeper orange so white text passes WCAG AA; the bright orange is for accents. Status colours stay distinct from the brand colour.
- Angular 22 defaults throughout: standalone, zoneless, OnPush, built-in control flow, `@Service()`, Vitest.

## Layout

```
src/app/
  _api/            generated OpenAPI types (do not edit)
  _models/         aliases over the generated types; SignalR message types (hand-written: the hub isn't in OpenAPI)
  _services/       AuthStore, RealtimeService
  _interceptors/   bearer token + 401 handling; ProblemDetails → AppError
  _guards/         role guards
  _helpers/        pure UI rules (status labels, allowed actions, time ago)
  _shared/         status badge, money pipe, page states, confirm dialog
  _layout/         toolbar + live indicator, shared by both areas
  authentication/  dev sign-in
  claimant/        my claims, claim detail, report incident
  staff/           work queue, claim workbench, team
```

## Tests

`npm test` runs 64 unit tests: guards, interceptors, status labels and the allowed-action table, FNOL validation, realtime reload, polling and connection state, and component tests for My claims, the work queue (including the 409) and the Team page.

Each phase was also checked in a real browser against the running stack: both user types end to end, a decision in one browser updating another, and every screen at phone width with no sideways page scroll.
Those Playwright scripts were for verification and aren't committed. A committed Playwright suite is the first thing I'd add (P2 in FRONTEND.md).

## Out of scope

Real SSO sign-in, translations, layouts designed for mobile (it's responsive, not mobile-first), and offline support.
