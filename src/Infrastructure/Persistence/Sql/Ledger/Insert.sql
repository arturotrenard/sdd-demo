-- T024 — Insert.sql per data-model.md §2 + research.md §5.
-- Owner-scoped name uniqueness is enforced by ux_ledger_owner_name_lower (case-insensitive).
INSERT INTO ledger (
    id,
    owner_id,
    name,
    description,
    currency_code,
    status,
    version,
    created_at,
    last_modified_at
)
VALUES (
    @Id,
    @OwnerId,
    @Name,
    @Description,
    @CurrencyCode,
    @Status,
    @Version,
    @CreatedAt,
    @LastModifiedAt
)
RETURNING version;
