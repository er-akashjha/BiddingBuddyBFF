using BiddingBuddy.Bff.Core.DTOs.Admin;

namespace BiddingBuddy.Bff.Core.Interfaces;

/// <summary>
/// Applies pending SQL schema migrations to the PostgreSQL database on demand.
/// Migrations are embedded .sql scripts, applied in filename order, each recorded
/// in the schema_migrations table so they run at most once.
/// </summary>
public interface IDbMigrator
{
    /// <summary>List every known migration script and whether it has been applied.</summary>
    Task<IReadOnlyList<MigrationStatusDto>> GetStatusAsync(CancellationToken ct = default);

    /// <summary>Run all not-yet-applied migration scripts in order. Idempotent.</summary>
    Task<MigrationRunResultDto> ApplyPendingAsync(CancellationToken ct = default);
}
