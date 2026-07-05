using BiddingBuddy.Bff.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BiddingBuddy.Bff.Infrastructure.Persistence.Configurations;

public class UserSavedFilterConfiguration : IEntityTypeConfiguration<UserSavedFilter>
{
    public void Configure(EntityTypeBuilder<UserSavedFilter> b)
    {
        b.ToTable("user_saved_filters");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.UserId).HasColumnName("user_id");
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.Kind).HasColumnName("kind").IsRequired();
        b.Property(x => x.Name).HasColumnName("name");
        // Stored as jsonb; Npgsql serializes the POCO via System.Text.Json.
        b.Property(x => x.Filters).HasColumnName("filters").HasColumnType("jsonb").IsRequired();
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        b.HasIndex(x => new { x.UserId, x.OrgId });

        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
        b.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrgId);
    }
}
