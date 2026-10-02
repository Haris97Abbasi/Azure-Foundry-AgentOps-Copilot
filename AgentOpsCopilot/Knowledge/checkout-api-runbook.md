# Checkout API Runbook

## Common 5xx causes
- Payment gateway timeouts or SDK errors (look for PaymentGatewayClient exceptions in logs).
- Database connection-pool exhaustion ("timeout waiting for connection from pool").
- A bad configuration value or feature flag shipped in the latest release.

## Dependency checks
1. Payment gateway: check the provider status page and the client timeout setting; gateway p99 latency is normally under 3s.
2. Orders Database: check connection-pool usage and p95 query latency.
3. Redis cache: check hit rate and connection errors.

## Rollback criteria
- Roll back the latest release if the 5xx rate stays above 5% for 10 minutes and started within 1 hour of a deployment.
- Roll back only through the release pipeline, with approval from the on-call lead.
- After a rollback, confirm the 5xx rate falls below 1% within 15 minutes.
