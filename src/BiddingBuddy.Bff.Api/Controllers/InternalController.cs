using BiddingBuddy.Bff.Api.Filters;
using BiddingBuddy.Bff.Core.DTOs.Internal;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

/// <summary>
/// Internal endpoints called by the BidProcessor pipeline.
/// Protected by X-Api-Key header (Pipeline:ApiKey config).
/// </summary>
[ApiController]
[Route("internal")]
[PipelineApiKey]
public class InternalController(IInternalPipelineService pipelineService) : ControllerBase
{
    /// <summary>POST /internal/tenders — upsert tender from pipeline enrichment</summary>
    [HttpPost("tenders")]
    public async Task<IActionResult> UpsertTender([FromBody] UpsertTenderDto dto, CancellationToken ct)
    {
        var result = await pipelineService.UpsertTenderAsync(dto, ct);
        return result.Created
            ? StatusCode(201, result)
            : Ok(result);
    }

    /// <summary>POST /internal/tenders/{gemTenderId}/documents — store extracted document content</summary>
    [HttpPost("tenders/{gemTenderId}/documents")]
    public async Task<IActionResult> UpsertDocument(string gemTenderId, [FromBody] UpsertDocumentContentDto dto, CancellationToken ct)
    {
        await pipelineService.UpsertDocumentContentAsync(gemTenderId, dto, ct);
        return NoContent();
    }

    /// <summary>POST /internal/competitors — upsert competitor from pipeline observation</summary>
    [HttpPost("competitors")]
    public async Task<IActionResult> UpsertCompetitor([FromBody] UpsertCompetitorDto dto, CancellationToken ct)
    {
        await pipelineService.UpsertCompetitorAsync(dto, ct);
        return NoContent();
    }

    /// <summary>POST /internal/analysis — store AI analysis result for a tender</summary>
    [HttpPost("analysis")]
    public async Task<IActionResult> UpsertAnalysis([FromBody] UpsertAiAnalysisDto dto, CancellationToken ct)
    {
        await pipelineService.UpsertAiAnalysisAsync(dto, ct);
        return NoContent();
    }
}
