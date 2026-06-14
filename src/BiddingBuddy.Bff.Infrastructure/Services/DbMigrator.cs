using System.Reflection;
using BiddingBuddy.Bff.Core.DTOs.Admin;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BiddingBuddy.Bff.Infrastructure.Services;

/// <summary>
/// Runs embedded SQL migration scripts against PostgreSQL on demand.
///
/// Scripts live in Persistence/Migrations/*.sql, are marked &lt;EmbeddedResource&gt;,
/// and are applied in ascending filename order (e.g. 0001_*, 0002_*). Each applied
/// script is recorded in the schema_migrations table, so re-running the endpoint
/// only executes scripts that have not run yet. Every script + its tracking insert
/// run inside a single transaction: a failing script rolls back fully and is not
/// recorded, so it will be retried on the next call.
/// </summary>
public class DbMigrator(BffDbContext db, ILogger<DbMigrator> log) : IDbMigrator
{
    private const string ResourcePrefix = "Persistence.Migrations.";

    public async Task<IReadOnlyList<MigrationStatusDto>> GetStatusAsync(CancellationToken ct = default)
    {
        var conn = await OpenAsync(ct);
        await EnsureMigrationsTableAsync(conn, ct);
        var applied = await LoadAppliedAsync(conn, ct);

        return GetScripts()
            .Select(s => applied.TryGetValue(s.Name, out var at)
                ? new MigrationStatusDto(s.Name, true, at)
                : new MigrationStatusDto(s.Name, false, null))
            .ToList();
    }

    public async Task<MigrationRunResultDto> ApplyPendingAsync(CancellationToken ct = default)
    {
        var conn = await OpenAsync(ct);
        await EnsureMigrationsTableAsync(conn, ct);
        var applied = await LoadAppliedAsync(conn, ct);

        var scripts = GetScripts();
        var nowApplied      = new List<string>();
        var alreadyApplied  = new List<string>();

        foreach (var script in scripts)
        {
            if (applied.ContainsKey(script.Name))
            {
                alreadyApplied.Add(script.Name);
                continue;
            }

            var sql = await ReadResourceAsync(script.ResourceName, ct);

            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                await using (var cmd = new NpgsqlCommand(sql, conn, tx))
                {
                    cmd.CommandTimeout = 120;
                    await cmd.ExecuteNonQueryAsync(ct);
                }

                await using (var rec = new NpgsqlCommand(
                    "INSERT INTO schema_migrations (name, applied_at) VALUES (@name, NOW())", conn, tx))
                {
                    rec.Parameters.AddWithValue("name", script.Name);
                    await rec.ExecuteNonQueryAsync(ct);
                }

                await tx.CommitAsync(ct);
                nowApplied.Add(script.Name);
                log.LogInformation("Applied migration {Name}", script.Name);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                log.LogError(ex, "Migration {Name} failed; rolled back. Aborting remaining migrations.", script.Name);
                throw new InvalidOperationException(
                    $"Migration '{script.Name}' failed and was rolled back: {ex.Message}", ex);
            }
        }

        return new MigrationRunResultDto(nowApplied, alreadyApplied, scripts.Count);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);
        return conn;
    }

    private static async Task EnsureMigrationsTableAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS schema_migrations (
              name        TEXT PRIMARY KEY,
              applied_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<Dictionary<string, DateTime>> LoadAppliedAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        var result = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = new NpgsqlCommand("SELECT name, applied_at FROM schema_migrations", conn);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            result[r.GetString(0)] = r.GetDateTime(1);
        return result;
    }

    private static IReadOnlyList<(string Name, string ResourceName)> GetScripts()
    {
        var asm = typeof(DbMigrator).Assembly;
        var asmName = asm.GetName().Name; // BiddingBuddy.Bff.Infrastructure
        var fullPrefix = $"{asmName}.{ResourcePrefix}";

        return asm.GetManifestResourceNames()
            .Where(n => n.StartsWith(fullPrefix, StringComparison.Ordinal)
                     && n.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            // Strip the assembly + folder prefix to get the bare file name as the migration key
            .Select(n => (Name: n[fullPrefix.Length..], ResourceName: n))
            .OrderBy(x => x.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static async Task<string> ReadResourceAsync(string resourceName, CancellationToken ct)
    {
        await using var stream = typeof(DbMigrator).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded migration resource not found: {resourceName}");
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(ct);
    }
}
