// Console entry point per Constitution Tech Stack > Local development orchestration —
// the perf project is a console app, NOT an xUnit harness, NOT an Aspire host.
// Run via BenchmarkDotNet console invocation.
//
// Phase 7 (T080, T081) populates this project with BenchmarkDotNet jobs.
//
// Named class (not top-level statements) to avoid CS0436 collision with
// SddDemo.Ledger.Api's exported `Program` type (referenced for benchmarking).

using BenchmarkDotNet.Running;

namespace SddDemo.Ledger.Performance;

internal static class PerfRunner
{
    public static void Main(string[] args) =>
        BenchmarkSwitcher.FromAssembly(typeof(PerfRunner).Assembly).Run(args);
}
