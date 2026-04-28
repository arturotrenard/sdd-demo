using System.ComponentModel.DataAnnotations;
using SddDemo.Ledger.Domain.Common;

namespace SddDemo.Ledger.Domain.Auditing;

/// <summary>
/// data-model.md §1.2 — append-only audit row written in the same transaction as
/// the state change (FR-012 + SC-005). Constructor is private; callers go through
/// <see cref="Builder"/>. Payload is held as a JSON string (the SQL casts to <c>jsonb</c>);
/// using a string instead of <c>JsonDocument</c> avoids the disposal dance for write-only
/// data and matches what Dapper passes to <c>NpgsqlParameter</c>.
/// </summary>
public sealed class AuditEntry
{
    /// <summary>
    /// Sole purpose: deterministic ordering once the row is in the database.
    /// Defaults to 0 on the in-memory builder; the database assigns the real value
    /// (<c>bigserial</c>) on insert.
    /// </summary>
    public long Id { get; private init; }

    [Required]
    public Guid ActorId { get; private init; }

    [Required]
    public Guid LedgerId { get; private init; }

    public AuditEventType EventType { get; private init; }

    public DateTimeOffset EventAt { get; private init; }

    [Required]
    public string Payload { get; private init; } = "{}";

    private AuditEntry() { }

    public static AuditEntryBuilder Builder() => new();

    public sealed class AuditEntryBuilder
    {
        private long _id;
        private Guid _actorId;
        private Guid _ledgerId;
        private AuditEventType _eventType;
        private DateTimeOffset _eventAt;
        private string _payload = "{}";

        public AuditEntryBuilder WithId(long id) { _id = id; return this; }
        public AuditEntryBuilder WithActorId(Guid actorId) { _actorId = actorId; return this; }
        public AuditEntryBuilder WithLedgerId(Guid ledgerId) { _ledgerId = ledgerId; return this; }
        public AuditEntryBuilder WithEventType(AuditEventType type) { _eventType = type; return this; }
        public AuditEntryBuilder WithEventAt(DateTimeOffset at) { _eventAt = at; return this; }
        public AuditEntryBuilder WithPayload(string payload) { _payload = payload; return this; }

        public Result<AuditEntry> Build(IServiceProvider? services = null)
        {
            var entry = new AuditEntry
            {
                Id = _id,
                ActorId = _actorId,
                LedgerId = _ledgerId,
                EventType = _eventType,
                EventAt = _eventAt,
                Payload = _payload,
            };

            return DomainValidator.Validate(entry, services);
        }
    }
}
