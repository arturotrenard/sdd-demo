#!/usr/bin/env bash
# T084 — Constitution Principle II coverage gate (>= 80% patch coverage).
#
# Runs `dotnet test` with coverlet's XPlat Code Coverage collector against every
# test project, merges the per-project Cobertura outputs via ReportGenerator,
# and (if a base ref is provided) computes diff-aware patch coverage on the
# changed lines only.
#
# Usage:
#   scripts/coverage-gate.sh                    # whole-suite gate (>= 80% line coverage)
#   scripts/coverage-gate.sh main               # patch gate vs origin/main (>= 80% on changed lines)
#
# Exits non-zero if the gate fails.

set -euo pipefail

THRESHOLD="${COVERAGE_THRESHOLD:-80}"
BASE_REF="${1:-}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
RESULTS_DIR="$ROOT/TestResults/coverage"
REPORT_DIR="$ROOT/TestResults/coverage-report"

rm -rf "$RESULTS_DIR" "$REPORT_DIR"
mkdir -p "$RESULTS_DIR"

echo "==> Running tests with coverage collection (coverlet XPlat)…"
dotnet test "$ROOT/SddDemo.Ledger.slnx" \
  --configuration Release \
  --collect:"XPlat Code Coverage" \
  --results-directory "$RESULTS_DIR" \
  --logger "trx;LogFileName=coverage.trx" \
  --nologo

echo "==> Aggregating Cobertura reports via ReportGenerator…"
dotnet tool install -g dotnet-reportgenerator-globaltool >/dev/null 2>&1 || true
export PATH="$HOME/.dotnet/tools:$PATH"

reportgenerator \
  "-reports:$RESULTS_DIR/**/coverage.cobertura.xml" \
  "-targetdir:$REPORT_DIR" \
  "-reporttypes:Cobertura;TextSummary;Html" \
  "-assemblyfilters:+SddDemo.Ledger.*;-SddDemo.Ledger.*.Tests;-SddDemo.Ledger.Performance" \
  >/dev/null

echo
cat "$REPORT_DIR/Summary.txt"
echo

LINE_RATE="$(python3 -c "
import xml.etree.ElementTree as ET, sys
root = ET.parse(sys.argv[1]).getroot()
print(float(root.attrib.get('line-rate', '0')) * 100)
" "$REPORT_DIR/Cobertura.xml")"

printf '==> Whole-suite line coverage: %.2f%% (threshold %s%%)\n' "$LINE_RATE" "$THRESHOLD"

if (( $(printf '%.0f' "$LINE_RATE") < THRESHOLD )); then
  echo "FAIL: whole-suite line coverage below ${THRESHOLD}% threshold." >&2
  exit 1
fi

if [[ -n "$BASE_REF" ]]; then
  echo "==> Diff-aware patch coverage vs $BASE_REF…"
  if ! command -v git >/dev/null; then
    echo "git missing — skipping patch gate." >&2
    exit 0
  fi

  CHANGED_FILES="$(git diff --name-only --diff-filter=AM "$BASE_REF"...HEAD -- 'src/**/*.cs')"
  if [[ -z "$CHANGED_FILES" ]]; then
    echo "No changed C# source files in src/. Patch gate trivially passes."
    exit 0
  fi

  python3 - "$BASE_REF" "$REPORT_DIR/Cobertura.xml" <<'PY'
import subprocess, sys, xml.etree.ElementTree as ET, os, re

base_ref = sys.argv[1]
report   = sys.argv[2]
threshold = int(os.environ.get("COVERAGE_THRESHOLD", "80"))

tree = ET.parse(report)
root = tree.getroot()

# Collect every covered/uncovered line per (lower-cased) filename.
coverage = {}
for cls in root.iter("class"):
    fname = cls.attrib.get("filename", "").replace("\\", "/")
    if not fname:
        continue
    lines = coverage.setdefault(fname.lower(), {})
    for line in cls.iter("line"):
        ln  = int(line.attrib["number"])
        hit = int(line.attrib.get("hits", "0")) > 0
        # If multiple class entries cover the same line, OR the hits.
        lines[ln] = lines.get(ln, False) or hit

# Parse `git diff` to collect added line numbers per file.
diff_cmd = ["git", "diff", "--unified=0", f"{base_ref}...HEAD", "--", "src/**/*.cs"]
diff = subprocess.check_output(diff_cmd, text=True)

added = {}
current = None
for raw in diff.splitlines():
    if raw.startswith("+++ b/"):
        current = raw[6:].lower()
        added.setdefault(current, set())
        continue
    if raw.startswith("@@") and current is not None:
        m = re.search(r"\+(\d+)(?:,(\d+))?", raw)
        if not m:
            continue
        start = int(m.group(1))
        count = int(m.group(2) or "1")
        for ln in range(start, start + count):
            added[current].add(ln)

total = 0
covered = 0
for path, lines in added.items():
    # Match the file in the cobertura report by suffix because cobertura paths
    # may be relative to a different root.
    matched = next(
        (k for k in coverage if k.endswith(path) or path.endswith(k)),
        None,
    )
    if matched is None:
        continue
    for ln in lines:
        if ln not in coverage[matched]:
            continue
        total += 1
        if coverage[matched][ln]:
            covered += 1

if total == 0:
    print("No measurable patch lines (no added executable lines).")
    sys.exit(0)

pct = covered * 100.0 / total
print(f"Patch coverage: {covered}/{total} = {pct:.2f}% (threshold {threshold}%)")
if pct + 1e-9 < threshold:
    print(f"FAIL: patch coverage below {threshold}% threshold.", file=sys.stderr)
    sys.exit(1)
PY
fi

echo "OK: coverage gate passed."
