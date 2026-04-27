-- T024 — UpdateOptimistic.sql per research.md §5.
-- The compare-and-swap is atomic in a single statement so the database is the
-- single arbiter of conflicts (no SELECT-then-UPDATE race).
UPDATE ledger
   SET name             = @Name,
       description      = @Description,
       status           = @Status,
       version          = version + 1,
       last_modified_at = @LastModifiedAt
 WHERE id = @Id
   AND owner_id = @OwnerId
   AND version = @ExpectedVersion
RETURNING version;
