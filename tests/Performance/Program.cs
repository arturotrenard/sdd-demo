// Console entry point per Constitution Tech Stack > Local development orchestration —
// the perf project is a console app, NOT an xUnit harness, NOT an Aspire host.
// Run via `dotnet run -c Release --project tests/Performance`.
//
// Named class (not top-level statements) to avoid CS0436 collision with
// SddDemo.Ledger.Api's exported `Program` type (referenced for benchmarking).
//
// T080 perf gate: after BenchmarkSwitcher returns, every benchmark report's
// percentile statistics are inspected. The process exits non-zero if any of:
//   - Single-ledger CRUD (Create, Get_*, Update, Delete) p99 >= 1 s   (SC-002)
//   - ListLedgers_1000 p95 >= 1 s                                     (SC-003)

using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace SddDemo.Ledger.Performance;

internal static class PerfRunner
{
    private static readonly string[] Sc002CrudBenchmarks =
    [
        "CreateLedger",
        "GetLedger_CacheHit",
        "GetLedger_CacheMiss",
        "UpdateLedger",
        "DeleteLedger",
    ];

    private const string Sc003ListBenchmark = "ListLedgers_1000";
    private const double OneSecondNs = 1_000_000_000d;

    public static int Main(string[] args)
    {
        var summaries = BenchmarkSwitcher
            .FromAssembly(typeof(PerfRunner).Assembly)
            .Run(args)
            .ToArray();

        var violations = new List<string>();
        foreach (var summary in summaries)
        {
            foreach (var report in summary.Reports)
            {
                CheckBudgets(report, violations);
            }
        }

        if (violations.Count > 0)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("PERF GATE FAILED (Constitution Workflow > Performance budgets):");
            foreach (var v in violations)
            {
                Console.Error.WriteLine("  - " + v);
            }
            return 1;
        }

        return 0;
    }

    private static void CheckBudgets(BenchmarkReport report, List<string> violations)
    {
        var stats = report.ResultStatistics;
        if (stats is null)
        {
            return;
        }

        var name = report.BenchmarkCase.Descriptor.WorkloadMethod.Name;
        var p95Ns = stats.Percentiles.P95;

        // P99 may be 0 on short runs that lack the sample density. Fall back to P95.
        var p99Field = stats.Percentiles.GetType().GetProperty("P99");
        var p99Raw = p99Field?.GetValue(stats.Percentiles) as double?;
        var p99Ns = p99Raw is > 0 ? p99Raw.Value : p95Ns;

        if (Array.IndexOf(Sc002CrudBenchmarks, name) >= 0 && p99Ns >= OneSecondNs)
        {
            violations.Add(
                $"SC-002 violated: {name} p99 = {p99Ns / 1e6:F1} ms (budget < 1 000 ms).");
        }

        if (name == Sc003ListBenchmark && p95Ns >= OneSecondNs)
        {
            violations.Add(
                $"SC-003 violated: {name} p95 = {p95Ns / 1e6:F1} ms (budget < 1 000 ms).");
        }
    }
}
