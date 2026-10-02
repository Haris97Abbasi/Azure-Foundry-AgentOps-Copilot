# Database Runbook

## Connection-pool exhaustion
- Symptom: clients log "timeout waiting for connection from pool"; requests queue, then fail.
- Check active vs. max connection pool size per service, recent connection pool configuration changes, and long-running queries.
- Typical fix: restore the previous connection pool size through a configuration rollback (requires approval).

## Timeout checks
- Query timeouts usually follow rising latency: compare p95 query latency with the baseline (120ms for the Orders Database).
- Review the slow-query log for queries over 500ms.
- Check for lock contention and blocking sessions.

## Safe escalation
- Never kill sessions, drop indexes, or restart the primary without DBA approval.
- If p95 latency stays above 1s for 15 minutes, page the data-platform on-call.
- Read-only diagnostics are always allowed.
