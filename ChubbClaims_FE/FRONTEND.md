# FRONTEND.md — Claims Platform Web (Angular 22)

> **For Claude Code:** this is the build spec for the frontend. The backend contract is defined in `../ChubbClaims_BE/BACKEND.md`.
> Work **one phase at a time** (see §10). After each phase: build, run tests, stop, and wait for review.
> Use **current Angular 22 conventions** (standalone, signals, built-in control flow). Do not generate NgModules, `*ngIf`/`*ngFor`, or zone-based patterns.

---

## 1. Goal and scope

One Angular application serving **two user types**: claimants and claims staff (officers and managers).
The brief says the frontend comes **after** the backend and a partial frontend is acceptable, so the goal is a **thin, well-structured slice that proves both user types work end to end**, not every screen.

### Priorities (MoSCoW)

| Priority | Item |
|---|---|
| **P0 Must** | App shell, dev login (pick a seeded user), JWT interceptor, role-based routing with lazy-loaded areas |
| **P0 Must** | Claimant: **Report incident (FNOL)**, **My claims** list, **Claim detail** with timeline + answer an information request |
| **P0 Must** | Officer: **Work queue** (unassigned / mine, overdue highlighting), **Claim workbench** (pick up, adjust reserve, request info, approve, settle, reject) |
| **P0 Must** | Loading / empty / error / pending states everywhere; ProblemDetails errors shown properly (e.g. 409 "already picked up") |
| **P0 Must** | Unit tests for guards, interceptor, status/action mapping, FNOL form validation, one component test |
| **P1 Should** | Realtime: SignalR connection; claim screens refresh when `ClaimUpdated` arrives |
| **P1 Should** | Staff: **Team dashboard** (workload & performance per officer for officers and managers; exposure by market/currency for managers only) |
| **P2 Could** | Playwright e2e happy path; nginx container in docker compose; notification bell |

### Out of scope (say so in the README)
Real SSO login, document upload, i18n/translations, mobile-specific layouts beyond responsive basics, offline support.

---

## 2. Tech stack (decided)

| Concern | Choice | Why / notes |
|---|---|---|
| Framework | **Angular 22**, standalone components | Required by brief |
| Reactivity | **Signals**; zoneless + OnPush (v22 defaults for new projects; keep them) | Fewer change-detection surprises; modern idiom |
| Server reads | **`httpResource()`** (stable in v22) for GET data | Gives `value` / `isLoading` / `error` signals and `reload()`, which maps directly onto pending/error states |
| Commands | Small feature services using `HttpClient` (POST/PUT) returning results; on success → `resource.reload()` | Commands need explicit error handling (409/422) |
| Forms | **Typed Reactive Forms** (default). Signal Forms are stable in v22; only use them if you can defend them line by line | Predictability under time pressure |
| UI kit | **Angular Material** (table, form fields, chips, dialog, snackbar, toolbar) | Fast, accessible, enterprise look |
| API types | **Generated from the backend OpenAPI** (`openapi-typescript` → `src/app/_api/schema.d.ts`); hand-written thin services | The OpenAPI doc is the contract; types only, so very little generated code to defend. Run via `npx openapi-typescript@7.13.0` (pinned) rather than a devDependency: it peers on TypeScript 5 and Angular 22 uses TypeScript 6 |
| Realtime | `@microsoft/signalr` (P1) | Browsers can't consume Kafka; backend bridges Kafka → SignalR |
| Global state | **None (no NgRx).** Session state in an `AuthStore` signal service; everything else is per-feature server state | App state is mostly server state; NgRx would be ceremony for this scope |
| Tests | **Vitest** (Angular CLI default test runner) + TestBed; Playwright for e2e (P2) | Use what the v22 CLI scaffolds |
| Dev proxy | `proxy.conf.json` → `/api`, `/hubs` (WebSockets) and `/health` to `http://127.0.0.1:5080` (IPv4: Node resolves `localhost` to `::1` first) | No CORS issues; same URLs as production behind a reverse proxy |

> Claude Code: before writing code, run `ng version` and check what `ng new` generated (test runner, zoneless, change detection defaults, `@Injectable` vs `@Service`). **Follow the generated defaults consistently; don't mix styles.**

---

## 3. How one codebase serves two user types (key walkthrough answer)

1. **Role-separated feature areas, lazy-loaded:** `/claimant/**` and `/staff/**` are separate route trees with their own lazy chunks.
2. **`canMatch` role guards** (not just `canActivate`): a claimant never even *downloads* the staff chunk, and vice versa.
3. **Shared core:** auth/session, HTTP interceptors, API types, realtime service, and shared UI (status badge, money display, age/SLA chip, page states) are written once.
4. **Different home per role:** after login, redirect by role (`Claimant` → `/claimant/claims`, `ClaimsOfficer` → `/staff/queue`, `ClaimsManager` → `/staff/team`).
5. **Security lives in the API.** Guards are UX; hiding a button is not authorization. Every action is re-checked server-side.
6. **Scaling story (more time):** the same boundaries could become separate deployable apps (micro-frontends / module federation) if the teams split. Not justified at this size.

---

## 4. Project structure

> Folder and file naming is aligned with `D:\Personal_Projects\IMSAnuglar` (structure only, **no code copied**): underscore-prefixed shared folders (`_services`, `_guards`, `_interceptors`, `_models`, `_layout`), feature folders, and one folder per component with separate `.component.ts / .html / .css` files.
> IMSAnuglar is **Angular 12 + NgModules**; its *patterns* are **not** adopted (see §4.1). Everything stays standalone + signals per §2.

```
ChubbClaims_FE/
  FRONTEND.md                          (this file)
  proxy.conf.json                      /api and /hubs → http://localhost:5080
  src/app/
    app.config.ts                      providers: router, httpClient(withInterceptors), animations, Material
    app.routes.ts                      top-level routes + role redirects
    app.component.ts / .html / .css    shell (toolbar + <router-outlet>)
    _api/
      schema.d.ts                      GENERATED from /openapi/v1.json (do not edit)
    _models/
      api-types.ts                     friendly aliases over generated types (Claim, WorkQueueItem, …)
      app-error.ts                     AppError (status, title, detail, field errors)
    _services/
      auth.store.ts                    signal-based session (user, token, role) persisted to sessionStorage
      realtime.service.ts              (P1) SignalR connection, reconnect, `claimUpdated` signal/stream
    _interceptors/
      auth.interceptor.ts              attach Bearer token; 401 → logout + redirect
      problem.interceptor.ts           map ProblemDetails → AppError
    _guards/
      role.guard.ts                    canMatch factory: roleGuard(['ClaimsOfficer','ClaimsManager'])
    _helpers/
      claim-status.ts                  status labels, colours, "waiting on", allowedActions per status+role (pure functions, unit tested)
    _shared/                           reusable standalone UI components
      status-badge/  money/  sla-chip/  page-state/  confirm-dialog/
    _layout/
      claimant-layout/                 toolbar + nav for claimants
      staff-layout/                    toolbar + nav for officers/managers
    authentication/
      login/                           login.component.*  pick a seeded user → POST /api/auth/dev-login
    claimant/
      claimant.routes.ts
      claimant-claims.service.ts
      my-claims/                       my-claims.component.*
      claim-detail/                    claim-detail.component.*  timeline, decision, open info request + answer form
      report-incident/                 report-incident.component.*  FNOL stepper
    staff/
      staff.routes.ts
      staff-claims.service.ts
      work-queue/                      work-queue.component.*  tabs Unassigned | Mine; SLA sort; overdue + waiting-on
      claim-workbench/                 claim-workbench.component.*  summary, reserve, info request, decision, notes, history
      team-dashboard/                  team-dashboard.component.*  (P1, staff) workload; exposure panel for managers
    errors/
      not-found/                       not-found.component.*
```

Naming (**decided in phase 1: Angular 22 defaults**, overriding the IMSAnuglar `.component` suffix): one folder per component with `name.ts` (`export class Name`) + `name.html` + `name.css`, which is what `ng generate` writes in v22; services use v22's `@Service()` decorator. Feature services sit next to their feature; app-wide services in `_services/`. Tests sit next to the file as `*.spec.ts`. Components are always `ChangeDetectionStrategy.OnPush` (set as the generator default in `angular.json`, so it's explicit, not assumed).

The tree above still shows the original `.component` names; read `login.component.*` as `login.*`.

### 4.1 Alignment with IMSAnuglar (what was adopted, what was not)

| Area | IMSAnuglar (Angular 12) | Decision for ChubbClaims |
|---|---|---|
| Shared folders | `_services`, `_guards`, `_interceptors`, `_models`, `_layout`, `_directives` | **Adopted** (plus `_api`, `_helpers`, `_shared`) |
| Feature folders | `authentication/`, `customer/`, `admin/`, `errors/` | **Adopted** as `authentication/`, `claimant/`, `staff/`, `errors/` |
| Component files | folder per component, `.component.ts/.html/.css` | Folder per component **adopted**; `.component` suffix **not** (Angular 22 defaults, decided in phase 1) |
| DTO models | `_models/dtos/*.ts` hand-written | **Not adopted**: types generated from OpenAPI (§9) |
| Modules | `app.module.ts`, `_modules/shared.module.ts` | **Not adopted**: standalone components, `app.config.ts` |
| Template syntax | `*ngIf` / `*ngFor` | **Not adopted**: `@if` / `@for` / `@switch` |
| Guards / interceptors | class-based | **Not adopted**: functional `CanMatchFn` / `HttpInterceptorFn` |
| Admin / customer split | `admin.guard`, `customer.guard`, `canActivate` | **Kept in spirit**: one `roleGuard` factory with `canMatch` (lazy chunk never downloaded) |

---

## 5. Routes

```
/login                          public
/claimant                       canMatch: roleGuard(['Claimant'])            → claimant.routes (lazy)
  /claims                       My claims
  /claims/new                   Report incident (FNOL)
  /claims/:id                   Claim detail
/staff                          canMatch: roleGuard(['ClaimsOfficer','ClaimsManager']) → staff.routes (lazy)
  /queue                        Work queue
  /claims/:id                   Claim workbench
  /team                         (any staff)                                  Team dashboard (exposure panel only for ClaimsManager)
/                               redirect by role (or /login)
**                              not-found
```

Use **component input binding** for route params (`withComponentInputBinding()`), so `id` arrives as an `input()`.

---

## 6. Screens (what each must show)

### Claimant
- **My claims:** claim number, type, status badge, reported date, "what happens next" text (e.g. *"Awaiting assessment"*, *"We need more information from you"*, *"Approved: payment being processed"*). Primary button: Report incident.
- **Report incident (FNOL):** stepper: (1) choose policy (from `/api/policies/mine`; market/currency come from the policy), (2) incident date, location, description, optional estimated loss, (3) motor **or** property details depending on policy type, (4) review & submit. Client validation mirrors the server rules (no future date; required fields per type), but **server errors (422) are still displayed per field/summary**. On success: show the claim number and go to the claim detail.
- **Claim detail:** status timeline, current reserve hidden (internal), decision and settlement amount when available, **open information request with an answer box** (disabled while submitting; success → reload).

### Officer
- **Work queue:** Material table; columns claim no., type, market, status, age, SLA (overdue in red), waiting on (Officer/Claimant), outstanding reserve + currency. Tabs Unassigned / Mine. Row click → workbench. "Pick up" button on unassigned rows. **409 → snackbar "Already picked up by another officer" + reload.**
- **Claim workbench:** buttons shown from `allowedActions(status, role, isAssignedToMe)`:
  - Adjust reserve (amount + reason)
  - Request information (question)
  - Approve (settlement amount) → confirm dialog
  - Settle → confirm
  - Reject (reason) → confirm
  - Notes list + add note; status history

  While a command is in flight: disable actions and show progress. On success: reload the claim and the queue.

### Team dashboard (P1, officers and managers)
- **Team dashboard:** workload per officer (open by status, overdue, awaiting claimant, decisions last 7 days, avg days to decision) and, **for managers only**, **exposure by market × currency × type** with the `lastEventAt` freshness ("updated 5 s ago"). **Never add amounts across currencies**; show per currency.

---

## 7. Async handling (the "decisions arrive later" question)

- Every screen handles four states from `httpResource`: **loading, error, empty, data**. No blank screens.
- **Pending business states are first-class UI:** "Submitted, waiting for an officer", "Waiting for your information", "Approved, payment pending". Derived in `_helpers/claim-status.ts`, unit tested.
- **Commands are not optimistic.** The server can reject them (409/422), so the UI waits for the response, then reloads.
- **Realtime (P1):** `RealtimeService` connects to `/hubs/claims` with the JWT. On `ClaimUpdated(claimId)`: if the claim is on screen, `reload()` it; the queue reloads too. Push is only a **hint**; data always comes from REST.
- **Fallback:** if the hub is disconnected, poll the visible claim/queue every 30 s and show a small "live updates paused" indicator.
- **Exposure numbers are eventually consistent** (built from Kafka events): show the "updated X ago" timestamp instead of pretending they're instant.

---

## 8. Tests (Vitest + TestBed)

P0:
- `role.guard`: allows the right roles, blocks others, redirects to login when not signed in.
- `auth.interceptor`: adds the Bearer header; 401 logs out.
- `problem.interceptor`: maps ProblemDetails to `AppError` (409/422 cases).
- `claim-status.ts`: `allowedActions` table for every status × role × assigned flag; "what happens next" text.
- FNOL form: type-specific required fields switch correctly; future date rejected.
- `work-queue.component`: renders rows, highlights overdue, shows the snackbar on a 409 pick-up (HTTP mocked with `provideHttpClientTesting`).

P2: Playwright e2e: claimant submits FNOL → officer picks up and approves → claimant sees "Approved".

---

## 9. Contract workflow ("who defines the contract?")

- The **backend owns the contract**: the OpenAPI 3.1 document at `/openapi/v1.json` is generated from the API code.
- The frontend **generates TypeScript types** from it: `npm run api:types` (`openapi-typescript http://localhost:5080/openapi/v1.json -o src/app/_api/schema.d.ts`). The generated file is committed so the app builds without the API running.
- A breaking API change shows up as a **compile error** in Angular, not a runtime bug.
- More time: CI step that regenerates types and fails on diff; contract tests; AsyncAPI for the Kafka events.

---

## 10. Build phases (commit after each; journal entry per phase)

| # | Phase | Done when | Suggested commit |
|---|---|---|---|
| 1 | `ng new` (v22 defaults) in `ChubbClaims_FE`, folder layout per §4, Material, proxy, app shell, generated API types | App runs on :4200; types compile | `chore(web): scaffold Angular 22 app, Material, API types` |
| 2 | Core: AuthStore, dev login page, interceptors, role guards, role routing + tests | Log in as each seeded user, land on the right area | `feat(web): dev login, interceptors and role-based routing` |
| 3 | Claimant: My claims, Claim detail (+ answer info request), FNOL stepper + tests | Claimant flow works against the real API | `feat(web): claimant FNOL, tracking and info responses` |
| 4 | Officer: Work queue, Claim workbench actions + tests | Officer flow works; 409 handled | `feat(web): officer work queue and claim workbench` |
| 5 (P1) | Realtime service + Team dashboard | A decision in one browser updates the other | `feat(web): realtime updates and team dashboard` |
| 6 | README section, screenshots, polish | Fresh clone: `npm ci && npm start` works | `docs(web): run instructions and frontend decisions` |

**Time budget for the frontend: about 1–1.5 h.** If short on time, stop after phase 4 and list phase 5 under "more time".

---

## 11. Rules for the AI (Claude Code) and reviewer checklist

Claude Code must:
- Use standalone components, signals, `input()`/`output()`, `@if`/`@for`/`@switch`, `inject()`. No NgModules, no `*ngIf`, no constructor-injection boilerplate, no `any`.
- Keep components presentational where possible; HTTP lives in feature services; business-ish UI rules live in `_helpers/` as pure functions.
- Not invent API endpoints or fields. Use only the generated types; if something is missing, **stop and say so** (it's a backend change).
- Not add NgRx, Tailwind, or other libraries without asking.

Reviewer checklist (human): good "challenged/overrode" journal material.
- [ ] Any subscription leaks / manual `subscribe()` that should be a resource or `toSignal`?
- [ ] Any money summed across currencies?
- [ ] Any role check that exists only in the UI with no server-side equivalent?
- [ ] Every screen has loading/empty/error states?
- [ ] Errors from ProblemDetails shown to the user in plain words?
- [ ] Anything generated that I can't explain? Delete or simplify.
