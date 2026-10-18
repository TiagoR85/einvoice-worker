# ADR-0001: Events trigger processing, payloads are fetched over REST

- Status: Accepted
- Date: 2026-10-11

## Context

The API publishes `invoice.created.v1` with a deliberately small payload
(`{ invoiceId, number, currency, totalAmount }`). Rendering a PDF needs much more:
issue/due dates, line items, status and both parties (name, country, VAT).

## Decision

The event is a **trigger, not a data carrier**. On `invoice.created.v1` the worker
authenticates as its own OAuth2 client (`einvoice-worker`, scope `invoice:read`,
client-credentials) and fetches `GET /v1/invoices/{id}` plus the two
`GET /v1/parties/{id}` calls over REST before rendering.

## Consequences

- The event contract stays small and stable; the API can enrich invoices later
  without touching the broker contract.
- The worker always renders the *current* invoice state, and reuses the API's
  authorization model instead of inventing a second one.
- Processing needs 3 extra HTTP calls and the API must be reachable — network and
  5xx failures are absorbed by the retry queue (ADR-0002).
- The worker's read-only scope limits blast radius if its secret leaks.
