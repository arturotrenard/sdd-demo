using System.ComponentModel.DataAnnotations;
using SddDemo.Ledger.Domain.Common;
using SddDemo.Ledger.Domain.Currency;

namespace SddDemo.Ledger.Domain.Ledgers;

/// <summary>
/// data-model.md §1.1 / §3.1 — the ledger aggregate root.
/// Constructor is private; callers MUST go through <see cref="Builder"/>.
/// All properties are <c>private init</c> and only ever set by the Builder.
/// Validation runs through Data Annotations + <see cref="IValidatableObject"/>
/// via <see cref="DomainValidator.Validate{T}"/> in the Builder's <c>Build()</c>.
/// </summary>
public sealed class Ledger : IValidatableObject
{
    [Required]
    public Guid Id { get; private init; }

    [Required]
    public Guid OwnerId { get; private init; }

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Name { get; private init; } = default!;

    [StringLength(500)]
    public string? Description { get; private init; }

    [Required]
    [ValidIsoCurrency]
    public string CurrencyCode { get; private init; } = default!;

    public LedgerStatus Status { get; private init; }

    [Range(1, long.MaxValue)]
    public long Version { get; private init; }

    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset LastModifiedAt { get; private init; }

    private Ledger() { }

    public static LedgerBuilder Builder() => new();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            yield return new ValidationResult(
                "Name must not be whitespace.",
                [nameof(Name)]);
        }

        if (CreatedAt > LastModifiedAt)
        {
            yield return new ValidationResult(
                "LastModifiedAt must be >= CreatedAt.",
                [nameof(LastModifiedAt)]);
        }
    }

    public sealed class LedgerBuilder
    {
        private Guid _id;
        private Guid _ownerId;
        private string _name = string.Empty;
        private string? _description;
        private string _currencyCode = string.Empty;
        private LedgerStatus _status = LedgerStatus.Active;
        private long _version = 1;
        private DateTimeOffset _createdAt;
        private DateTimeOffset _lastModifiedAt;

        public LedgerBuilder WithId(Guid id) { _id = id; return this; }
        public LedgerBuilder WithOwnerId(Guid ownerId) { _ownerId = ownerId; return this; }
        public LedgerBuilder WithName(string name) { _name = name; return this; }
        public LedgerBuilder WithDescription(string? description) { _description = description; return this; }
        public LedgerBuilder WithCurrencyCode(string code) { _currencyCode = code; return this; }
        public LedgerBuilder WithStatus(LedgerStatus status) { _status = status; return this; }
        public LedgerBuilder WithVersion(long version) { _version = version; return this; }

        public LedgerBuilder WithTimestamps(DateTimeOffset created, DateTimeOffset modified)
        {
            _createdAt = created;
            _lastModifiedAt = modified;
            return this;
        }

        /// <summary>
        /// Builds and validates the aggregate. The optional <paramref name="services"/>
        /// is forwarded to <see cref="DomainValidator.Validate{T}"/> so that
        /// <see cref="ValidIsoCurrencyAttribute"/> can resolve <see cref="ICurrencyCatalog"/>.
        /// </summary>
        public Result<Ledger> Build(IServiceProvider? services = null)
        {
            var ledger = new Ledger
            {
                Id = _id,
                OwnerId = _ownerId,
                Name = _name,
                Description = _description,
                CurrencyCode = _currencyCode,
                Status = _status,
                Version = _version,
                CreatedAt = _createdAt,
                LastModifiedAt = _lastModifiedAt,
            };

            return DomainValidator.Validate(ledger, services);
        }
    }
}
