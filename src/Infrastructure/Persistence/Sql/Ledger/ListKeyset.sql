-- T024 — ListKeyset.sql per research.md §9 + FR-006.
-- Keyset pagination over (last_modified_at DESC, id DESC). The handler decodes
-- the opaque base64 cursor into (@CursorTimestamp, @CursorId) and the
-- LIMIT @PageSize + 1 trick lets the caller know whether a next page exists.
-- @IncludeArchived = true returns both Active and Archived; false (default) only Active.
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
 WHERE owner_id = @OwnerId
   AND (@IncludeArchived OR status = 1)
   AND (
        @CursorTimestamp IS NULL
     OR (last_modified_at, id) < (@CursorTimestamp, @CursorId)
   )
 ORDER BY last_modified_at DESC, id DESC
 LIMIT @Limit;
