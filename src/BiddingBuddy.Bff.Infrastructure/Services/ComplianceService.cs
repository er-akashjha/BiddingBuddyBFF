using BiddingBuddy.Bff.Core.DTOs.Compliance;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BiddingBuddy.Bff.Infrastructure.Services;

public class ComplianceService(BffDbContext db) : IComplianceService
{
    public Task<IReadOnlyList<ComplianceRequirementDto>> ListRequirementsAsync(Guid orgId, CancellationToken ct = default)
        => db.ComplianceRequirements
            .Include(r => r.Documents).ThenInclude(d => d.Document)
            .Where(r => r.OrgId == orgId)
            .OrderBy(r => r.Name)
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<ComplianceRequirementDto>)t.Result.Select(MapRequirement).ToList(), ct);

    public async Task<ComplianceRequirementDto> GetRequirementAsync(Guid requirementId, Guid orgId, CancellationToken ct = default)
    {
        var req = await db.ComplianceRequirements
            .Include(r => r.Documents).ThenInclude(d => d.Document)
            .FirstOrDefaultAsync(r => r.Id == requirementId && r.OrgId == orgId, ct)
            ?? throw new KeyNotFoundException("Compliance requirement not found.");
        return MapRequirement(req);
    }

    public async Task<ComplianceRequirementDto> CreateRequirementAsync(Guid orgId, CreateRequirementDto dto, CancellationToken ct = default)
    {
        var req = new ComplianceRequirement
        {
            OrgId       = orgId,
            Name        = dto.Name,
            Description = dto.Description,
            Category    = dto.Category,
            IsMandatory = dto.IsMandatory,
        };
        db.ComplianceRequirements.Add(req);
        await db.SaveChangesAsync(ct);
        return MapRequirement(req);
    }

    public async Task<ComplianceRequirementDto> UpdateRequirementAsync(Guid requirementId, Guid orgId, UpdateRequirementDto dto, CancellationToken ct = default)
    {
        var req = await db.ComplianceRequirements
            .Include(r => r.Documents).ThenInclude(d => d.Document)
            .FirstOrDefaultAsync(r => r.Id == requirementId && r.OrgId == orgId, ct)
            ?? throw new KeyNotFoundException("Compliance requirement not found.");

        if (dto.Name        is not null) req.Name        = dto.Name;
        if (dto.Description is not null) req.Description = dto.Description;
        if (dto.Category    is not null) req.Category    = dto.Category;
        if (dto.IsMandatory.HasValue)    req.IsMandatory = dto.IsMandatory.Value;

        await db.SaveChangesAsync(ct);
        return MapRequirement(req);
    }

    public async Task DeleteRequirementAsync(Guid requirementId, Guid orgId, CancellationToken ct = default)
    {
        var req = await db.ComplianceRequirements
            .FirstOrDefaultAsync(r => r.Id == requirementId && r.OrgId == orgId, ct)
            ?? throw new KeyNotFoundException("Compliance requirement not found.");
        db.ComplianceRequirements.Remove(req);
        await db.SaveChangesAsync(ct);
    }

    public async Task<ComplianceDocumentDto> AttachDocumentAsync(Guid requirementId, Guid orgId, Guid verifiedBy, AttachComplianceDocumentDto dto, CancellationToken ct = default)
    {
        var req = await db.ComplianceRequirements
            .AnyAsync(r => r.Id == requirementId && r.OrgId == orgId, ct);
        if (!req) throw new KeyNotFoundException("Compliance requirement not found.");

        var compDoc = new ComplianceDocument
        {
            OrgId         = orgId,
            RequirementId = requirementId,
            DocumentId    = dto.DocumentId,
            Status        = "valid",
            ExpiryDate    = dto.ExpiryDate,
            Notes         = dto.Notes,
            VerifiedBy    = verifiedBy,
            VerifiedAt    = DateTime.UtcNow,
        };
        db.ComplianceDocuments.Add(compDoc);
        await db.SaveChangesAsync(ct);

        string? docName = null;
        if (dto.DocumentId.HasValue)
            docName = await db.Documents.Where(d => d.Id == dto.DocumentId).Select(d => d.Name).FirstOrDefaultAsync(ct);

        return MapComplianceDoc(compDoc, docName);
    }

    public async Task<ComplianceDocumentDto> UpdateComplianceDocumentAsync(Guid complianceDocId, Guid orgId, Guid verifiedBy, UpdateComplianceDocumentDto dto, CancellationToken ct = default)
    {
        var compDoc = await db.ComplianceDocuments
            .Include(d => d.Document)
            .FirstOrDefaultAsync(d => d.Id == complianceDocId && d.OrgId == orgId, ct)
            ?? throw new KeyNotFoundException("Compliance document not found.");

        if (dto.DocumentId.HasValue) compDoc.DocumentId = dto.DocumentId;
        if (dto.Status     is not null) compDoc.Status  = dto.Status;
        if (dto.Notes      is not null) compDoc.Notes   = dto.Notes;
        if (dto.ExpiryDate.HasValue)    compDoc.ExpiryDate = dto.ExpiryDate;
        compDoc.VerifiedBy = verifiedBy;
        compDoc.VerifiedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return MapComplianceDoc(compDoc, compDoc.Document?.Name);
    }

    public async Task DetachDocumentAsync(Guid complianceDocId, Guid orgId, CancellationToken ct = default)
    {
        var compDoc = await db.ComplianceDocuments
            .FirstOrDefaultAsync(d => d.Id == complianceDocId && d.OrgId == orgId, ct)
            ?? throw new KeyNotFoundException("Compliance document not found.");
        db.ComplianceDocuments.Remove(compDoc);
        await db.SaveChangesAsync(ct);
    }

    private static ComplianceRequirementDto MapRequirement(ComplianceRequirement r)
        => new(r.Id, r.Name, r.Description, r.Category, r.IsMandatory, r.CreatedAt,
            r.Documents.Select(d => MapComplianceDoc(d, d.Document?.Name)).ToList());

    private static ComplianceDocumentDto MapComplianceDoc(ComplianceDocument d, string? docName)
        => new(d.Id, d.RequirementId, d.DocumentId, docName,
            d.Status, d.ExpiryDate, d.VerifiedBy, d.VerifiedAt, d.Notes, d.UpdatedAt);
}
