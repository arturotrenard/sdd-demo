-- T024 — Audit/Insert.sql per data-model.md §1.2 + research.md §6.
-- Append-only — written in the same transaction as the state-change so SC-005
-- (audit visible within 5 s) is trivially satisfied.
INSERT INTO ledger_audit (
    actor_id,
    ledger_id,
    event_type,
    event_at,
    payload
)
VALUES (
    @ActorId,
    @LedgerId,
    @EventType,
    @EventAt,
    @Payload::jsonb
);
