using System.Collections.Concurrent;
using System.Reflection;

namespace SddDemo.Ledger.Infrastructure.Persistence;

/// <summary>
/// Loads SQL strings out of the assembly's embedded resources (<c>Persistence/Sql/**/*.sql</c>),
/// caching the result so repeated reads don't re-stream the manifest. The MSBuild
/// <c>EmbeddedResource</c> name mangles backslashes/slashes into dots, so
/// <c>Ledger/Insert.sql</c> ends up as <c>SddDemo.Ledger.Infrastructure.Persistence.Sql.Ledger.Insert.sql</c>.
/// </summary>
internal static class EmbeddedSql
{
    private static readonly Assembly OwningAssembly = typeof(EmbeddedSql).Assembly;
    private const string ResourceRoot = "SddDemo.Ledger.Infrastructure.Persistence.Sql";

    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.Ordinal);

    /// <param name="relative">Slash-delimited path under <c>Persistence/Sql/</c>, e.g. <c>"Ledger/Insert.sql"</c>.</param>
    public static string Load(string relative)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relative);

        return Cache.GetOrAdd(relative, key =>
        {
            var resourceName = $"{ResourceRoot}.{key.Replace('/', '.')}";
            using var stream = OwningAssembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException(
                    $"Embedded SQL resource not found: '{resourceName}'. " +
                    $"Verify <EmbeddedResource Include=\"Persistence\\Sql\\**\\*.sql\" /> is set in the csproj.");

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        });
    }
}
