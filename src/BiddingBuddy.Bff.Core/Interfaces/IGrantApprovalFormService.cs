namespace BiddingBuddy.Bff.Core.Interfaces;

/// <summary>
/// Renders the "Grant Application Approval to Proceed" Word form for a grant application, pre-filled
/// with the opportunity data we already hold (title, agency, deadline, funding figures, cost-share).
/// Phase 1 ships one shared template for every org — see <c>Resources/GrantApprovalForm.Template.docx</c>.
/// </summary>
public interface IGrantApprovalFormService
{
    /// <summary>
    /// Build the approval form for <paramref name="applicationId"/> within <paramref name="orgId"/>.
    /// Throws <see cref="KeyNotFoundException"/> (mapped to 404) when the application is not in this org.
    /// When the application is linked to a source grant, the richer funding fields are joined from
    /// BiddingBuddyServices; a missing grant or an upstream outage degrades to the application's own
    /// snapshot rather than failing the download.
    /// </summary>
    Task<GrantApprovalFormResult> BuildAsync(Guid applicationId, Guid orgId, CancellationToken ct = default);
}

/// <summary>The rendered .docx bytes plus the file name the browser should save it as.</summary>
public record GrantApprovalFormResult(byte[] Content, string FileName);
