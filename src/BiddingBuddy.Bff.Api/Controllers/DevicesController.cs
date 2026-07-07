using BiddingBuddy.Bff.Core.DTOs.Devices;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

/// <summary>
/// Push device registry for the mobile app. Org-agnostic — a device belongs to a user,
/// not an org (exempt from OrgContextMiddleware; JWT only). The app registers on every
/// launch and on FCM-token rotation, and unregisters on logout.
/// </summary>
[ApiController]
[Route("api/devices")]
[Authorize]
[Produces("application/json")]
public class DevicesController(IDeviceService devices) : BffControllerBase
{
    /// <summary>Register/refresh the caller's push device (idempotent on the FCM token).</summary>
    [HttpPost]
    [ProducesResponseType(typeof(DeviceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register([FromBody] RegisterDeviceDto dto, CancellationToken ct)
    {
        try
        {
            return Ok(await devices.RegisterAsync(CurrentUserId, dto, ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>List the caller's active devices.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<DeviceDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await devices.ListAsync(CurrentUserId, ct));

    /// <summary>Toggle the per-device push switch (the app's "Push notifications" setting).</summary>
    [HttpPatch("push")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetPush([FromBody] SetDevicePushDto dto, CancellationToken ct)
    {
        await devices.SetPushEnabledAsync(CurrentUserId, dto.FcmToken, dto.Enabled, ct);
        return NoContent();
    }

    /// <summary>Unregister a device (logout).</summary>
    [HttpDelete("{fcmToken}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Unregister(string fcmToken, CancellationToken ct)
    {
        await devices.UnregisterAsync(CurrentUserId, fcmToken, ct);
        return NoContent();
    }
}
