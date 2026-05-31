using BiddingBuddy.Bff.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BiddingBuddy.Bff.Infrastructure.Persistence.Configurations;

public class OrgMemberConfiguration : IEntityTypeConfiguration<OrgMember>
{
    public void Configure(EntityTypeBuilder<OrgMember> b)
    {
        b.ToTable("org_members");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.UserId).HasColumnName("user_id");
        b.Property(x => x.Role).HasColumnName("role").HasDefaultValue("viewer");
        b.Property(x => x.Department).HasColumnName("department");
        b.Property(x => x.Status).HasColumnName("status").HasDefaultValue("active");
        b.Property(x => x.InvitedBy).HasColumnName("invited_by");
        b.Property(x => x.JoinedAt).HasColumnName("joined_at");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        b.HasIndex(x => new { x.OrgId, x.UserId }).IsUnique();
        b.HasIndex(x => x.UserId);
    }
}
