-- T024 — GetById.sql per data-model.md §1.1 + FR-005.
-- Owner-scoped: the predicate includes owner_id so cross-owner reads return
-- no rows (handler maps no-rows to NotFound — FR-009 no-information-leak).
SELECT id,
       owner_id         AS OwnerId,
       name             AS Name,
       description      AS Description,
       currency_code    AS CurrencyCode,
       status           AS Status,
       version          AS Version,
       created_at       AS CreatedAt,
       last_modified_at AS LastModifiedAt
  FROM ledger
 WHERE id = @Id
   AND owner_id = @OwnerId
 LIMIT 1;
