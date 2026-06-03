using Amazon.Runtime;
using Amazon.S3;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Core.Options;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using BiddingBuddy.Bff.Infrastructure.Repositories;
using BiddingBuddy.Bff.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BiddingBuddy.Bff.Infrastructure.Extensions;

public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<BffDbContext>(opt =>
            opt.UseNpgsql(config.GetConnectionString("DefaultConnection"),
                npg => npg.MigrationsAssembly(typeof(BffDbContext).Assembly.FullName)));

        services.AddHttpClient();

        // ── Cloudflare R2 (S3-compatible) ─────────────────────────────────────
        services.Configure<R2Options>(config.GetSection(R2Options.Section));

        services.AddKeyedSingleton<IAmazonS3>("R2", (sp, _) =>
        {
            var r2 = config.GetSection(R2Options.Section).Get<R2Options>()
                ?? throw new InvalidOperationException("R2 configuration section is missing.");

            var s3Config = new AmazonS3Config
            {
                ServiceURL     = r2.Endpoint,
                ForcePathStyle = true,
                AuthenticationRegion = "auto",
            };

            var credentials = new BasicAWSCredentials(r2.AccessKeyId, r2.SecretAccessKey);
            return new AmazonS3Client(credentials, s3Config);
        });

        services.AddScoped<IR2Storage, R2Storage>();

        // Typed HTTP client — BiddingBuddyServices (MongoDB internal API, Basic auth)
        services.AddHttpClient<IBiddingBuddyServicesClient, BiddingBuddyServicesClient>();

        // Repositories
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IOAuthAccountRepository, OAuthAccountRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IOrganizationRepository, OrganizationRepository>();

        // Auth services
        services.AddScoped<TokenService>();
        services.AddScoped<ITokenService>(sp => sp.GetRequiredService<TokenService>());
        services.AddScoped<IOAuthProviderService, OAuthProviderService>();
        services.AddScoped<IAuthService, AuthService>();

        // Domain services
        services.AddScoped<IOrganizationService, OrganizationService>();
        services.AddScoped<ITenderService, TenderService>();
        services.AddScoped<IBidService, BidService>();
        services.AddScoped<IComplianceService, ComplianceService>();
        services.AddScoped<IDocumentService, DocumentService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<ICompetitorService, CompetitorService>();
        services.AddScoped<IAnalysisService, AnalysisService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IGemIntegrationService, GemIntegrationService>();
        services.AddScoped<IInternalPipelineService, InternalPipelineService>();

        return services;
    }
}
