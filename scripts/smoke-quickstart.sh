#!/usr/bin/env bash
# T082 / T083 — quickstart.md §4 smoke flow.
#
# T082: run against `dotnet run --project src/AppHost` (canonical bring-up).
# T083: run against the docker-compose fallback (see quickstart.md §3a) +
#       `dotnet run --project src/Api`.
#
# This script does NOT start the service for you — both bring-up paths are
# manual per the constitution (T082 also requires you to inspect the Aspire
# dashboard's Traces tab to confirm a single trace covers
# gRPC server → handler → repository → SQL → audit insert per quickstart §5.4).
#
# Usage:
#   GRPC_HOST=localhost:5001 OWNER=<uuid> scripts/smoke-quickstart.sh
#
# Prereqs: grpcurl, jq.

set -euo pipefail

GRPC_HOST="${GRPC_HOST:?set GRPC_HOST=host:port (e.g. localhost:5001)}"
OWNER="${OWNER:?set OWNER=<uuid> (the X-Owner-Id used for the smoke flow)}"
PROTO_FILE="${PROTO_FILE:-src/Contracts/Protos/ledger.v1.proto}"
PROTO_IMPORT="${PROTO_IMPORT:-src/Contracts/Protos}"
GRPCURL_BASE=(grpcurl -import-path "$PROTO_IMPORT" -proto "$PROTO_FILE" -H "x-owner-id: $OWNER")

if ! command -v grpcurl >/dev/null; then
  echo "grpcurl not found — install it first." >&2
  exit 1
fi
if ! command -v jq >/dev/null; then
  echo "jq not found — install it first." >&2
  exit 1
fi

echo "==> 4.1 CreateLedger"
created=$("${GRPCURL_BASE[@]}" -d '{
  "name": "Operating Account",
  "description": "Primary operating ledger",
  "currency_code": "USD"
}' "$GRPC_HOST" sddDemo.ledger.v1.Ledgers/CreateLedger)
echo "$created" | jq .

ledger_id=$(echo "$created" | jq -r .id)
version=$(echo "$created" | jq -r .versionToken)

echo "==> 4.2 GetLedger"
"${GRPCURL_BASE[@]}" -d "{\"id\": \"$ledger_id\"}" \
  "$GRPC_HOST" sddDemo.ledger.v1.Ledgers/GetLedger | jq .

echo "==> 4.3 ListLedgers"
"${GRPCURL_BASE[@]}" -d '{"page_size": 50}' \
  "$GRPC_HOST" sddDemo.ledger.v1.Ledgers/ListLedgers | jq '.ledgers | length'

echo "==> 4.4 UpdateLedger (description)"
updated=$("${GRPCURL_BASE[@]}" -d "{
  \"id\": \"$ledger_id\",
  \"version_token\": \"$version\",
  \"description\": \"primary operating account (smoke)\"
}" "$GRPC_HOST" sddDemo.ledger.v1.Ledgers/UpdateLedger)
echo "$updated" | jq .
version=$(echo "$updated" | jq -r .versionToken)

echo "==> 4.5 Archive then attempt rename (must fail with InvalidArgument)"
archived=$("${GRPCURL_BASE[@]}" -d "{
  \"id\": \"$ledger_id\",
  \"version_token\": \"$version\",
  \"status\": \"LEDGER_STATUS_ARCHIVED\"
}" "$GRPC_HOST" sddDemo.ledger.v1.Ledgers/UpdateLedger)
echo "$archived" | jq .
version=$(echo "$archived" | jq -r .versionToken)

if "${GRPCURL_BASE[@]}" -d "{
  \"id\": \"$ledger_id\",
  \"version_token\": \"$version\",
  \"name\": \"Renamed While Archived\"
}" "$GRPC_HOST" sddDemo.ledger.v1.Ledgers/UpdateLedger 2>/dev/null
then
  echo "FAIL: archived rename did not reject" >&2
  exit 1
fi

echo "==> 4.6 Delete (un-archive first per FR-007a, then delete)"
unarchived=$("${GRPCURL_BASE[@]}" -d "{
  \"id\": \"$ledger_id\",
  \"version_token\": \"$version\",
  \"status\": \"LEDGER_STATUS_ACTIVE\"
}" "$GRPC_HOST" sddDemo.ledger.v1.Ledgers/UpdateLedger)
version=$(echo "$unarchived" | jq -r .versionToken)

"${GRPCURL_BASE[@]}" -d "{\"id\": \"$ledger_id\", \"version_token\": \"$version\"}" \
  "$GRPC_HOST" sddDemo.ledger.v1.Ledgers/DeleteLedger | jq .

echo
echo "OK: quickstart §4 smoke flow passed against $GRPC_HOST."
echo "    For T082: inspect the Aspire dashboard Traces tab and confirm a"
echo "    single trace spans gRPC → handler → repository → SQL → audit insert."
