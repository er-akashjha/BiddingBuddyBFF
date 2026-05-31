using BiddingBuddy.Bff.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BiddingBuddy.Bff.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.Email).HasColumnName("email").IsRequired();
        b.Property(x => x.Name).HasColumnName("name").IsRequired();
        b.Property(x => x.AvatarUrl).HasColumnName("avatar_url");
        b.Property(x => x.Phone).HasColumnName("phone");
        b.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        b.Property(x => x.LastLoginAt).HasColumnName("last_login_at");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        b.HasIndex(x => x.Email).IsUnique();

        b.HasMany(x => x.OAuthAccounts).WithOne(x => x.User).HasForeignKey(x => x.UserId);
        b.HasMany(x => x.RefreshTokens).WithOne(x => x.User).HasForeignKey(x => x.UserId);
        b.HasMany(x => x.OrgMemberships).WithOne(x => x.User).HasForeignKey(x => x.UserId);
        b.HasMany(x => x.OwnedOrganizations).WithOne(x => x.Owner).HasForeignKey(x => x.OwnedBy);
    }
}
