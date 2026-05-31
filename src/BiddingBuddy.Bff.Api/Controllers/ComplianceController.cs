using BiddingBuddy.Bff.Core.DTOs.Compliance;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

[ApiController]
[Route("api/compliance")]
[Authorize]
public class ComplianceController(IComplianceService complianceService) : BffControllerBase
{
    /// <summary>GET /api/compliance</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var reqs = await complianceService.ListRequirementsAsync(CurrentOrgId, ct);
        return Ok(reqs);
    }

    /// <summary>GET /api/compliance/{id}</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var req = await complianceService.GetRequirementAsync(id, CurrentOrgId, ct);
        return Ok(req);
    }

    /// <summary>POST /api/compliance</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRequirementDto dto, CancellationToken ct)
    {
        var req = await complianceService.CreateRequirementAsync(CurrentOrgId, dto, ct);
        return CreatedAtAction(nameof(Get), new { id = req.Id }, req);
    }

    /// <summary>PATCH /api/compliance/{id}</summary>
    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateRequirementDto dto, CancellationToken ct)
    {
        var req = await complianceService.UpdateRequirementAsync(id, CurrentOrgId, dto, ct);
        return Ok(req);
    }

    /// <summary>DELETE /api/compliance/{id}</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await complianceService.DeleteRequirementAsync(id, CurrentOrgId, ct);
        return NoContent();
    }

    /// <summary>POST /api/compliance/{id}/documents</summary>
    [HttpPost("{id:guid}/documents")]
    public async Task<IActionResult> AttachDocument(Guid id, [FromBody] AttachComplianceDocumentDto dto, CancellationToken ct)
    {
        var doc = await complianceService.AttachDocumentAsync(id, CurrentOrgId, CurrentUserId, dto, ct);
        return Ok(doc);
    }

    /// <summary>PATCH /api/compliance/documents/{complianceDocId}</summary>
    [HttpPatch("documents/{complianceDocId:guid}")]
    public async Task<IActionResult> UpdateDocument(Guid complianceDocId, [FromBody] UpdateComplianceDocumentDto dto, CancellationToken ct)
    {
        var doc = await complianceService.UpdateComplianceDocumentAsync(complianceDocId, CurrentOrgId, CurrentUserId, dto, ct);
        return Ok(doc);
    }

    /// <summary>DELETE /api/compliance/documents/{complianceDocId}</summary>
    [HttpDelete("documents/{complianceDocId:guid}")]
    public async Task<IActionResult> DetachDocument(Guid complianceDocId, CancellationToken ct)
    {
        await complianceService.DetachDocumentAsync(complianceDocId, CurrentOrgId, ct);
        return NoContent();
    }
}
