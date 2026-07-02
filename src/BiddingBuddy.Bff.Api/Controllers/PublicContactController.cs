using System.Net.Mail;
using BiddingBuddy.Bff.Core.DTOs;
using BiddingBuddy.Bff.Core.DTOs.Notifications;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BiddingBuddy.Bff.Api.Controllers;

/// <summary>
/// Anonymous "Contact us" endpoint for the marketing site. Forwards the form as
/// an email to the team inbox (config <c>ContactForm:Recipient</c>) through the
/// existing notification pipeline — INotificationPublisher inserts the rows and
/// publishes a RabbitMQ trigger; the BidProcessor's email worker renders the
/// <c>CONTACT_FORM</c> template and sends it. No JWT, IP rate-limited to deter abuse.
/// </summary>
[ApiController]
[Route("api/public/contact")]
[AllowAnonymous]
[EnableRateLimiting("public")]
[Produces("application/json")]
public class PublicContactController(
    INotificationPublisher publisher,
    IConfiguration config,
    ILogger<PublicContactController> logger) : ControllerBase
{
    private const string TemplateCode = "CONTACT_FORM";
    private const string DefaultRecipient = "a4akashjha@gmail.com";

    /// <summary>Submit the contact form. 202 on accept, 400 on a validation error.</summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Submit([FromBody] ContactFormDto form, CancellationToken ct)
    {
        if (form is null)
            return BadRequest(new { error = "Request body is required." });

        var name    = form.Name?.Trim()    ?? string.Empty;
        var email   = form.Email?.Trim()   ?? string.Empty;
        var company = form.Company?.Trim() ?? string.Empty;
        var message = form.Message?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(message))
            return BadRequest(new { error = "Name, email and message are required." });

        if (!IsValidEmail(email))
            return BadRequest(new { error = "Please provide a valid email address." });

        // Guard the rendered subject/body and the notifications row against oversized input.
        if (name.Length > 200)     name    = name[..200];
        if (company.Length > 200)  company = company[..200];
        if (message.Length > 5000) message = message[..5000];

        var recipient = config["ContactForm:Recipient"] ?? DefaultRecipient;

        try
        {
            await publisher.SendAsync(new SendNotificationDto(
                Category:     NotificationCategory.Information,
                TemplateCode: TemplateCode,
                UserId:       null,                       // anonymous visitor — no user
                Payload:      new Dictionary<string, object>
                {
                    ["Name"]        = name,
                    ["Email"]       = email,
                    ["Company"]     = string.IsNullOrWhiteSpace(company) ? "—" : company,
                    ["Message"]     = message,
                    ["SubmittedAt"] = DateTime.UtcNow.ToString("dd MMM yyyy HH:mm 'UTC'"),
                },
                Recipients:   new[]
                {
                    new NotificationRecipientDto(NotificationChannel.Email, recipient),
                }), ct);
        }
        catch (Exception ex)
        {
            // The insert-then-publish failed (e.g. Postgres unreachable). Surface a
            // failure so the UI can ask the visitor to retry rather than silently dropping.
            logger.LogError(ex, "[ContactForm] Failed to enqueue contact message from {Email}", email);
            return StatusCode(StatusCodes.Status502BadGateway,
                new { error = "We couldn't send your message right now. Please try again shortly." });
        }

        logger.LogInformation("[ContactForm] Accepted contact message from {Email} → {Recipient}", email, recipient);
        return Accepted();
    }

    private static bool IsValidEmail(string email)
    {
        if (email.Length > 320) return false;
        try
        {
            var addr = new MailAddress(email);
            return addr.Address == email;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
