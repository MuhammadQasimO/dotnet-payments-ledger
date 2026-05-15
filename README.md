# dotnet-payments-ledger

A double-entry payments ledger in **.NET 8**, demonstrating the patterns from production
cross-border payments systems: balanced books enforced at the database, Stripe-style
idempotent transfers, multi-currency support, signed webhook delivery, full
observability.

> Built as a reference implementation of patterns I designed and shipped on a production
> fintech application — extracted into a clean, runnable repo so the design choices are
> visible.

---

## What this demonstrates

- **Money value object** — signed `long` minor units (no `decimal`, no `double`),
  cross-currency arithmetic throws, division returns `(quotient, remainder)` (no silent
  rounding).
- **Database-enforced balance invariant** — `DEFERRABLE INITIALLY DEFERRED` constraint
  trigger fires at `COMMIT` and rejects any unbalanced transaction. Application checks
  are defence-in-depth on top of the DB truth.
- **Append-only ledger** — `ledger_entries` has no UPDATE/DELETE path; corrections
  post compensating entries in new transactions (auditable by construction).
- **Stripe-style HTTP idempotency** — `Idempotency-Key` + SHA-256 body hash. Replay with
  the same body → cached response. Same key, **different** body → **409** (surfaces
  client bugs that blind dedupe would hide).
- **Two-layer rate limiting** — Redis sliding-window (per-IP and per-user) implemented
  as a single atomic Lua script. Standard `X-RateLimit-*` headers + `Retry-After`.
- **Outbox pattern + signed webhooks** — webhook rows written in the same DB transaction
  as the ledger. `BackgroundService` polls with `FOR UPDATE SKIP LOCKED` so replicas
  don't double-send. HMAC-SHA256 over `{timestamp}.{body}` (replay-resistant). Retry
  schedule 5s, 30s, 5m, 30m, 6h, then dead-letter.
- **Three-layer tests** — xUnit unit tests, **CsCheck property tests** (invariants hold
  across random transfer sequences), Testcontainers integration tests against real
  Postgres + Redis including a deferred-trigger rollback test.
- **Observability** — Serilog (JSON in prod, pretty in dev), OpenTelemetry traces with
  `X-Correlation-Id` propagation, Prometheus `/metrics`, `/health` + `/health/ready`.
- **Clean Architecture** — Domain has zero NuGet refs; Application defines interfaces;
  Infrastructure implements; Api wires composition. Verified at build time via project
  references.

---

## Why double-entry?

Single-entry bookkeeping can *detect* movement but not enforce correctness — you have
to trust the writer. Double-entry makes balance bugs **impossible by construction**: for
every signed entry on one wallet, an equal-and-opposite entry exists on another; the sum
across the system stays zero. When the database also enforces this (deferred trigger),
no application bug, manual SQL, or future code path can break the invariant.

---

## Architecture

See [`docs/architecture.md`](docs/architecture.md) for the layer diagram and the
`POST /api/transfers` request flow.

---

## Key design decisions

Full ADRs in [`docs/design-decisions.md`](docs/design-decisions.md):

1. Integer minor units, not `decimal` (ADR-0001)
2. Balance is computed, never stored (ADR-0002)
3. Deferred trigger over application-layer validation (ADR-0003)
4. Outbox pattern over inline webhook fire (ADR-0004)
5. No soft deletes on ledger entries (ADR-0005)
6. HMAC signing over plain shared secret (ADR-0006)
7. Idempotency-Key with body hash, 409 on reuse (Stripe-style) (ADR-0007)
8. No MediatR — direct handler DI (ADR-0008)

---

## Quick start

```bash
# 1. Spin up Postgres + Redis + API
docker compose up --build -d

# 2. Wait for /health/ready
curl http://localhost:8080/health/ready

# 3. Create two wallets
curl -sS http://localhost:8080/api/wallets \
  -H 'Content-Type: application/json' \
  -d '{ "userId": "00000000-0000-0000-0000-000000000001", "currency": "USD" }'

# 4. Transfer with idempotency key
curl -sS http://localhost:8080/api/transfers \
  -H 'Content-Type: application/json' \
  -H 'Idempotency-Key: abc-1' \
  -d '{
    "fromWalletId": "<wallet-1>",
    "toWalletId":   "<wallet-2>",
    "amountMinorUnits": 1500,
    "currency": "USD",
    "reference": "demo"
  }'
```

Full API reference in [`docs/api.md`](docs/api.md). Swagger UI in dev: `/swagger`.

---

## Testing

```bash
# Unit + property (xUnit + CsCheck)
dotnet test tests/PaymentsLedger.UnitTests

# Integration (Testcontainers — needs Docker running)
dotnet test tests/PaymentsLedger.IntegrationTests
```

Property tests run hundreds of randomised transfer sequences and assert that the
double-entry invariant holds after each one. Integration tests include a direct
SQL-level test that forces an unbalanced insert and asserts the deferred trigger
rolls it back at COMMIT.

---

## Non-goals (intentionally out of scope)

This repo is a clean reference, not a complete production payments system. The following
are deliberately **not** implemented:

- **Authentication / authorisation.** Assumed to be performed by an upstream gateway
  (Kong / Envoy / API Gateway) that injects `X-User-Id`.
- **Real FX rates.** Cross-currency transfers are out of scope; production needs a live
  rate feed with rate-locking and slippage handling.
- **KYC / AML hooks.** Real money movement requires compliance integration.
- **Multi-region replication, sharding, read replicas.** Single-Postgres demo.
- **Regulatory reporting** (SAR / CTR / tax).
- **Cross-currency transfers.** Same-currency only; cross-currency adds an FX wallet
  middleman and rate-locking semantics.
- **Reconciliation engine** against external bank statements. Natural v2.

---

## Production notes

Things that would change for a real deployment:

- Connection string and webhook shared secret come from a secret store (Azure Key Vault
  / AWS Secrets Manager), not `appsettings.json`.
- `AutoMigrate=true` is **off**; migrations are applied out-of-band via CI/CD using
  `dotnet ef migrations script` against a controlled gate.
- `OpenTelemetry:OtlpEndpoint` points at the org's collector; sampling is configured.
- Prometheus scrapes `/metrics` via service discovery; alert rules wired to PagerDuty.
- Webhook target endpoints are per-tenant with rotating secrets.

---

## Stack

| Layer            | Choice                                                  |
|------------------|---------------------------------------------------------|
| Runtime          | .NET 8 (LTS)                                            |
| Web              | ASP.NET Core (Controllers)                              |
| ORM              | EF Core 8 + Npgsql                                      |
| Database         | PostgreSQL 16                                           |
| Cache / limits   | Redis 7                                                 |
| Testing          | xUnit + Testcontainers + FluentAssertions               |
| Property tests   | CsCheck                                                 |
| Observability    | OpenTelemetry + Serilog + prometheus-net                |
| API docs         | Swashbuckle                                             |
| Runtime / dev    | Docker Compose                                          |
| CI               | GitHub Actions (Ubuntu + Windows matrix)                |

---

## License

MIT — see [`LICENSE`](LICENSE).
