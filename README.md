# einvoice-worker

[![ci](https://github.com/TiagoR85/einvoice-worker/actions/workflows/ci.yml/badge.svg)](https://github.com/TiagoR85/einvoice-worker/actions/workflows/ci.yml)
[![license](https://img.shields.io/badge/license-MIT-green)](LICENSE)

Async processor of the `einvoice-hub`: consumes `invoice.created.v1` events, fetches invoice data from `einvoice-api`, renders a PDF (iText), stores it in MinIO and publishes `invoice.completed.v1`.

## About this repository

This is a **study project** from my public portfolio. I learn by building: I use **AI-assisted development tools** (agent-based pair programming) while holding the project to production standards - automated tests, CI quality gates, security scanning, review pipelines and documented architecture decisions.

The scenario is simple on purpose; the engineering is not.

## Status

Work in progress (plan: docs/superpowers/plans/2026-10-11-einvoice-worker.md). See [Roadmap](#roadmap).

## Roadmap

- [x] Scaffold + contracts
- [ ] Processing pipeline (retry, DLQ, dedupe)
- [ ] PDF + MinIO + `invoice.completed`
- [ ] Docker + docs

## License

MIT
