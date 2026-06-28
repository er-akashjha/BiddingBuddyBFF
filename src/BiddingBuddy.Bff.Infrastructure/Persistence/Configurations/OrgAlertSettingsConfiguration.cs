using BiddingBuddy.Bff.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BiddingBuddy.Bff.Infrastructure.Persistence.Configurations;

public class OrgAlertSettingsConfiguration : IEntityTypeConfiguration<OrgAlertSettings>
{
    public void Configure(EntityTypeBuilder<OrgAlertSettings> b)
    {
        b.ToTable("org_alert_settings");
        b.HasKey(x => x.OrgId);
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.IsEnabled).HasColumnName("is_enabled").HasDefaultValue(true);
        b.Property(x => x.DigestSize).HasColumnName("digest_size").HasDefaultValue(10);
        b.Property(x => x.MinSendIntervalMinutes).HasColumnName("min_send_interval_minutes").HasDefaultValue(360);
        b.Property(x => x.LastDigestSentAt).HasColumnName("last_digest_sent_at");
        b.Property(x => x.NotifyChannels).HasColumnName("notify_channels").HasColumnType("text[]");
        b.Property(x => x.NotifyRoles).HasColumnName("notify_roles").HasColumnType("text[]");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        b.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrgId);
    }
}
