-- T024 — DeleteOptimistic.sql per research.md §5 (FR-008 hard-delete).
-- The handler distinguishes Conflict (version mismatch) from NotFound (row absent)
-- via a pre-fetch; this statement just performs the optimistic delete.
DELETE FROM ledger
 WHERE id = @Id
   AND owner_id = @OwnerId
   AND version = @ExpectedVersion
RETURNING id;
