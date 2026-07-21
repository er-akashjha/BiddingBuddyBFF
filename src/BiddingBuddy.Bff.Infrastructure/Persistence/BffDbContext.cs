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
    public DbSet<PendingRegistration> PendingRegistrations => Set<PendingRegistration>();
    public DbSet<PasswordResetCode> PasswordResetCodes => Set<PasswordResetCode>();
    public DbSet<OAuthExchangeCode> OAuthExchangeCodes => Set<OAuthExchangeCode>();
    public DbSet<UserDevice> UserDevices => Set<UserDevice>();

    // Orgs
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrgMember> OrgMembers => Set<OrgMember>();
    public DbSet<OrganizationInvite> OrganizationInvites => Set<OrganizationInvite>();

    // Grants (GRANT product line). A global corpus like Tenders — no org_id; org-scoped grant
    // tables carry their own tenancy.
    public DbSet<GrantOpportunity> GrantOpportunities => Set<GrantOpportunity>();

    // Tenders
    public DbSet<Tender> Tenders => Set<Tender>();
    public DbSet<TenderDocument> TenderDocuments => Set<TenderDocument>();
    public DbSet<OrgTenderSettings> OrgTenderSettings => Set<OrgTenderSettings>();

    // Per-user saved tender filters (auto last-used snapshot + named views)
    public DbSet<UserSavedFilter> UserSavedFilters => Set<UserSavedFilter>();

    // Tender-match digests (interests → buffered matches → grouped notification)
    public DbSet<TenderAlertRule> TenderAlertRules => Set<TenderAlertRule>();
    public DbSet<OrgAlertSettings> OrgAlertSettings => Set<OrgAlertSettings>();
    public DbSet<TenderMatch> TenderMatches => Set<TenderMatch>();

    // Bids
    public DbSet<Bid> Bids => Set<Bid>();
    public DbSet<BidActivity> BidActivities => Set<BidActivity>();
    public DbSet<BidChecklistItem> BidChecklistItems => Set<BidChecklistItem>();
    public DbSet<BidComment> BidComments => Set<BidComment>();
    public DbSet<BidAttachment> BidAttachments => Set<BidAttachment>();
    public DbSet<BidDocument> BidDocuments => Set<BidDocument>();

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

    // In-app notification inbox (was Notifications; renamed to free the name
    // for the dispatch-event table below)
    public DbSet<UserNotification> UserNotifications => Set<UserNotification>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();

    // Idempotency ledger for the scheduled deadline/expiry reminder scan
    public DbSet<NotificationReminder> NotificationReminders => Set<NotificationReminder>();

    // Notification dispatch subsystem (event → per-channel deliveries → audit log)
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();
    public DbSet<NotificationTemplate> NotificationTemplates => Set<NotificationTemplate>();
    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();

    // Integrations
    public DbSet<GemIntegration> GemIntegrations => Set<GemIntegration>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BffDbContext).Assembly);
    }
}
