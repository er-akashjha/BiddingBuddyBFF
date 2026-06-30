namespace BiddingBuddy.Bff.Core.DTOs;

/// <summary>
/// Marketing "Contact us" form submission from an anonymous site visitor.
/// Delivered as an email to the team inbox via the notification pipeline
/// (template <c>CONTACT_FORM</c>). No auth — see PublicContactController.
/// </summary>
public record ContactFormDto(
    string Name,
    string Email,
    string? Company,
    string Message
);
