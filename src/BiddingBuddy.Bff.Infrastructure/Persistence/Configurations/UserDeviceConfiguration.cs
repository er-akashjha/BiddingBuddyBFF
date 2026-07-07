using BiddingBuddy.Bff.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BiddingBuddy.Bff.Infrastructure.Persistence.Configurations;

public class UserDeviceConfiguration : IEntityTypeConfiguration<UserDevice>
{
    public void Configure(EntityTypeBuilder<UserDevice> b)
    {
        b.ToTable("user_devices");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.UserId).HasColumnName("user_id");
        b.Property(x => x.Platform).HasColumnName("platform").IsRequired();
        b.Property(x => x.FcmToken).HasColumnName("fcm_token").IsRequired();
        b.Property(x => x.AppVersion).HasColumnName("app_version");
        b.Property(x => x.PushEnabled).HasColumnName("push_enabled");
        b.Property(x => x.LastSeenAt).HasColumnName("last_seen_at").HasDefaultValueSql("now()");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        b.Property(x => x.RevokedAt).HasColumnName("revoked_at");
        b.Property(x => x.RevocationReason).HasColumnName("revocation_reason");

        b.HasIndex(x => x.FcmToken).IsUnique();
        b.HasIndex(x => x.UserId);

        b.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
