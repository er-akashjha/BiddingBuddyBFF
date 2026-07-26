using BiddingBuddy.Bff.Core.DTOs.Tenders;

namespace BiddingBuddy.Bff.Core.Exceptions;

/// <summary>
/// Publication was refused by the compliance engine.
///
/// <para>Carries the full <see cref="ValidationResultDto"/> rather than a message, because the
/// authoring form has to render every finding against the field it belongs to — and each finding
/// carries the citation of the instrument behind it, which is the whole point of the engine. A
/// message string would collapse that back into "publish failed".</para>
///
/// <para><c>BuyerTendersController</c> maps this to <b>422 Unprocessable Entity</b>, not 400: the
/// request is well-formed and was understood, and the tender is simply not yet publishable. Like
/// <see cref="DuplicateOrganizationException"/> it is handled at the controller rather than by
/// <c>GlobalExceptionHandler</c>, which would render it as bare ProblemDetails and drop the
/// findings.</para>
/// </summary>
public sealed class ValidationFailedException(ValidationResultDto result)
    : Exception($"The tender cannot be published: {result.ErrorCount} error(s), {result.WarningCount} warning(s).")
{
    public ValidationResultDto Result { get; } = result;
}
