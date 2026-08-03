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

        // ── AWS S3 — scraped tender files (separate bucket + credentials from R2) ──
        services.Configure<TenderS3Options>(config.GetSection(TenderS3Options.Section));

        services.AddKeyedSingleton<IAmazonS3>("TenderS3", (sp, _) =>
        {
            var cfg = config.GetSection(TenderS3Options.Section).Get<TenderS3Options>()
                ?? throw new InvalidOperationException("TenderS3 configuration section is missing.");

            var s3Config = new AmazonS3Config
            {
                RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(cfg.Region),
            };

            // Fall back to the default AWS credential chain (env / instance role) when
            // explicit keys aren't configured — handy on EC2 with an instance profile.
            return string.IsNullOrWhiteSpace(cfg.AccessKeyId)
                ? new AmazonS3Client(s3Config)
                : new AmazonS3Client(new BasicAWSCredentials(cfg.AccessKeyId, cfg.SecretAccessKey), s3Config);
        });

        services.AddScoped<ITenderFileStorage, TenderFileStorage>();

        // Forwards the ambient correlation id as X-Correlation-Id on outbound calls.
        services.AddTransient<Logging.CorrelationHeaderHandler>();

        // Typed HTTP client — BiddingBuddyServices (MongoDB internal API, Basic auth)
        services.AddHttpClient<IBiddingBuddyServicesClient, BiddingBuddyServicesClient>()
            .AddHttpMessageHandler<Logging.CorrelationHeaderHandler>();

        // Repositories
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IOAuthAccountRepository, OAuthAccountRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IOrganizationRepository, OrganizationRepository>();

        // Auth services
        services.AddScoped<TokenService>();
        services.AddScoped<ITokenService>(sp => sp.GetRequiredService<TokenService>());
        services.AddScoped<IOAuthProviderService, OAuthProviderService>();
        // Singleton so the Apple JWKS cache is shared across requests.
        services.AddSingleton<IAppleTokenVerifier, AppleTokenVerifier>();
        // Same reasoning for Entra's JWKS — a per-request instance would re-fetch Microsoft's key
        // set on every single sign-in, turning a 24 h cache into no cache at all.
        services.AddSingleton<IMicrosoftTokenVerifier, MicrosoftTokenVerifier>();
        services.AddScoped<IAuthService, AuthService>();

        // Domain services
        services.AddScoped<IOrganizationService, OrganizationService>();
        services.AddScoped<IJoinRequestService, JoinRequestService>();
        // Inbound path to buyer status: org requests, operator approves (reuses SetOrgTypeAsync).
        services.AddScoped<IBuyerRequestService, BuyerRequestService>();
        services.AddScoped<ISsoService, SsoService>();
        services.AddScoped<ITenderService, TenderService>();
        // Buyer-side tendering: a department authors a tender here rather than us scraping one.
        services.AddScoped<IBuyerTenderService, BuyerTenderService>();
        services.AddScoped<ISavedFilterService, SavedFilterService>();
        services.AddScoped<IBidService, BidService>();
        services.AddScoped<IBidAttachmentService, BidAttachmentService>();
        services.AddScoped<IBidDocumentService, BidDocumentService>();
        services.AddScoped<IBidEmdService, BidEmdService>();
        services.AddScoped<IComplianceService, ComplianceService>();
        services.AddScoped<IDocumentService, DocumentService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<ICompetitorService, CompetitorService>();
        services.AddScoped<IAnalysisService, AnalysisService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IDeviceService, DeviceService>();
        services.AddScoped<IGemIntegrationService, GemIntegrationService>();
        services.AddScoped<ITenderAlertRuleService, TenderAlertRuleService>();
        services.AddScoped<IMatchingService, MatchingService>();
        services.Configure<MatchingScanOptions>(config.GetSection(MatchingScanOptions.Section));
        services.AddScoped<IInternalPipelineService, InternalPipelineService>();

        // GRANT product line
        services.AddScoped<IInternalGrantPipelineService, InternalGrantPipelineService>();
        // Same concrete client as IBiddingBuddyServicesClient so both share one cached JWT.
        services.AddScoped<IGrantServicesClient>(sp =>
            (BiddingBuddyServicesClient)sp.GetRequiredService<IBiddingBuddyServicesClient>());
        // Grant pursuit lifecycle (org-scoped): saved grants + applications + proposal authoring.
        services.AddScoped<ISavedGrantService, SavedGrantService>();
        services.AddScoped<IGrantApplicationService, GrantApplicationService>();
        // Pre-filled "Approval to Proceed" Word form, downloaded when a grant enters the pipeline.
        services.AddScoped<IGrantApprovalFormService, GrantApprovalFormService>();

        // Deadline / expiry reminder scan (bids, invoices, compliance, delivery, EMD)
        services.AddScoped<INotificationAudienceResolver, NotificationAudienceResolver>();
        services.AddScoped<IDeadlineScanService, DeadlineScanService>();
        services.Configure<DeadlineScanOptions>(config.GetSection(DeadlineScanOptions.Section));

        // Weekly org-summary digest
        services.AddScoped<IWeeklyDigestService, WeeklyDigestService>();
        services.Configure<WeeklyDigestOptions>(config.GetSection(WeeklyDigestOptions.Section));

        // ── Subscription billing ─────────────────────────────────────────────
        services.AddScoped<IPlanService, PlanService>();
        services.AddScoped<IAiQuotaService, AiQuotaService>();
        services.Configure<RazorpayOptions>(config.GetSection(RazorpayOptions.Section));
        services.AddHttpClient<IRazorpayClient, RazorpayClient>()
            .AddHttpMessageHandler<Logging.CorrelationHeaderHandler>();
        services.AddScoped<IPromoService, PromoService>();
        services.AddScoped<IBillingService, BillingService>();
        services.AddScoped<ISubscriptionLifecycleService, SubscriptionLifecycleService>();
        services.Configure<SubscriptionLifecycleOptions>(
            config.GetSection(SubscriptionLifecycleOptions.Section));

        // Schema migrator (runs embedded SQL scripts via /internal/migrations)
        services.AddScoped<IDbMigrator, DbMigrator>();

        // ── Notification subsystem ───────────────────────────────────────────
        services.Configure<RabbitMqOptions>(config.GetSection(RabbitMqOptions.Section));
        services.AddSingleton<IRabbitMqPublisher, RabbitMqPublisher>();
        services.AddScoped<INotificationPublisher, NotificationPublisher>();
        services.AddScoped<INotificationTemplateService, NotificationTemplateService>();

        return services;
    }
}
