# Feature Specification: Basic CRUD for Ledger Services

**Feature Branch**: `001-ledger-crud`
**Created**: 2026-04-27
**Status**: Draft
**Input**: User description: "I would like to build a Basic CRUD for Ledger services"

## Clarifications

### Session 2026-04-27

- Q: Ownership scope — is a ledger owned by an individual user, an organization, or shared with explicit grants? → A: Single-user ownership; only the owner can read/update/delete; names unique per user.
- Q: What does "archived" status do to behavior? → A: Archived ledgers are hidden from the default list (visible via explicit filter), read-only (only the un-archive transition is allowed), and cannot be deleted.
- Q: Delete semantics — recoverable or permanent? → A: Hard-delete; the row is removed and the audit log is the sole remaining record. Re-creating a ledger with the same name is allowed immediately after delete.
- Q: Audit log retention and access? → A: 1-year retention then automatic purge; read access limited to admin/ops, not exposed to end users.
- Q: Concurrency control on updates? → A: Optimistic concurrency. Reads return a version token; updates must echo it; stale tokens are rejected with a conflict response, prompting the client to re-read and retry.
- Q: How does FR-010's "reject anonymous" rule reconcile with the on-prem deployment context? → A: Authn/authz are explicitly **deferred** per Constitution Principle V > Deferral (services run anonymously inside the trusted on-prem perimeter). Inside that perimeter, the trusted gateway supplies a stable owner identifier on every request via the `X-Owner-Id` header, which the service treats as the authenticated principal for ownership and audit-actor purposes. The deferred authn rules **re-engage automatically as MUST** the moment any of the following becomes true: (a) a service ships beyond the on-prem network (cloud, hybrid, partner, internet); (b) a service serves more than one tenant or trust zone; (c) the deployment introduces an untrusted client. Until a trigger fires, FR-010's "authentication" is satisfied by gateway-supplied identity; once a trigger fires, FR-010 reads as written.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Create a new ledger (Priority: P1)

An authenticated user opens the application and creates a new ledger by providing a name, an optional description, and a currency. The newly created ledger is persisted, assigned a unique identifier, time-stamped, and immediately available for the user to view.

**Why this priority**: Without the ability to create a ledger, no other operation has meaning. This is the entry point for all downstream value.

**Independent Test**: A user submits a valid ledger creation request with a unique name and supported currency; the system confirms creation and the ledger appears when the user lists their ledgers.

**Acceptance Scenarios**:

1. **Given** an authenticated user with no ledger named "Operating Account", **When** they submit a creation request with name "Operating Account" and currency "USD", **Then** the system stores the ledger, returns its unique identifier, and lists it as active.
2. **Given** an authenticated user, **When** they attempt to create a ledger with a name that already exists in their own ledger collection, **Then** the system rejects the request with a clear duplicate-name message and does not create a new record.
3. **Given** an authenticated user, **When** they submit a creation request missing a required field (name or currency), **Then** the system rejects the request with a validation error indicating the missing field.

---

### User Story 2 - View ledgers (Priority: P1)

An authenticated user retrieves a list of all ledgers they own and can also fetch the full details of a single ledger by identifier. Listing supports basic pagination so the user can navigate large collections.

**Why this priority**: Users need to confirm that creation succeeded, locate ledgers by identifier, and inspect details before performing updates. Read access pairs with create as the minimum useful slice.

**Independent Test**: After creating two ledgers, the user lists their ledgers and sees both entries; fetching one by identifier returns its full details.

**Acceptance Scenarios**:

1. **Given** an authenticated user owning three ledgers, **When** they request a list of their ledgers, **Then** the system returns all three with summary fields (identifier, name, currency, status, last-updated timestamp).
2. **Given** an authenticated user owning a ledger with a known identifier, **When** they fetch that ledger by identifier, **Then** the system returns the ledger's full details.
3. **Given** an authenticated user requesting a ledger that does not exist or that they do not own, **When** they fetch by identifier, **Then** the system returns a not-found response without leaking ownership information.
4. **Given** an authenticated user owning more ledgers than the page size, **When** they request the second page, **Then** the system returns the next set of ledgers in deterministic order.

---

### User Story 3 - Update an existing ledger (Priority: P2)

An authenticated user modifies the editable attributes of a ledger they own — for example, renaming it, updating its description, or changing its status (active/archived). The system records the time of the change.

**Why this priority**: Once ledgers exist, users need to correct mistakes and reflect organizational changes. Update is essential but only meaningful after create and read are working.

**Independent Test**: A user updates the description of an existing ledger and, on re-fetch, sees the new description and an updated last-modified timestamp.

**Acceptance Scenarios**:

1. **Given** a user owns an active ledger, **When** they submit an update changing the description, **Then** the system persists the new description and updates the last-modified timestamp.
2. **Given** a user owns a ledger, **When** they submit an update with an invalid currency value, **Then** the system rejects the change and the stored ledger is unchanged.
3. **Given** a user does not own a ledger, **When** they attempt to update it, **Then** the system rejects the request and the ledger is unchanged.
4. **Given** a user owns an archived ledger, **When** they submit any update other than un-archiving (status → active), **Then** the system rejects the change and the stored ledger is unchanged.
5. **Given** a user owns an archived ledger, **When** they submit an update setting status back to active, **Then** the system marks it active and the ledger reappears in the default list.
6. **Given** two sessions read the same ledger and both attempt to update it, **When** the second session submits its update with the now-stale version token, **Then** the system rejects the second update with a conflict response and the first update's changes remain intact.

---

### User Story 4 - Delete a ledger (Priority: P3)

An authenticated user removes a ledger they own that is no longer needed. After deletion, the ledger no longer appears in their list and cannot be fetched.

**Why this priority**: Cleanup is valuable but lower-frequency than create/read/update. Users can work productively without delete in the short term.

**Independent Test**: A user deletes one of their ledgers; subsequent list and fetch operations confirm it is no longer accessible.

**Acceptance Scenarios**:

1. **Given** a user owns a ledger, **When** they delete it, **Then** the system confirms deletion and the ledger no longer appears in their list.
2. **Given** a user does not own a ledger, **When** they attempt to delete it, **Then** the system rejects the request and the ledger remains intact for its owner.
3. **Given** a ledger has already been deleted, **When** the user attempts to delete it again, **Then** the system returns a not-found response.
4. **Given** a user owns an archived ledger, **When** they attempt to delete it, **Then** the system rejects the request and instructs the user to un-archive first.

---

### Edge Cases

- What happens when a ledger name contains only whitespace or exceeds the maximum allowed length? The system rejects the creation/update with a validation error.
- What happens when two different users create ledgers with the same name? Names are unique only within a single owner's collection, so both succeed.
- How does the system handle concurrent updates to the same ledger from two sessions? Optimistic concurrency: each update must include the version token returned by the most recent read. The first writer succeeds; the second receives a conflict response and must re-read, merge, and retry.
- What happens when an unauthenticated request hits any endpoint? The system rejects it with an authentication error before any ledger logic runs.
- What happens when the user supplies an unsupported currency code? The system rejects the request with a validation error listing the accepted codes.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST allow an authenticated user to create a ledger by providing a name and a currency code, with description as an optional field.
- **FR-002**: System MUST assign each ledger a unique, stable identifier on creation and record creation and last-modified timestamps. The System MUST also maintain a per-ledger version token that changes on every successful update; the token MUST be returned by every read (single-fetch and list).
- **FR-003**: System MUST enforce that ledger names are unique within a single owner's collection (case-insensitive).
- **FR-004**: System MUST validate currency codes against a predefined list of supported codes (ISO 4217) and reject unknown values.
- **FR-005**: System MUST allow an authenticated user to retrieve a single ledger by identifier, returning a not-found response if it does not exist or belongs to another owner.
- **FR-006**: System MUST allow an authenticated user to list the ledgers they own with deterministic ordering and basic pagination. By default the list MUST include only `active` ledgers; an explicit "include archived" filter MUST be supported to retrieve archived ledgers as well.
- **FR-007**: System MUST allow an authenticated user to update editable attributes of an `active` ledger they own (name, description, status), preserving the original creation timestamp and updating the last-modified timestamp. Every update request MUST include the version token from the client's most recent read; if the supplied token does not match the current stored token, the System MUST reject the update with a conflict response and MUST NOT modify any data. On success, the System MUST issue a new version token and return it.
- **FR-007a**: For an `archived` ledger, the System MUST reject all updates except a status change back to `active` (un-archive). Renaming, description edits, and other attribute changes are not permitted while the ledger is archived.
- **FR-008**: System MUST allow an authenticated user to delete an `active` ledger they own. Delete is permanent (hard-delete): the ledger record MUST be removed and MUST be unreachable via list and fetch afterwards; the audit log entry (FR-012) is the only remaining record. After a delete, the same owner MAY immediately create a new ledger with the same name. The System MUST reject delete requests targeting an `archived` ledger; the user must un-archive it first.
- **FR-009**: System MUST reject any read/update/delete request from a user who does not own the targeted ledger, without revealing whether the ledger exists.
- **FR-010**: System MUST require an authenticated caller identity on every operation and reject requests that lack one. *(Per the 2026-04-27 clarification: while authn/authz are deferred under the on-prem deployment context, the trusted gateway supplies the owner identity via the `X-Owner-Id` header and the service rejects requests with no resolvable owner. The deferred authn rules — token-based authentication, `[Authorize]` on the gRPC service, etc. — re-engage automatically when any of the listed deferral triggers fires; FR-010 then reads as originally written.)*
- **FR-011**: System MUST return clear, actionable validation errors for malformed input (missing required fields, oversized values, invalid status, unsupported currency).
- **FR-012**: System MUST log every create, update, and delete event with the actor identifier, ledger identifier, event type, and timestamp, sufficient to reconstruct an audit trail. Audit entries MUST be retained for 1 year from the event timestamp and MUST be automatically purged after that window. Audit entries MUST NOT be exposed through any end-user-facing endpoint; read access is limited to admin/ops roles.

### Key Entities

- **Ledger**: A named, owner-scoped record representing a logical accounting container. Key attributes: unique identifier, name, optional description, currency code, status (active/archived), owner reference, version token, creation timestamp, last-modified timestamp. A ledger belongs to exactly one owner; an owner may have many ledgers.
- **Audit Entry**: A non-user-facing record of a create/update/delete event on a ledger. Key attributes: actor identifier, ledger identifier, event type, timestamp. Retained for 1 year, then purged; readable only by admin/ops.
- **Owner (User)**: The authenticated principal that owns ledgers. Identified by a stable user identifier supplied by the existing authentication layer. Out of scope for this feature beyond the ownership reference.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An authenticated user can complete the create-then-view round-trip for a new ledger in under 30 seconds end-to-end.
- **SC-002**: 99% of valid CRUD operations on a single ledger return a result to the user in under 1 second under nominal load.
- **SC-003**: Listing a user's ledgers (up to one page) returns results in under 1 second for users with up to 1,000 ledgers.
- **SC-004**: 100% of attempts by a user to access another user's ledgers are rejected (verified by automated authorization tests).
- **SC-005**: 100% of create/update/delete events appear in the audit log within 5 seconds of the operation completing.
- **SC-006**: Validation errors are clear enough that, in usability checks, at least 90% of users correct their input on the first retry without external help.

## Assumptions

- An existing authentication layer is available and provides a stable user identifier on every request; designing or extending authentication is out of scope.
- "Basic CRUD" applies to the Ledger resource itself. Posting transactions or journal entries into a ledger is out of scope for this feature and will be specified separately.
- Each ledger has a single currency that is set at creation; changing currency post-creation is out of scope and is not exposed by the update operation.
- Deletes are permanent (hard-delete). The audit log (FR-012) is the only retained record of a deleted ledger; there is no user-facing or admin restore path.
- Supported currencies are limited to ISO 4217 alphabetic codes.
- The feature targets a service-style integration (no specific UI is required for this spec); user-facing applications will consume the service.
- Pagination defaults to a reasonable page size (e.g., 50) with the option to navigate to subsequent pages; exact page-size policy is an implementation detail.
- Ledger names are limited to **100 characters** and descriptions to **500 characters** (see `data-model.md` §1.1 for the durable schema). These bounds are aligned with industry norms and are enforced server-side as part of FR-011's validation.
