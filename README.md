# ChubbClaims — Claims Platform (Chubb APAC take-home)

Motor and property claims across six APAC markets: claimants report and track claims; claims staff pick up, assess and settle or reject them; managers see workload and outstanding liability exposure.

- Backend spec: [ChubbClaims_BE/BACKEND.md](ChubbClaims_BE/BACKEND.md) (.NET 10, SQL Server, Kafka)
- Frontend spec: [ChubbClaims_FE/FRONTEND.md](ChubbClaims_FE/FRONTEND.md) (Angular 22)
- AI working journal: [AI_JOURNAL.md](AI_JOURNAL.md)

> Work in progress. Built phase by phase (see BACKEND.md §12); this README is completed in the final phase.

## Run (Docker)

Prerequisites: Docker Desktop.

```bash
cp .env.example .env          # set MSSQL_SA_PASSWORD if you want a different one
docker compose up --build
```

| What | URL |
|---|---|
| API reference (Scalar) | http://localhost:5080/scalar |
| OpenAPI document | http://localhost:5080/openapi/v1.json |
| Kafka UI | http://localhost:8085 |
| SQL Server | `localhost,1433` (user `sa`) |

## Run the API from Visual Studio / CLI

Start only the infrastructure, then run the API on the host (it connects to Kafka on `localhost:9092`):

```bash
docker compose up -d sqlserver kafka kafka-ui
cd ChubbClaims_BE
dotnet run --project ChubbClaims.Presentation     # http://localhost:5080/scalar
dotnet test ChubbClaims.slnx
```

## Solution layout

```
ChubbClaims_BE/
  ChubbClaims.slnx
  ChubbClaims.Presentation   HTTP host: controllers, middleware, OpenAPI/Scalar, Serilog
  ChubbClaims.Service        use cases (no EF, no Kafka)
  ChubbClaims.Repository     EF Core + SQL Server, transactional outbox
  ChubbClaims.Domain         claim aggregate + business rules (no package references)
  ChubbClaims.Messaging      Kafka publisher and consumers
  ChubbClaims.Tests          xUnit + Shouldly + NSubstitute
ChubbClaims_FE/              Angular app
```
