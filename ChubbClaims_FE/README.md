# ChubbClaims web (Angular 22)

One Angular app for both user types: **claimants** report and track claims, and **claims staff** work the queue and run the team.
It talks only to the real API in [`ChubbClaims_BE`](../ChubbClaims_BE). There is no mock backend.
The build spec, and the decisions behind it, are in [FRONTEND.md](FRONTEND.md).

## Run it

**Just to use it:** `docker compose up --build` from the repository root also builds this app and serves it at http://localhost:4200 (the `web` container). Nothing else to install.

**To work on it** with live reload: Node 22.22+ or 24.15+ (tested with Node 24 and npm 11), and the backend running.

```bash
# 1. Backend: from the repository root (see the root README)
docker compose up --build
docker compose stop web   # frees port 4200 for the dev server

# 2. Frontend
cd ChubbClaims_FE
npm ci
npm start            # http://localhost:4200
```

The `web` container ([Dockerfile](Dockerfile), [nginx.conf](nginx.conf)) builds the production bundle and serves it with nginx:
- **One origin:** `/api`, `/hubs` (WebSockets) and `/health` are forwarded to the API container, as the dev server's proxy does.
- **Deep links:** a link like `/staff/claims/{id}` loads the app. A missing file is a real 404, not the app's page.
- **Caching:** `index.html` is never cached. The hashed bundles are cached for a year, since their names change with their content.
- **Uploads:** photos up to 10 MB get through. nginx allows a little more, so the API, not nginx, refuses a larger file with its own message.
- **Headers:** `nosniff`, `X-Frame-Options: DENY` and a same-origin referrer policy.
- **API rebuilds:** nginx re-resolves the API's address, so rebuilding the API doesn't need a web restart.

Pick any demo user on the sign-in page. There's no password: the page stands in for a real identity provider and only exists in Development.
The dev server proxies `/api`, `/hubs` (WebSockets) and `/health` to the API on `127.0.0.1:5080` ([proxy.conf.json](proxy.conf.json)), so there's no CORS setup and the URLs match a production reverse proxy.

`npm ci` may warn that it skipped install scripts (esbuild, lmdb). The app builds, tests and serves without them; this was checked from a fresh clone.

| Command | What it does |
|---|---|
| `npm start` | Dev server on :4200 with the API proxy |
| `npm test` | Unit tests (Vitest), 99 tests |
| `npm run e2e` | Browser tests (Playwright) against the running stack, 38 tests |
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

`npm test` runs 99 unit tests (`*.spec.ts` next to the code they test). They cover:
- guards, interceptors, status labels and the allowed-action table
- FNOL validation, including the reporting deadline
- realtime reload, polling and connection state
- search and paging, toasts, the notification bell, and the photo checks
- component tests for My claims, the work queue (including the 409) and the Team page

### Browser tests

`npm run e2e` runs 38 Playwright tests (`e2e/*.e2e.ts`) in Chromium against the real API, database, Kafka and SignalR.

```bash
docker compose up -d                 # from the repository root: the API and its stack
npx playwright install chromium      # once
npm run e2e                          # about 2 minutes
```

- **Any state, any number of runs.** Each test creates the claims it needs through the API, drives the app through the browser, and closes its claims at the end. No fresh seed is needed. The demo users' lists do collect these closed claims, so reset the database before a demo.
- **One browser per user.** A test can have a claimant and an officer signed in side by side, which is how live updates and notifications are tested.
- **Console errors fail the test.** Any error in a browser console fails it, unless the test expects that error (a 409 it provokes, say).
- **Sound and pop-ups are recorded.** Chimes are counted by wrapping Web Audio. Every pop-up a page shows is recorded, so a newer one replacing it can't hide it.
- **Against either build:** the tests use whatever answers at `http://localhost:4200`. That's the `web` container (the production build behind nginx) or `npm start`. If nothing answers, they start the dev server there.
- **On failure:** a screenshot and a trace are saved; `npx playwright show-report e2e-report`.
- **In VS Code:** with the Playwright extension (recommended in `.vscode/extensions.json`), the tests appear in the Testing panel and can be run or debugged one at a time.
- **Settings:** `E2E_API_URL` and `E2E_WEB_URL` override the defaults (`http://127.0.0.1:5080`, `http://localhost:4200`).

| File | What it covers |
|---|---|
| `sign-in` | Redirect to sign-in, each role's home, a claimant never downloads staff code (and vice versa), refresh, sign out |
| `claimant` | My claims and the banner, answering a question, reporting a motor or property incident, the date picker, the error state |
| `officer` | Queue order, pick up, reserve, notes, ask, approve and settle, reject with cancel, a stale queue's 409, a manager's read-only view |
| `realtime` | "Live" in the toolbar, a decision showing in another browser, a new claim reaching the queue and exposure, paused without a WebSocket |
| `notifications` | Pop-ups with a chime, the bell count and menu, sound off, officers told of new claims (and another market not) |
| `search-paging` | Page size, paging that keeps the current page on screen, search, the banner, the team page link |
| `documents` | Photos and PDFs both ways, live, with previews, the viewer, drag and drop, and a closed claim |
| `phone` | Every screen at 390 px with no sideways page scroll |
| `toasts` | Info, error, warning and success pop-ups each look different |

## Out of scope

Real SSO sign-in, translations, layouts designed for mobile (it's responsive, not mobile-first), and offline support.
