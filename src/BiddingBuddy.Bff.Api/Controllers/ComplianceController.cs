using BiddingBuddy.Bff.Core.DTOs.Compliance;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

[ApiController]
[Route("api/compliance")]
[Authorize]
[Produces("application/json")]
public class ComplianceController(IComplianceService complianceService) : BffControllerBase
{
    /// <summary>List all compliance requirements for the org.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ComplianceRequirementDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var reqs = await complianceService.ListRequirementsAsync(CurrentOrgId, ct);
        return Ok(reqs);
    }

    /// <summary>Get a single compliance requirement with its attached documents.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ComplianceRequirementDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var req = await complianceService.GetRequirementAsync(id, CurrentOrgId, ct);
        return Ok(req);
    }

    /// <summary>Create a new compliance requirement for the org.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ComplianceRequirementDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateRequirementDto dto, CancellationToken ct)
    {
        var req = await complianceService.CreateRequirementAsync(CurrentOrgId, dto, ct);
        return CreatedAtAction(nameof(Get), new { id = req.Id }, req);
    }

    /// <summary>Update a compliance requirement's name, description, category or mandatory flag.</summary>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType(typeof(ComplianceRequirementDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateRequirementDto dto, CancellationToken ct)
    {
        var req = await complianceService.UpdateRequirementAsync(id, CurrentOrgId, dto, ct);
        return Ok(req);
    }

    /// <summary>Delete a compliance requirement.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await complianceService.DeleteRequirementAsync(id, CurrentOrgId, ct);
        return NoContent();
    }

    /// <summary>Attach a document vault entry to a compliance requirement.</summary>
    [HttpPost("{id:guid}/documents")]
    [ProducesResponseType(typeof(ComplianceDocumentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AttachDocument(Guid id, [FromBody] AttachComplianceDocumentDto dto, CancellationToken ct)
    {
        var doc = await complianceService.AttachDocumentAsync(id, CurrentOrgId, CurrentUserId, dto, ct);
        return Ok(doc);
    }

    /// <summary>Update a compliance document's status, expiry or notes.</summary>
    [HttpPatch("documents/{complianceDocId:guid}")]
    [ProducesResponseType(typeof(ComplianceDocumentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateDocument(Guid complianceDocId, [FromBody] UpdateComplianceDocumentDto dto, CancellationToken ct)
    {
        var doc = await complianceService.UpdateComplianceDocumentAsync(complianceDocId, CurrentOrgId, CurrentUserId, dto, ct);
        return Ok(doc);
    }

    /// <summary>Detach (remove) a document from a compliance requirement.</summary>
    [HttpDelete("documents/{complianceDocId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DetachDocument(Guid complianceDocId, CancellationToken ct)
    {
        await complianceService.DetachDocumentAsync(complianceDocId, CurrentOrgId, ct);
        return NoContent();
    }
}
