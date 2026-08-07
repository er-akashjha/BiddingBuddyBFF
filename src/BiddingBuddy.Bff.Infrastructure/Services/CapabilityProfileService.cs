using BiddingBuddy.Bff.Core.DTOs.Capability;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BiddingBuddy.Bff.Infrastructure.Services;

public class CapabilityProfileService(BffDbContext db) : ICapabilityProfileService
{
    public async Task<CapabilityProfileDto> GetAsync(Guid orgId, CancellationToken ct = default)
    {
        var profile = await db.OrgCapabilityProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.OrgId == orgId, ct);
        var credentialCount = await db.OrgCredentials.CountAsync(c => c.OrgId == orgId, ct);
        return ToDto(profile, credentialCount);
    }

    public async Task<CapabilityProfileDto> UpdateAsync(
        Guid orgId, Guid userId, UpdateCapabilityProfileDto dto, CancellationToken ct = default)
    {
        var profile = await db.OrgCapabilityProfiles.FirstOrDefaultAsync(p => p.OrgId == orgId, ct);
        if (profile is null)
        {
            profile = new OrgCapabilityProfile { OrgId = orgId };
            db.OrgCapabilityProfiles.Add(profile);
        }

        // A PUT of the whole form: every field is assigned, including to null. Patch semantics
        // (`?? existing`) would make clearing a wrong turnover figure impossible from the UI,
        // and a stale turnover is worse than an absent one — absent produces "we can't say",
        // stale produces a confident wrong verdict.
        profile.TurnoverFy1        = dto.TurnoverFy1;
        profile.TurnoverFy2        = dto.TurnoverFy2;
        profile.TurnoverFy3        = dto.TurnoverFy3;
        profile.TurnoverFy1Label   = Trim(dto.TurnoverFy1Label);
        profile.NetWorth           = dto.NetWorth;
        profile.IncorporationDate  = dto.IncorporationDate;
        profile.UdyamNumber        = Trim(dto.UdyamNumber);
        profile.UdyamCategory      = Trim(dto.UdyamCategory)?.ToLowerInvariant();
        profile.DpiitStartupNumber = Trim(dto.DpiitStartupNumber);
        profile.NsicNumber         = Trim(dto.NsicNumber);
        profile.ServiceableStates  = Clean(dto.ServiceableStates);
        profile.CategoriesSupplied = Clean(dto.CategoriesSupplied);
        profile.EmdHeadroom        = dto.EmdHeadroom;
        profile.BgLimit            = dto.BgLimit;
        profile.BgUtilised         = dto.BgUtilised;
        profile.UpdatedBy          = userId;
        profile.UpdatedAt          = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        var credentialCount = await db.OrgCredentials.CountAsync(c => c.OrgId == orgId, ct);
        return ToDto(profile, credentialCount);
    }

    public async Task<IReadOnlyList<CredentialDto>> ListCredentialsAsync(
        Guid orgId, CancellationToken ct = default)
    {
        var rows = await db.OrgCredentials.AsNoTracking()
            .Where(c => c.OrgId == orgId)
            .OrderBy(c => c.Kind).ThenBy(c => c.Code)
            .Select(c => new { c, DocumentName = c.Document != null ? c.Document.Name : null })
            .ToListAsync(ct);

        var today = Today();
        return rows.Select(r => ToDto(r.c, r.DocumentName, today)).ToList();
    }

    public async Task<CredentialDto> UpsertCredentialAsync(
        Guid orgId, Guid userId, UpsertCredentialDto dto, CancellationToken ct = default)
    {
        var kind = Trim(dto.Kind)?.ToLowerInvariant();
        if (kind is null || !CredentialKinds.All.Contains(kind))
            throw new ArgumentException(
                $"Unknown credential kind '{dto.Kind}'. Expected one of: {string.Join(", ", CredentialKinds.All)}.");

        // Upper-cased so matching a tender's required list is case-stable. The tender side
        // normalises the same way; if only one side did, every comparison would silently miss.
        var code = Trim(dto.Code)?.ToUpperInvariant()
            ?? throw new ArgumentException("Credential code is required.");

        // The document, when supplied, must belong to THIS org — otherwise a guessed id would
        // link one tenant's certificate to another's profile.
        if (dto.DocumentId is Guid docId &&
            !await db.Documents.AnyAsync(d => d.Id == docId && d.OrgId == orgId, ct))
            throw new KeyNotFoundException("Document not found in this organization.");

        var existing = await db.OrgCredentials
            .FirstOrDefaultAsync(c => c.OrgId == orgId && c.Kind == kind && c.Code == code, ct);

        if (existing is null)
        {
            existing = new OrgCredential
            {
                OrgId     = orgId,
                Kind      = kind,
                Code      = code,
                CreatedBy = userId,
            };
            db.OrgCredentials.Add(existing);
        }

        existing.Label      = Trim(dto.Label);
        existing.Number     = Trim(dto.Number);
        existing.IssuedAt   = dto.IssuedAt;
        existing.ValidUntil = dto.ValidUntil;
        existing.DocumentId = dto.DocumentId;
        existing.Notes      = Trim(dto.Notes);
        existing.UpdatedAt  = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        var documentName = existing.DocumentId is null
            ? null
            : await db.Documents.Where(d => d.Id == existing.DocumentId)
                .Select(d => d.Name).FirstOrDefaultAsync(ct);

        return ToDto(existing, documentName, Today());
    }

    public async Task DeleteCredentialAsync(Guid orgId, Guid credentialId, CancellationToken ct = default)
    {
        var row = await db.OrgCredentials
            .FirstOrDefaultAsync(c => c.Id == credentialId && c.OrgId == orgId, ct)
            ?? throw new KeyNotFoundException("Credential not found.");
        db.OrgCredentials.Remove(row);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<CredentialSuggestionDto>> SuggestFromDocumentsAsync(
        Guid orgId, CancellationToken ct = default)
    {
        var documents = await db.Documents.AsNoTracking()
            .Where(d => d.OrgId == orgId)
            .Select(d => new { d.Id, d.Name, d.DocumentType, d.ExpiryDate, d.Tags })
            .ToListAsync(ct);

        var held = await db.OrgCredentials.AsNoTracking()
            .Where(c => c.OrgId == orgId)
            .Select(c => c.Kind + "|" + c.Code)
            .ToListAsync(ct);
        var heldSet = held.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var suggestions = new List<CredentialSuggestionDto>();
        foreach (var d in documents)
        {
            var guess = GuessCredential(d.DocumentType, d.Name, d.Tags);
            if (guess is null) continue;
            var (kind, code, label) = guess.Value;
            if (!heldSet.Add($"{kind}|{code}")) continue;   // already recorded, or already suggested

            suggestions.Add(new CredentialSuggestionDto(
                kind, code, label, d.ExpiryDate, d.Id, d.Name,
                Because: $"Matched from your uploaded document \"{d.Name}\""
                       + (string.IsNullOrWhiteSpace(d.DocumentType) ? "" : $" (type: {d.DocumentType})")));
        }

        return suggestions;
    }

    public async Task<(OrgCapabilityProfile? Profile, IReadOnlyList<OrgCredential> Credentials)> GetOperandsAsync(
        Guid orgId, CancellationToken ct = default)
    {
        var profile = await db.OrgCapabilityProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.OrgId == orgId, ct);
        var credentials = await db.OrgCredentials.AsNoTracking()
            .Where(c => c.OrgId == orgId)
            .ToListAsync(ct);
        return (profile, credentials);
    }

    // ── Suggestion heuristics ───────────────────────────────────────────────

    /// <summary>
    /// Infer a credential from a vault document. Deliberately narrow: it only fires on the
    /// document-type vocabulary the upload dialog offers plus a few unmistakable name patterns.
    /// A wrong suggestion costs a user one dismissal; a wrong *recorded* credential would make
    /// the fit engine assert something false, which is why nothing here writes.
    /// </summary>
    private static (string Kind, string Code, string Label)? GuessCredential(
        string? documentType, string name, string[]? tags)
    {
        var haystack = string.Join(' ', new[] { documentType, name }
            .Concat(tags ?? []).Where(s => !string.IsNullOrWhiteSpace(s))).ToUpperInvariant();

        // ISO standards carry their number in the name almost without exception, and the number
        // is what a tender asks for — "ISO certification" alone is not matchable.
        foreach (var iso in IsoStandards)
            if (haystack.Contains(iso, StringComparison.Ordinal))
                return (CredentialKinds.Certification, iso, $"ISO {iso[4..]}");

        if (Mentions(haystack, "MSME", "UDYAM"))
            return (CredentialKinds.Registration, "UDYAM", "Udyam (MSME) registration");
        if (Mentions(haystack, "GST"))
            return (CredentialKinds.Registration, "GST", "GST registration");
        if (Mentions(haystack, "NSIC"))
            return (CredentialKinds.Registration, "NSIC", "NSIC registration");
        if (Mentions(haystack, "DPIIT", "STARTUP INDIA"))
            return (CredentialKinds.Registration, "DPIIT", "DPIIT startup recognition");
        if (Mentions(haystack, "OEM"))
            // The brand is not reliably recoverable from a filename, so this is left generic and
            // the user names it. Guessing "DELL" from "oem_letter.pdf" would be a fabrication.
            return (CredentialKinds.OemAuthorization, "OEM", "OEM authorization letter");

        return null;
    }

    private static readonly string[] IsoStandards =
    [
        "ISO 9001", "ISO 14001", "ISO 27001", "ISO 45001", "ISO 13485", "ISO 22000",
    ];

    private static bool Mentions(string haystack, params string[] needles) =>
        needles.Any(n => haystack.Contains(n, StringComparison.Ordinal));

    // ── Mapping ─────────────────────────────────────────────────────────────

    private static CapabilityProfileDto ToDto(OrgCapabilityProfile? p, int credentialCount)
    {
        var missing = new List<string>();

        // Turnover first because it is the single most common eligibility bar in Indian public
        // procurement — without it the engine cannot answer the question users most often ask.
        if (p?.TurnoverFy1 is null) missing.Add("Last completed financial year's turnover");
        if (p?.IncorporationDate is null) missing.Add("Date of incorporation (drives years-of-experience checks)");
        if (credentialCount == 0) missing.Add("At least one certificate or registration");
        if (p?.UdyamNumber is null && p?.DpiitStartupNumber is null)
            missing.Add("Udyam or DPIIT number, if you hold one (unlocks MSE/startup relaxations)");
        if (p?.EmdHeadroom is null) missing.Add("EMD headroom (how much you can block per bid)");
        if (p?.ServiceableStates is not { Length: > 0 }) missing.Add("States you can serve");

        // Six inputs, weighted equally. A finer weighting would imply a precision this doesn't
        // have; the list above is the actionable part, the percent is just the nudge.
        const int total = 6;
        var percent = (int)Math.Round((total - missing.Count) * 100.0 / total);

        return new CapabilityProfileDto(
            p?.TurnoverFy1, p?.TurnoverFy2, p?.TurnoverFy3, p?.TurnoverFy1Label,
            p?.NetWorth, p?.IncorporationDate,
            p?.UdyamNumber, p?.UdyamCategory, p?.DpiitStartupNumber, p?.NsicNumber,
            p?.ServiceableStates ?? [], p?.CategoriesSupplied ?? [],
            p?.EmdHeadroom, p?.BgLimit, p?.BgUtilised,
            p?.UpdatedAt,
            new CompletenessDto(
                percent,
                // The bar for a real verdict is deliberately low. Turnover alone lets the engine
                // answer the most common blocker; demanding a complete profile before saying
                // anything would leave the paid tab empty for almost everyone.
                CanEvaluate: p?.TurnoverFy1 is not null || credentialCount > 0,
                missing));
    }

    private static CredentialDto ToDto(OrgCredential c, string? documentName, DateOnly today) =>
        new(c.Id, c.Kind, c.Code, c.Label, c.Number, c.IssuedAt, c.ValidUntil,
            c.DocumentId, documentName, c.VerifiedAt, c.Notes,
            IsExpired: c.ExpiresBy(today),
            DaysUntilExpiry: c.ValidUntil is null ? null : c.ValidUntil.Value.DayNumber - today.DayNumber,
            c.CreatedAt);

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string[]? Clean(string[]? values) =>
        values is null ? null
        : values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>Expiry is judged in IST — the same calendar the quota month and every bid
    /// deadline in this product use. UTC "today" is a day behind for five and a half hours
    /// every night, which is enough to call a live certificate expired.</summary>
    private static DateOnly Today() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IstZone));

    private static readonly TimeZoneInfo IstZone = ResolveIst();

    private static TimeZoneInfo ResolveIst()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"); }
    }
}
