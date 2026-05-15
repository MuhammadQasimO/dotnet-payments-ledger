# API

Base URL (local): `http://localhost:8080`. Swagger UI: `/swagger` (Development env).

All responses are JSON. Errors follow RFC 7807 (`application/problem+json`).

---

## Auth

This service does not authenticate. An upstream gateway must inject `X-User-Id: <guid>`;
the service reads it and trusts it. See ADR-0008 in `docs/design-decisions.md` for the
rationale.

```
X-User-Id: 00000000-0000-0000-0000-000000000001
```

---

## `POST /api/wallets`

Create a wallet for a user in a specific currency. Balance starts at zero.

```bash
curl -sS http://localhost:8080/api/wallets \
  -H 'Content-Type: application/json' \
  -H 'X-User-Id: 00000000-0000-0000-0000-000000000001' \
  -d '{ "userId": "00000000-0000-0000-0000-000000000001", "currency": "USD" }'
```

201 Created:

```json
{
  "walletId": "cc8d…",
  "currency": "USD",
  "balance": { "amountMinorUnits": 0, "currency": "USD", "display": "0.00 USD" }
}
```

---

## `GET /api/wallets/{id}/balance[?asOf=...]`

Current balance, or the historical balance as of a timestamp (ISO 8601).

```bash
curl -sS 'http://localhost:8080/api/wallets/cc8d.../balance'
curl -sS 'http://localhost:8080/api/wallets/cc8d.../balance?asOf=2026-05-16T12:00:00Z'
```

---

## `POST /api/transfers`

Same-currency transfer. **Requires** the `Idempotency-Key` header.

```bash
curl -sS http://localhost:8080/api/transfers \
  -H 'Content-Type: application/json' \
  -H 'Idempotency-Key: transfer-2026-05-16-001' \
  -d '{
    "fromWalletId": "cc8d…",
    "toWalletId":   "771a…",
    "amountMinorUnits": 1500,
    "currency": "USD",
    "reference": "invoice-9001"
  }'
```

201 Created on first call. **Replay with the same key and same body** → 200 with
`Idempotent-Replay: true`. **Replay with the same key and a different body** → 409.

---

## `GET /api/transactions/{id}`

Fetch a transaction with all ledger entries.

```bash
curl -sS http://localhost:8080/api/transactions/abc1…
```

---

## Operational endpoints

```
GET /health          # liveness, always 200 if process is up
GET /health/ready    # readiness, pings Postgres + Redis
GET /metrics         # Prometheus text exposition
```

---

## Rate limits

Two-layer sliding window (Redis), default per IP **100/s** and per user **50/s**.

Every response carries:

```
X-RateLimit-Limit: 100
X-RateLimit-Remaining: 87
X-RateLimit-Reset: 1715864400
```

On 429, `Retry-After: <seconds>` is set.
