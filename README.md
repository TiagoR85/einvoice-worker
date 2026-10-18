# einvoice-worker

[![ci](https://github.com/TiagoR85/einvoice-worker/actions/workflows/ci.yml/badge.svg)](https://github.com/TiagoR85/einvoice-worker/actions/workflows/ci.yml)
[![license](https://img.shields.io/badge/license-MIT-green)](LICENSE)

Async processor of the `einvoice-hub` (EU e-invoicing). It consumes `invoice.created.v1` events from RabbitMQ, fetches the full invoice from [`einvoice-api`](https://github.com/TiagoR85/einvoice-api), renders a PDF with iText, stores it in S3-compatible object storage (Silo — a MinIO-compatible server) and publishes `invoice.completed.v1`.

## About this repository

This is a **study project** from my public portfolio. I learn by building: I use **AI-assisted development tools** (agent-based pair programming) while holding the project to production standards - automated tests, CI quality gates, security scanning, review pipelines and documented architecture decisions.

The scenario is simple on purpose; the engineering is not.

## How it works

```
RabbitMQ (einvoice.events) ──invoice.created.v1──▶ EventSubscriber (manual ack)
                                                      │ 1. dedupe check (Redis wip/done marker)
                                                      │ 2. GET /v1/invoices + /v1/parties (OAuth2)
                                                      │ 3. render PDF (iText)
                                                      │ 4. put invoices/{id}.pdf (S3-compatible storage)
                                                      ▼ 5. publish invoice.completed.v1
                                              success → ack · failure → retry queue (15s×2ⁿ, max 5) → DLQ
```

### Event contract

| Event | Routing key | Producer | `data` payload |
|---|---|---|---|
| Invoice created | `einvoice.invoice.created.v1` | einvoice-api (outbox) | `{ invoiceId, number, currency, totalAmount }` |
| Invoice completed | `einvoice.invoice.completed.v1` | einvoice-worker | `{ invoiceId, objectKey, contentType, sizeBytes, completedAt }` |

Both are structured-mode CloudEvents (`application/cloudevents+json`) on topic exchange `einvoice.events`.

### Delivery semantics

- **At-least-once** with manual acks (`autoAck:false`): a crash re-delivers.
- **Retry:** exponential backoff `min(15s × 2ⁿ, 5 min)` via a retry queue (per-message TTL + dead-lettering).
- **DLQ:** after 5 retries, or immediately on permanent errors (bad payload, 401/403) → `einvoice-worker.invoice-created.dlq` for inspection.
- **Dedupe:** Redis `done:invoice:{invoiceId}` (7d) skips duplicates; `wip:invoice:{invoiceId}` (60s) guards concurrent processing. The deterministic storage key makes re-uploads overwrite-safe.

## Quickstart — one command (the whole hub)

```powershell
git clone https://github.com/TiagoR85/einvoice-ui.git
cd einvoice-ui
Copy-Item .env.example .env   # set OIDC_CLIENT_SECRET, OIDC_WORKER_CLIENT_SECRET, POSTGRES_PASSWORD
docker compose up --build
```

The compose in [`einvoice-ui`](https://github.com/TiagoR85/einvoice-ui) builds this worker from `main` and boots it against the hub's RabbitMQ, Redis and Silo.

### Alternative — worker against an existing hub

With the [`einvoice-api`](https://github.com/TiagoR85/einvoice-api) stack running (`docker compose up` on ports 8080/5672/6379):

```powershell
git clone https://github.com/TiagoR85/einvoice-worker.git
cd einvoice-worker
Copy-Item .env.example .env    # set WORKER_CLIENT_SECRET = OIDC_WORKER_CLIENT_SECRET used by the API
docker compose up --build
```

Then create an invoice via the API (see the einvoice-api README for the token) and watch the worker logs: `Invoice {id} processed into invoices/{id}.pdf`.

Local development without containers for the worker itself:

```powershell
dotnet run --project src\Einvoice.Worker   # after `docker compose up` in einvoice-api
```

## Configuration

| Key | Default | Notes |
|---|---|---|
| `RabbitMq:Host/Port/Username/Password` | localhost/5672/guest/guest | broker from the API compose |
| `Redis:Configuration` | localhost:6379 | processing marker |
| `Minio:Endpoint/AccessKey/SecretKey/Bucket` | localhost:9000/minioadmin/minioadmin/einvoice-docs | S3-compatible object storage (Silo) |
| `Api:BaseUrl` | http://localhost:8080 | invoice/party reads |
| `Api:ClientId/ClientSecret/Scope` | einvoice-worker/(env)/invoice:read | OAuth2 client-credentials |

Secrets come only from environment variables (`.env`, gitignored); `.env.example` documents keys without values.

> **Note on object storage:** `docker-compose.yml` uses [`pgsty/silo`](https://hub.docker.com/r/pgsty/silo), a community MinIO-compatible (S3) server — the original `minio/minio` image was removed from Docker Hub in Sep 2026 when MinIO went source-only. The MinIO .NET SDK speaks plain S3, so any S3-compatible endpoint works.

## Tests

```powershell
dotnet test tests\Einvoice.Worker.Tests\Einvoice.Worker.Tests.csproj /p:CollectCoverage=true /p:Threshold=80
```

Unit tests run in milliseconds; integration tests boot real RabbitMQ, Redis and Silo containers (Testcontainers) plus a WireMock stub of the API. Coverage gate: 80% (enforced in CI).

## Decisions

- [ADR-0001](docs/adr/ADR-0001-event-as-trigger.md) — events trigger, payloads are fetched over REST
- [ADR-0002](docs/adr/ADR-0002-processing-marker-and-retry-topology.md) — Redis wip/done marker + retry/DLQ topology
- [ADR-0003](docs/adr/ADR-0003-s3-storage-via-silo.md) — plain S3 SDK with Silo as MinIO-compatible storage

## Roadmap

- [x] Scaffold + contracts
- [x] Processing pipeline (retry, DLQ, dedupe)
- [x] PDF + object storage + `invoice.completed`
- [x] Docker + docs
- [ ] Blazor UI + one-command demo (F1c, sibling repo)

## License

MIT
