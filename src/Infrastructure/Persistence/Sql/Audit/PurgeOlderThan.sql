-- T024 — Audit/PurgeOlderThan.sql per research.md §6 + §12.
-- The retention policy is enforced by AuditRetentionPurgeService running daily at 03:00 UTC.
-- @Cutoff = now() - retention; rows older than cutoff are removed.
DELETE FROM ledger_audit
 WHERE event_at < @Cutoff;
