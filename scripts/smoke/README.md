# Smoke tests

End-to-end checks against a running API at `http://localhost:5080` (local `dotnet run` or `docker compose up`).
They use the real database, Kafka and SignalR; nothing is mocked.

| Script | Checks | Needs |
|---|---|---|
| `officer_flow.py` | Work queue, every officer action, 10 simultaneous pick-up races (one 204, one 409) | **fresh seed** |
| `claimant_flow.py` | Dev login, FNOL validation, visibility (404 not 403), answering an information request | **fresh seed** |
| `exposure_flow.py` | Exposure read model updated from Kafka after an FNOL | any state |
| `realtime_flow.mjs` | SignalR pushes and isolation, notifications, team workload | any state; `npm install` once |

Run on a fresh database, in this order:

```bash
python officer_flow.py
python claimant_flow.py
python exposure_flow.py
npm install && npm run realtime
```

A fresh seed: drop the `ChubbClaims` database and restart the API (it migrates and seeds in Development).
The two "fresh seed" scripts count seeded claims and change seeded claims' status, so a second run fails by design.
