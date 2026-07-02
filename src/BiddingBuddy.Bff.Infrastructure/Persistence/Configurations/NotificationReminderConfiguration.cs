using BiddingBuddy.Bff.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BiddingBuddy.Bff.Infrastructure.Persistence.Configurations;

public class NotificationReminderConfiguration : IEntityTypeConfiguration<NotificationReminder>
{
    public void Configure(EntityTypeBuilder<NotificationReminder> b)
    {
        b.ToTable("notification_reminders");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.EntityType).HasColumnName("entity_type");
        b.Property(x => x.EntityId).HasColumnName("entity_id");
        b.Property(x => x.ReminderKey).HasColumnName("reminder_key");
        b.Property(x => x.SentAt).HasColumnName("sent_at").HasDefaultValueSql("now()");

        // Mirrors the UNIQUE constraint the claim's ON CONFLICT targets (migration 0015).
        b.HasIndex(x => new { x.EntityType, x.EntityId, x.ReminderKey }).IsUnique();
    }
}
