#!/usr/bin/env bash
# T088 — Constitution Tech Stack > Local development orchestration (v2.10.0):
# runtime projects (Domain, Application, Infrastructure, Infrastructure.Migrations,
# Contracts, Api) MUST NOT reference src/AppHost. The AppHost is a developer-experience
# artifact only — production deployments do NOT run it.

set -euo pipefail

RUNTIME_PROJECTS=(
  "src/Domain"
  "src/Application"
  "src/Infrastructure"
  "src/Infrastructure.Migrations"
  "src/Contracts"
  "src/Api"
)

OFFENDERS=""
for project in "${RUNTIME_PROJECTS[@]}"; do
  match=$(grep -RIn -E "<ProjectReference[^>]*Include=\"[^\"]*AppHost[^\"]*\"" "$project" 2>/dev/null || true)
  if [[ -n "$match" ]]; then
    OFFENDERS+="$match"$'\n'
  fi
done

if [[ -n "$OFFENDERS" ]]; then
  echo "FAIL: runtime project references src/AppHost — Tech Stack v2.10.0 forbids this." >&2
  printf '%s' "$OFFENDERS" >&2
  exit 1
fi

echo "OK: no runtime project references src/AppHost."
