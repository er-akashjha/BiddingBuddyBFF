using BiddingBuddy.Bff.Api.Filters;
using BiddingBuddy.Bff.Core.DTOs.Internal;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

/// <summary>
/// Internal endpoints called by the BidProcessor pipeline.
/// Protected by X-Api-Key header (Pipeline:ApiKey config). Not for client use.
/// </summary>
[ApiController]
[Route("internal")]
[PipelineApiKey]
[Produces("application/json")]
public class InternalController(IInternalPipelineService pipelineService) : ControllerBase
{
    /// <summary>Upsert a tender from pipeline enrichment. Returns 201 on create, 200 on update.</summary>
    [HttpPost("tenders")]
    [ProducesResponseType(typeof(UpsertTenderResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(UpsertTenderResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpsertTender([FromBody] UpsertTenderDto dto, CancellationToken ct)
    {
        var result = await pipelineService.UpsertTenderAsync(dto, ct);
        return result.Created ? StatusCode(201, result) : Ok(result);
    }

    /// <summary>Store extracted text content from a tender document (PDF).</summary>
    [HttpPost("tenders/{gemTenderId}/documents")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpsertDocument(string gemTenderId, [FromBody] UpsertDocumentContentDto dto, CancellationToken ct)
    {
        await pipelineService.UpsertDocumentContentAsync(gemTenderId, dto, ct);
        return NoContent();
    }

    /// <summary>Upsert a competitor record from pipeline bid observations.</summary>
    [HttpPost("competitors")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpsertCompetitor([FromBody] UpsertCompetitorDto dto, CancellationToken ct)
    {
        await pipelineService.UpsertCompetitorAsync(dto, ct);
        return NoContent();
    }

    /// <summary>Store AI analysis result for a tender (eligibility, risk factors, win strategy).</summary>
    [HttpPost("analysis")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpsertAnalysis([FromBody] UpsertAiAnalysisDto dto, CancellationToken ct)
    {
        await pipelineService.UpsertAiAnalysisAsync(dto, ct);
        return NoContent();
    }
}
