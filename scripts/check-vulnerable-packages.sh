#!/usr/bin/env bash
# T085 — Constitution Principle V (always-on dependency scanning).
# Fails the build if `dotnet list package --vulnerable --include-transitive`
# reports any vulnerable package across the solution.

set -euo pipefail

OUTPUT="$(dotnet list SddDemo.Ledger.slnx package --vulnerable --include-transitive 2>&1)"
echo "$OUTPUT"

# A clean run prints only "no tiene paquetes vulnerables" / "has no vulnerable packages"
# per project. A non-clean run includes a "Resuelto" / "Resolved" column.
if echo "$OUTPUT" | grep -qE "Resuelto[[:space:]]+Gravedad|Resolved[[:space:]]+Severity"; then
  echo "FAIL: vulnerable packages detected — see report above." >&2
  exit 1
fi

echo "OK: no vulnerable packages."
