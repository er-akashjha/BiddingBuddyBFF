namespace BiddingBuddy.Bff.Core.DTOs.Admin;

/// <summary>One migration script and whether it has been applied to the database.</summary>
public record MigrationStatusDto(
    string Name,
    bool   Applied,
    DateTime? AppliedAt
);

/// <summary>Outcome of running pending migrations.</summary>
public record MigrationRunResultDto(
    IReadOnlyList<string> Applied,        // scripts executed during this run
    IReadOnlyList<string> AlreadyApplied, // scripts skipped because already recorded
    int TotalScripts
);
