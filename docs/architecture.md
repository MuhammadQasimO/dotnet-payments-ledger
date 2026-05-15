# Architecture

## Layering (Clean Architecture)

```
┌──────────────────────────────────────────────────────────────────┐
│ PaymentsLedger.Api                                               │
│ Controllers · Middleware · Program.cs · OTel/Serilog wiring      │
└────────────────┬─────────────────────────────────────────────────┘
                 │ depends on
┌────────────────▼─────────────────────────────────────────────────┐
│ PaymentsLedger.Application                                       │
│ Handlers · Commands/Queries · Abstractions (interfaces only)     │
└────────────────┬─────────────────────────────────────────────────┘
                 │ depends on
┌────────────────▼─────────────────────────────────────────────────┐
│ PaymentsLedger.Domain                                            │
│ Money · Wallet · LedgerEntry · Transaction · Exceptions          │
│ ZERO framework dependencies                                      │
└──────────────────────────────────────────────────────────────────┘
                 ▲ implements
┌────────────────┴─────────────────────────────────────────────────┐
│ PaymentsLedger.Infrastructure                                    │
│ LedgerDbContext (EF Core + Npgsql) · Repositories · Migrations   │
│ RedisIdempotencyStore · RedisSlidingWindowRateLimiter            │
│ EfOutbox · OutboxDispatcher (BackgroundService) · HmacWebhookSigner │
└──────────────────────────────────────────────────────────────────┘
```

Dependency rule: arrows point inward. The Domain knows nothing of EF Core, ASP.NET, or
Redis. The Application defines what it needs as interfaces; Infrastructure implements
them. Swap the implementation and the rest doesn't care.

## Request flow — `POST /api/transfers`

```
client ──HTTP──▶ ASP.NET Core pipeline:
                   Serilog request log
                   GlobalException
                   Correlation (X-Correlation-Id)
                   UserId (X-User-Id from upstream gateway)
                   RateLimit (Redis sliding window per IP + per user)
                   Idempotency (SHA-256 of body keyed by Idempotency-Key)
                 │
                 ▼
                 TransfersController.Post
                 │
                 ▼
                 TransferHandler.HandleAsync
                   1. FindByIdempotencyKey   ─── replay if found
                   2. Load both wallets
                   3. Transaction.NewTransfer (in-memory balance check)
                   4. Insert tx + entries + outbox row
                 │
                 ▼
                 UnitOfWork.SaveChangesAsync
                   BEGIN; INSERT ...; COMMIT;
                   │
                   ▼  on COMMIT, the deferred trigger fires:
                   check_transaction_balanced() — rejects unbalanced txs
                 │
                 ▼
                 201 Created (or replayed cached response)

         later:
                 OutboxDispatcher (BackgroundService, every 2s)
                   SELECT ... FOR UPDATE SKIP LOCKED LIMIT 50
                   HMAC-SHA256 sign body + timestamp
                   POST to webhook endpoint
                   on failure: exponential retry (5s, 30s, 5m, 30m, 6h)
                   after 6 attempts: mark dead-letter
```

## Why the deferred trigger is load-bearing

A row-level `CHECK` constraint cannot see sibling rows; an application-only validator
can be bypassed by a future code path. The deferred constraint trigger fires once at
`COMMIT` with the full transaction view, so the database guarantees:

> For every transaction id, the sum of `ledger_entries.amount` per `currency` is exactly zero.

This makes balance bugs impossible-by-construction at the storage layer — even if the
application code is wrong.

## Why outbox over direct webhook fire

If the API POST'd the webhook from inside the DB transaction, you'd be one network hiccup
away from either (a) committing the ledger and never sending the webhook, or (b) sending
the webhook but rolling back the ledger. Both are corruption.

The outbox is written in the same transaction as the ledger entries, then a background
dispatcher reads it and delivers — at-least-once, retryable, restart-safe. `FOR UPDATE
SKIP LOCKED` lets multiple dispatcher replicas run without double-sending.
