# Design Decisions

Short architecture decision records. Each captures the alternative considered and why
it was rejected.

---

## ADR-0001 — Money is integer minor units, not `decimal`

**Decision.** `Money` is a `readonly record struct` of `(long MinorUnits, string Currency)`.

**Alternatives.** `decimal Amount` (lossless base-10 but no rounding control), `double`
(absurd), a third-party Money library (NodaMoney) (good but adds a dep we don't need).

**Why.** Card networks, processors, and the banking interchange tracks use integer minor
units. There is no rounding to negotiate, no localisation in the storage layer, no risk
of a `decimal` cast losing precision in a transitive math expression. Every rounding
decision is explicit at the call site — `Divide` returns `(quotient, remainder)`, never
a silent truncation.

---

## ADR-0002 — Balance is computed, never stored

**Decision.** Wallet has no `Balance` column. `SELECT SUM(amount) FROM ledger_entries
WHERE wallet_id = @id` is the source of truth.

**Alternatives.** Materialise the balance in a column updated on every entry write.

**Why.** A stored balance is a denormalisation. It WILL drift — bugs, lost writes,
out-of-order replays, manual SQL fixes that forget to update the cached value. With a
covering index on `(wallet_id, created_at)` the aggregation is cheap. If balance reads
ever dominate, add a materialised view; never lie about the canonical answer.

---

## ADR-0003 — Deferred constraint trigger over application-layer balance check

**Decision.** A PostgreSQL `CONSTRAINT TRIGGER ... DEFERRABLE INITIALLY DEFERRED` fires
at COMMIT and rejects any transaction where ledger entries don't sum to zero per currency.

**Alternatives.** Application-layer validation only; a simple row-level `CHECK`.

**Why.** A row-level check can't see sibling rows. An application check is bypassable
by any code path that forgets it (refactor, new endpoint, future maintainer). The DB
trigger is the bottom of the stack — every write goes through it. We *also* keep an
in-memory `Transaction.EnsureBalanced()` for fail-fast feedback, but the DB is the
source of truth.

---

## ADR-0004 — Outbox pattern over inline webhook fire

**Decision.** Webhook payloads are written to `outbox_messages` in the same DB
transaction as the ledger write. A `BackgroundService` polls the outbox and delivers.

**Alternatives.** POST the webhook from inside the handler.

**Why.** Inline POST inside a DB transaction is the classic dual-write bug: the network
call can succeed and the DB roll back, or vice versa. Either is corruption. The outbox
makes "write happened" and "webhook delivered" two atomic outcomes coordinated by the
ledger's commit. Dispatcher uses `FOR UPDATE SKIP LOCKED` so replicas don't double-send.

---

## ADR-0005 — Append-only ledger entries (no UPDATE, no DELETE)

**Decision.** Ledger entries are immutable in code (`private set`) and conventionally
append-only in storage (no UPDATE/DELETE paths). Corrections post a compensating entry
in a new transaction.

**Alternatives.** Allow voiding by flipping an `is_voided` column.

**Why.** An audit trail you can mutate is not an audit trail. Append-only means the
historical balance as of any timestamp is a stable function of the data — required for
regulatory and dispute scenarios. Voids and reversals are real ledger events; they
deserve their own row.

---

## ADR-0006 — HMAC-SHA256 webhook signing over plain shared secret

**Decision.** Webhook requests carry `X-Signature: sha256=<hex>` over
`{unix-timestamp}.{body}`, plus `X-Timestamp`. Receivers verify both.

**Alternatives.** Send the shared secret as a header (`Authorization: Bearer <secret>`).

**Why.** A shared-secret header leaks on any logging or proxy capture. HMAC over
`timestamp.body` proves the sender holds the secret *and* binds the signature to the
request body, defeating replay-with-modified-body attacks. The timestamp also enables
clock-skew rejection on the receiver side.

---

## ADR-0007 — Idempotency-Key with body hash, 409 on reuse (Stripe-style)

**Decision.** `POST` requests require an `Idempotency-Key` header. The middleware stores
the SHA-256 of the body alongside the cached response. Replay with the same body returns
the cached response; replay with a *different* body returns 409.

**Alternatives.** Dedupe blindly on the key (common in demos).

**Why.** Blind dedupe hides client bugs: a buggy client that reuses keys with different
payloads gets silent inconsistency. The 409 surfaces the bug at integration time. This
is exactly how Stripe's idempotency layer behaves.

---

## ADR-0008 — No MediatR; direct handler DI

**Decision.** Application handlers are concrete classes registered in DI; controllers
inject them directly. No mediator pipeline.

**Alternatives.** MediatR with `IRequest<T>` / `IRequestHandler<T>` plus behaviours for
validation/logging.

**Why.** MediatR is a powerful pattern but its main payoff (decoupling many request
types from many handlers) doesn't exist here — every controller already knows exactly
which handler it wants. Direct injection keeps the stack trace shallow, the DI graph
trivial, and removes a runtime dependency. Cross-cutting concerns live in middleware
(rate limit, idempotency, correlation) where ASP.NET already gives us composition.
