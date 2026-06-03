using BiddingBuddy.Bff.Api.Controllers;
using BiddingBuddy.Bff.Core.DTOs.Documents;
using BiddingBuddy.Bff.Core.Helpers;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Core.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;

namespace BiddingBuddy.Bff.Tests.Documents;

/// <summary>Unit tests for POST /api/documents/upload-url validation.</summary>
public class PresignValidationTests
{
    private static readonly Guid   TestOrgId  = Guid.NewGuid();
    private static readonly Guid   TestUserId = Guid.NewGuid();

    // ── helpers ──────────────────────────────────────────────────────────────

    private static DocumentsController BuildController(
        IR2Storage? r2 = null,
        R2Options?  cfg = null)
    {
        var docService = new Mock<IDocumentService>();
        var storage    = r2 ?? new Mock<IR2Storage>().Object;
        var opts       = Options.Create(cfg ?? new R2Options
        {
            AccountId        = "test",
            BucketName       = "test-bucket",
            Endpoint         = "https://test.r2.cloudflarestorage.com",
            PresignTtlSeconds = 900,
            MaxUploadSizeKb  = 102_400,
            AccessKeyId      = "key",
            SecretAccessKey  = "secret",
        });

        var ctrl = new DocumentsController(docService.Object, storage, opts);

        // Inject HttpContext so CurrentOrgId works
        var httpCtx = new DefaultHttpContext();
        httpCtx.Items["OrgId"] = TestOrgId;

        var claimsIdentity = new System.Security.Claims.ClaimsIdentity(
            new[] { new System.Security.Claims.Claim("sub", TestUserId.ToString()) },
            "Test");
        httpCtx.User = new System.Security.Claims.ClaimsPrincipal(claimsIdentity);

        ctrl.ControllerContext = new ControllerContext { HttpContext = httpCtx };
        return ctrl;
    }

    // ── fileName validation ───────────────────────────────────────────────────

    [Fact]
    public async Task EmptyFileName_Returns400()
    {
        var ctrl   = BuildController();
        var result = await ctrl.RequestUploadUrl(
            new RequestUploadUrlDto("", "application/pdf", 100), default) as ObjectResult;

        Assert.Equal(400, result?.StatusCode);
    }

    [Fact]
    public async Task FileNameWithPathSeparators_IsSanitized_AndAccepted()
    {
        var presigned = new PresignedUpload("https://url", "key", new Dictionary<string, string>(), DateTime.UtcNow.AddMinutes(15));
        var r2Mock    = new Mock<IR2Storage>();
        r2Mock.Setup(r => r.CreatePresignedPutAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(presigned);

        var ctrl   = BuildController(r2Mock.Object);
        var result = await ctrl.RequestUploadUrl(
            new RequestUploadUrlDto("../../etc/passwd.pdf", "application/pdf", 100), default) as OkObjectResult;

        Assert.NotNull(result);
        var dto = result!.Value as UploadUrlResponseDto;
        // Resulting object key must not contain ".."
        Assert.DoesNotContain("..", dto!.ObjectKey);
    }

    // ── MIME allowlist ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    public async Task AllowedMimeTypes_Return200(string mime)
    {
        var presigned = new PresignedUpload("https://url", "key", new Dictionary<string, string>(), DateTime.UtcNow.AddMinutes(15));
        var r2Mock    = new Mock<IR2Storage>();
        r2Mock.Setup(r => r.CreatePresignedPutAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(presigned);

        var ctrl   = BuildController(r2Mock.Object);
        var result = await ctrl.RequestUploadUrl(
            new RequestUploadUrlDto("file.bin", mime, 100), default);

        Assert.IsType<OkObjectResult>(result);
    }

    [Theory]
    [InlineData("application/x-executable")]
    [InlineData("text/html")]
    [InlineData("application/javascript")]
    [InlineData("application/x-sh")]
    public async Task DisallowedMimeType_Returns400(string mime)
    {
        var ctrl   = BuildController();
        var result = await ctrl.RequestUploadUrl(
            new RequestUploadUrlDto("file.bin", mime, 100), default) as ObjectResult;

        Assert.Equal(400, result?.StatusCode);
    }

    // ── size validation ───────────────────────────────────────────────────────

    [Fact]
    public async Task ZeroSizeKb_Returns400()
    {
        var ctrl   = BuildController();
        var result = await ctrl.RequestUploadUrl(
            new RequestUploadUrlDto("file.pdf", "application/pdf", 0), default) as ObjectResult;

        Assert.Equal(400, result?.StatusCode);
    }

    [Fact]
    public async Task OverMaxSizeKb_Returns400()
    {
        var ctrl   = BuildController();
        var result = await ctrl.RequestUploadUrl(
            new RequestUploadUrlDto("file.pdf", "application/pdf", 999_999), default) as ObjectResult;

        Assert.Equal(400, result?.StatusCode);
    }

    [Fact]
    public async Task AtMaxSizeKb_Returns200()
    {
        var presigned = new PresignedUpload("https://url", "key", new Dictionary<string, string>(), DateTime.UtcNow.AddMinutes(15));
        var r2Mock    = new Mock<IR2Storage>();
        r2Mock.Setup(r => r.CreatePresignedPutAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(presigned);

        var ctrl   = BuildController(r2Mock.Object);
        var result = await ctrl.RequestUploadUrl(
            new RequestUploadUrlDto("file.pdf", "application/pdf", 102_400), default);

        Assert.IsType<OkObjectResult>(result);
    }

    // ── object key construction ───────────────────────────────────────────────

    [Fact]
    public async Task ObjectKey_AlwaysStartsWithOrgPrefix()
    {
        string? capturedKey = null;
        var r2Mock = new Mock<IR2Storage>();
        r2Mock.Setup(r => r.CreatePresignedPutAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
              .Callback<string, string, long, CancellationToken>((key, _, _, _) => capturedKey = key)
              .ReturnsAsync(new PresignedUpload("https://url", "key", new Dictionary<string, string>(), DateTime.UtcNow.AddMinutes(15)));

        var ctrl = BuildController(r2Mock.Object);
        await ctrl.RequestUploadUrl(new RequestUploadUrlDto("report.pdf", "application/pdf", 512), default);

        Assert.NotNull(capturedKey);
        Assert.StartsWith($"orgs/{TestOrgId}/docs/", capturedKey);
    }

    [Fact]
    public async Task ObjectKey_ContainsSanitizedFileName()
    {
        string? capturedKey = null;
        var r2Mock = new Mock<IR2Storage>();
        r2Mock.Setup(r => r.CreatePresignedPutAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
              .Callback<string, string, long, CancellationToken>((key, _, _, _) => capturedKey = key)
              .ReturnsAsync(new PresignedUpload("https://url", "key", new Dictionary<string, string>(), DateTime.UtcNow.AddMinutes(15)));

        var ctrl = BuildController(r2Mock.Object);
        await ctrl.RequestUploadUrl(new RequestUploadUrlDto("my report (v2).pdf", "application/pdf", 512), default);

        Assert.NotNull(capturedKey);
        Assert.EndsWith(".pdf", capturedKey);
    }

    // ── SanitizeFileName unit tests ───────────────────────────────────────────

    [Theory]
    [InlineData("report.pdf",               "report.pdf")]
    [InlineData("../../etc/passwd",          "passwd")]
    [InlineData("C:\\Windows\\system32.dll", "system32.dll")]
    [InlineData("file\x00name.pdf",          "filename.pdf")]
    [InlineData("my file (v2).pdf",          "my file _v2_.pdf")]
    public void SanitizeFileName_ProducesExpectedOutput(string input, string expected)
    {
        var result = FileNameSanitizer.Sanitize(input);
        Assert.Equal(expected, result);
    }
}
