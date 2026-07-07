using BiddingBuddy.Bff.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BiddingBuddy.Bff.Infrastructure.Persistence.Configurations;

public class OAuthExchangeCodeConfiguration : IEntityTypeConfiguration<OAuthExchangeCode>
{
    public void Configure(EntityTypeBuilder<OAuthExchangeCode> b)
    {
        b.ToTable("oauth_exchange_codes");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.UserId).HasColumnName("user_id");
        b.Property(x => x.CodeHash).HasColumnName("code_hash").IsRequired();
        b.Property(x => x.CodeChallenge).HasColumnName("code_challenge").IsRequired();
        b.Property(x => x.IsNewUser).HasColumnName("is_new_user");
        b.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        b.Property(x => x.UsedAt).HasColumnName("used_at");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        b.HasIndex(x => x.CodeHash).IsUnique();
        b.HasIndex(x => x.ExpiresAt);

        b.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
