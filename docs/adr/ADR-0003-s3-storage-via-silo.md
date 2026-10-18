# ADR-0003: Plain S3 SDK with Silo as MinIO-compatible storage

- Status: Accepted
- Date: 2026-10-17

## Context

The worker persists rendered PDFs in S3-compatible object storage. Until
September 2026 the stack used the official `minio/minio` server image; that image
was removed from Docker Hub when MinIO went source-only, breaking
`docker compose up` for fresh clones. The bucket layout
(`invoices/{invoiceId}.pdf`) and the upload code were already written against
the AWS S3 API surface.

## Decision

1. **Keep the plain S3 protocol as the contract.** The worker talks to storage
   only through the MinIO .NET SDK's S3 operations (`PutObject`, bucket ensure);
   no MinIO-specific APIs are used. Any S3-compatible endpoint is valid.
2. **Swap the server implementation to [`pgsty/silo`](https://hub.docker.com/r/pgsty/silo)**
   in `docker-compose.yml`, a community MinIO-compatible (S3) server that ships
   as a published image. Configuration (`Minio:Endpoint/AccessKey/SecretKey/Bucket`)
   stays unchanged, so only the image reference moved.
3. **Secrets and endpoint remain environment-driven** (`.env`, gitignored), so a
   future storage swap (Silo -> LocalStack -> real AWS S3) is a configuration
   change, not a code change.

## Consequences

- Fresh clones boot again without vendoring or building MinIO from source; the
  one-command demo in the hub compose keeps working.
- The worker is insulated from storage-vendor availability: the SDK abstraction
  absorbs a second provider change with zero code edits.
- Silo is a community project with a smaller security track record than MinIO;
  the bucket holds generated PDFs only (regenerable from the invoice), so the
  blast radius of a storage compromise is limited to the documents themselves -
  stored-object integrity relies on the deterministic key + Redis `done` marker
  from ADR-0002.
- Integration tests already run against the same S3 API via Testcontainers, so
  CI keeps validating the real wire protocol regardless of the server image.
