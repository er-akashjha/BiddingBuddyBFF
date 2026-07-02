using System.Text;
using BiddingBuddy.Bff.Api.Middleware;
using BiddingBuddy.Bff.Api.Swagger;
using BiddingBuddy.Bff.Core.Extensions;
using BiddingBuddy.Bff.Infrastructure.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Threading.RateLimiting;
using Serilog;
using Serilog.Events;

// ── Bootstrap logger (captures errors before full config is ready) ────────────
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("[BiddingBuddyBFF] Starting up…");

var builder = WebApplication.CreateBuilder(args);

// ── Serilog ─────────────────────────────────────────────────────────────────
// Shared console + file template across all five services so a single Loki
// `pattern` parser can extract level / app / correlation id from every line.
builder.Host.UseSerilog((ctx, _, cfg) =>
{
    var logPath = ctx.Configuration["Logging:FilePath"]
                  ?? "logs/biddingbuddy/pipeline-.log";
    cfg
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .MinimumLevel.Override("System",    LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("ApplicationName", "BiddingBuddyBFF")
        .Enrich.WithProperty("MachineName", Environment.MachineName)
        .WriteTo.Console(
            outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] [{ApplicationName}] [cid:{CorrelationId,-36}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.File(
            path: logPath,
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30,
            shared: true,
            outputTemplate:
                "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] [{ApplicationName}] [cid:{CorrelationId}] {Message:lj}{NewLine}{Exception}");
});

// ── Domain + Infrastructure ───────────────────────────────────────────────────
builder.Services
    .AddCoreServices()
    .AddInfrastructure(builder.Configuration);

// Scheduled tender-alert scan — turns newly-added tenders into per-org digest emails.
builder.Services.AddHostedService<BiddingBuddy.Bff.Api.Workers.TenderMatchScanWorker>();

// Scheduled deadline/expiry scan — turns approaching/passed dates (bids, invoices,
// compliance, delivery, EMD) into one-time in-app + email reminders.
builder.Services.AddHostedService<BiddingBuddy.Bff.Api.Workers.DeadlineScanWorker>();

// Weekly org-summary digest (open bids, due-this-week, overdue, won) → owners + admins.
builder.Services.AddHostedService<BiddingBuddy.Bff.Api.Workers.WeeklyDigestWorker>();

// ── JWT Authentication ────────────────────────────────────────────────────────
var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret is not configured.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = builder.Configuration["Jwt:Issuer"],
            ValidAudience            = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ClockSkew                = TimeSpan.FromSeconds(30),
            NameClaimType            = "sub",
        };
    });

builder.Services.AddAuthorization();

// ── Rate limiting ───────────────────────────────────────────────────────────────
// Protects the anonymous /api/public/* endpoints from scraping. Each public call
// fans out to BiddingBuddyServices on a shared service token, so an unthrottled
// open endpoint is an abuse risk. Fixed window, partitioned by client IP.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("public", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window      = TimeSpan.FromMinutes(1),
                QueueLimit  = 0,
            }));
});

// ── CORS ──────────────────────────────────────────────────────────────────────
var frontendUrl = builder.Configuration["Frontend:BaseUrl"] ?? "http://localhost:3000";
builder.Services.AddCors(opt =>
    opt.AddPolicy("AllowFrontend", policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            // Allow any localhost port in dev — avoids restarting BFF when frontend port changes
            policy.SetIsOriginAllowed(origin =>
                       Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
                       (uri.Host == "localhost" || uri.Host == "127.0.0.1"))
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        }
        else
        {
            policy.WithOrigins(frontendUrl)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        }
    }));

// ── Controllers + Swagger ─────────────────────────────────────────────────────
builder.Services.AddMemoryCache();   // backs the dynamic sitemap cache
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title       = "BiddingBuddy BFF",
        Version     = "v1",
        Description = "Backend for Frontend — Government Tender Management SaaS"
    });

    var securityScheme = new OpenApiSecurityScheme
    {
        Name         = "Authorization",
        Type         = SecuritySchemeType.Http,
        Scheme       = "bearer",
        BearerFormat = "JWT",
        In           = ParameterLocation.Header,
    };
    c.AddSecurityDefinition("Bearer", securityScheme);
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });

    // Inject X-Org-Id on org-scoped routes and X-Api-Key on /internal/* routes
    c.OperationFilter<OrgIdHeaderFilter>();
    c.OperationFilter<ApiKeyHeaderFilter>();

    // Wire up XML doc comments so Swagger shows descriptions and response schemas
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
        c.IncludeXmlComments(xmlPath);
});

// ── Exception handling ────────────────────────────────────────────────────────
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<BiddingBuddy.Bff.Api.Middleware.GlobalExceptionHandler>();

// ── Build ─────────────────────────────────────────────────────────────────────
var app = builder.Build();

// Correlation id must come first — before anything else logs — so every entry
// produced during the request (and the request-logging summary below) carries it.
app.UseMiddleware<CorrelationIdMiddleware>();

// One structured line per request: method, path, status, elapsed ms. This alone
// surfaces the 4xx/5xx that were previously invisible.
app.UseSerilogRequestLogging();

// Swagger available in all environments (restrict via reverse proxy in prod if needed)
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "BiddingBuddy BFF v1");
    c.RoutePrefix        = "swagger";
    c.DisplayRequestDuration();
    c.DefaultModelsExpandDepth(-1);   // collapse schemas by default — less noise
});

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseCors("AllowFrontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<OrgContextMiddleware>();
app.UseAuthorization();
app.MapControllers();

// Liveness probe for Docker/compose healthchecks + rolling deploys.
// Anonymous + excluded from OrgContextMiddleware (see middleware whitelist).
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "BiddingBuddyBFF" }))
   .AllowAnonymous();

app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "[BiddingBuddyBFF] Host terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
