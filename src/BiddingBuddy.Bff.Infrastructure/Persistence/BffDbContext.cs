using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace BiddingBuddy.Bff.Infrastructure.Persistence;

public class BffDbContext(DbContextOptions<BffDbContext> options) : DbContext(options)
{
    // Auth & Identity
    public DbSet<User> Users => Set<User>();
    public DbSet<OAuthAccount> OAuthAccounts => Set<OAuthAccount>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    // Orgs
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrgMember> OrgMembers => Set<OrgMember>();

    // Tenders
    public DbSet<Tender> Tenders => Set<Tender>();
    public DbSet<TenderDocument> TenderDocuments => Set<TenderDocument>();
    public DbSet<OrgTenderSettings> OrgTenderSettings => Set<OrgTenderSettings>();

    // Bids
    public DbSet<Bid> Bids => Set<Bid>();
    public DbSet<BidActivity> BidActivities => Set<BidActivity>();
    public DbSet<BidChecklistItem> BidChecklistItems => Set<BidChecklistItem>();
    public DbSet<BidComment> BidComments => Set<BidComment>();

    // Compliance
    public DbSet<ComplianceRequirement> ComplianceRequirements => Set<ComplianceRequirement>();
    public DbSet<ComplianceDocument> ComplianceDocuments => Set<ComplianceDocument>();

    // Documents
    public DbSet<DocumentFolder> DocumentFolders => Set<DocumentFolder>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();

    // Orders
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<DeliveryMilestone> DeliveryMilestones => Set<DeliveryMilestone>();

    // Payments
    public DbSet<EmdPayment> EmdPayments => Set<EmdPayment>();
    public DbSet<Invoice> Invoices => Set<Invoice>();

    // Competitors
    public DbSet<Competitor> Competitors => Set<Competitor>();
    public DbSet<CompetitorBidObservation> CompetitorBidObservations => Set<CompetitorBidObservation>();

    // AI & Performance
    public DbSet<AiAnalysisResult> AiAnalysisResults => Set<AiAnalysisResult>();
    public DbSet<OrgPerformanceSnapshot> OrgPerformanceSnapshots => Set<OrgPerformanceSnapshot>();

    // Notifications
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();

    // Integrations
    public DbSet<GemIntegration> GemIntegrations => Set<GemIntegration>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BffDbContext).Assembly);
    }
}
