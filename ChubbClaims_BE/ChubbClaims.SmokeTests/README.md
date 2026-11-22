# Smoke tests

End-to-end checks against the running stack: the real API, SQL Server, Kafka and SignalR. Nothing is mocked.
They call the API over HTTP and the claims hub with the official .NET SignalR client, as any client would, and don't reference the API's projects.

```bash
docker compose up -d                                  # from the repository root
dotnet test ChubbClaims_BE/ChubbClaims.SmokeTests     # about 20 s
```

- **Needs only the .NET SDK.**
- **Any state, any number of runs:** no fresh seed is needed. Each test creates the claims it checks, never changes the seeded ones, and rejects its claims at the end so the queues don't fill up. The demo users' lists do collect these rejected "Smoke test" claims, so reset the database before a demo (drop it and restart the API, which seeds again).
- **Skipped, not failed, without the API:** if nothing answers at `/health`, every test is reported as skipped, with the reason. So `dotnet test` on the solution works without Docker. Any answer, even "unhealthy", runs the tests, so a broken stack fails them.
- **Another address:** set `SMOKE_API_URL` (default `http://localhost:5080`).
- **One test at a time:** some checks read whole queues, counts or exposure totals, which a parallel test creating claims would change.

| Class | Checks |
|---|---|
| `ClaimantFlowTests` | Dev login, FNOL and its refusals (expired, someone else's policy, future date, missing details), the "needs information from you" banner, search, 404 (not 403) for someone else's claim, answering a question once |
| `WorkQueueTests` | Each market's queue, most urgent first, who each claim waits on, the status filter, paging, search, out-of-range paging is 400 |
| `OfficerActionTests` | FNOL to settled with its full history, who may act, actions out of order (409), bad input (400/422), and 10 simultaneous pick-ups (always one 204 and one 409) |
| `ExposureTests` | Exposure is per currency, managers only, and updated from Kafka shortly after an FNOL (the lag is logged) |
| `DocumentTests` | Upload and read back, the type detected from the bytes, refused files, a 413 sent before the body is read, who may see a file (404 otherwise), the notification, a closed claim |
| `RealtimeTests` | Hub needs a token, pushes go to the right people and nobody else, notifications (never the reserve), mark read, team workload |

Run one class: `dotnet test ChubbClaims_BE/ChubbClaims.SmokeTests --filter "FullyQualifiedName~DocumentTests"`.
See timings and other logged details: add `--logger "console;verbosity=detailed"`.
