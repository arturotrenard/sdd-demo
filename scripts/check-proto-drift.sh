#!/usr/bin/env bash
# T086 — Constitution Principle III: the planning snapshot at
# specs/001-ledger-crud/contracts/ledger.v1.proto MUST stay byte-identical to
# the build-time source at src/Contracts/Protos/ledger.v1.proto.
# Any drift is a review-blocking change.

set -euo pipefail

SPEC="specs/001-ledger-crud/contracts/ledger.v1.proto"
SRC="src/Contracts/Protos/ledger.v1.proto"

if ! diff -q "$SPEC" "$SRC" >/dev/null; then
  echo "FAIL: proto drift detected." >&2
  echo "  spec:  $SPEC" >&2
  echo "  build: $SRC" >&2
  diff -u "$SPEC" "$SRC" >&2 || true
  exit 1
fi

echo "OK: proto snapshot matches build-time source."
