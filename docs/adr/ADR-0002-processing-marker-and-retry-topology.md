# ADR-0002: Redis wip/done marker and retry/DLQ topology

- Status: Accepted
- Date: 2026-10-11

## Context

RabbitMQ with manual acks gives **at-least-once** delivery: a crash between
processing steps re-delivers the message, and the pipeline publishes
`invoice.completed.v1` before it can mark the invoice as done. Without controls,
duplicates would double-render PDFs and double-emit completion events.

## Decision

1. **Processing marker (Redis):**
   - `wip:invoice:{invoiceId}` — `SET NX` with TTL 60s, guards against concurrent
     processing of the same invoice (live key ⇒ return retryable, backoff).
   - `done:invoice:{invoiceId}` — TTL 7 days, short-circuits duplicates (ack + skip).
   - Released on failure so retries can proceed; deterministic storage key
     (`invoices/{id}.pdf`) makes re-uploads overwrite-safe.
2. **Retry topology:** main queue → on retryable failure, republish to
   `einvoice-worker.invoice-created.retry` with per-message TTL and dead-letter
   back to the main exchange. Attempt = custom `x-attempt` header incremented by the
   subscriber on each republish (a broker-managed `x-death` count was tried first
   but does not increment reliably when the producer re-publishes with copied
   headers); delay = `min(15s × 2^attempt, 5 min)`; after 5 retries (or on permanent
   errors such as malformed events and 401/403) the message parks in
   `einvoice-worker.invoice-created.dlq`.
3. **Ordering:** publish `invoice.completed.v1` **before** `MarkCompleted`. A crash
   between the two causes one reprocess (duplicate completed event — consumers
   dedupe by `invoiceId`); the reverse order could lose the event forever.

## Consequences

- Predictable retry budget, an inspectable DLQ, and idempotent end-to-end processing.
- Redis becomes required infrastructure for the worker (key expiry is part of the
  contract: 60s must exceed worst-case processing time, 7d bounds storage).
- Consumers of `invoice.completed.v1` must tolerate duplicates (at-least-once).
- The `x-attempt` header travels with the message across retry hops; inspection
  tooling can read the attempt number directly from message headers.
