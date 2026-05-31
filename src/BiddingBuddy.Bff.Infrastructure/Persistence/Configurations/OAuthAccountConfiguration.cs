using BiddingBuddy.Bff.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BiddingBuddy.Bff.Infrastructure.Persistence.Configurations;

public class OAuthAccountConfiguration : IEntityTypeConfiguration<OAuthAccount>
{
    public void Configure(EntityTypeBuilder<OAuthAccount> b)
    {
        b.ToTable("oauth_accounts");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.UserId).HasColumnName("user_id");
        b.Property(x => x.Provider).HasColumnName("provider").IsRequired();
        b.Property(x => x.ProviderUserId).HasColumnName("provider_user_id").IsRequired();
        b.Property(x => x.Email).HasColumnName("email");
        b.Property(x => x.AccessToken).HasColumnName("access_token");
        b.Property(x => x.RefreshToken).HasColumnName("refresh_token");
        b.Property(x => x.TokenExpiresAt).HasColumnName("token_expires_at");
        b.Property(x => x.RawProfile).HasColumnName("raw_profile").HasColumnType("jsonb");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        b.HasIndex(x => new { x.Provider, x.ProviderUserId }).IsUnique();
        b.HasIndex(x => x.UserId);
    }
}
