#!/usr/bin/env bash
# T087 — Constitution Tech Stack > Local development orchestration (v2.10.0):
# Aspire is NOT a test host. Test projects MUST NOT reference
# Aspire.Hosting.Testing or DistributedApplicationTestingBuilder.

set -euo pipefail

OFFENDERS=$(grep -RIn -E "Aspire\.Hosting\.Testing|DistributedApplicationTestingBuilder" \
  tests 2>/dev/null || true)

if [[ -n "$OFFENDERS" ]]; then
  echo "FAIL: Aspire test-host hooks detected in tests/ — v2.10.0 carve-out forbids this." >&2
  echo "$OFFENDERS" >&2
  exit 1
fi

echo "OK: no Aspire test-host references in tests/."
