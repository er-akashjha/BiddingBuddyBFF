using BiddingBuddy.Bff.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BiddingBuddy.Bff.Infrastructure.Persistence.Configurations;

public class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> b)
    {
        b.ToTable("organizations");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.OwnedBy).HasColumnName("owned_by");
        b.Property(x => x.Name).HasColumnName("name").IsRequired();
        b.Property(x => x.Slug).HasColumnName("slug");
        b.Property(x => x.Gstin).HasColumnName("gstin");
        b.Property(x => x.Pan).HasColumnName("pan");
        b.Property(x => x.Industry).HasColumnName("industry");
        b.Property(x => x.CompanySize).HasColumnName("company_size");
        b.Property(x => x.RegisteredAddress).HasColumnName("registered_address");
        b.Property(x => x.City).HasColumnName("city");
        b.Property(x => x.State).HasColumnName("state");
        b.Property(x => x.Pincode).HasColumnName("pincode");
        b.Property(x => x.Website).HasColumnName("website");
        b.Property(x => x.GemSellerId).HasColumnName("gem_seller_id");
        b.Property(x => x.GemSellerName).HasColumnName("gem_seller_name");
        b.Property(x => x.PrimaryCategory).HasColumnName("primary_category");
        b.Property(x => x.LogoUrl).HasColumnName("logo_url");

        // Buyer-side identity (migration 0031). org_type is NOT NULL with a 'supplier' default,
        // which is what makes the column safe to add to a live table.
        b.Property(x => x.OrgType).HasColumnName("org_type").HasDefaultValue("supplier");
        b.Property(x => x.EntityType).HasColumnName("entity_type");
        b.Property(x => x.Ministry).HasColumnName("ministry");
        b.Property(x => x.Department).HasColumnName("department");
        b.Property(x => x.Office).HasColumnName("office");
        b.Property(x => x.ProcuringEntityCode).HasColumnName("procuring_entity_code");

        // Enterprise SSO (migration 0032). The unique filtered index is what keeps one Entra
        // directory attached to at most one workspace.
        b.Property(x => x.EntraTenantId).HasColumnName("entra_tenant_id");
        b.Property(x => x.SsoDefaultRole).HasColumnName("sso_default_role").HasDefaultValue("viewer");
        b.HasIndex(x => x.EntraTenantId).IsUnique().HasFilter("entra_tenant_id IS NOT NULL");

        b.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        b.HasIndex(x => x.Slug).IsUnique().HasFilter("slug IS NOT NULL");
        b.HasIndex(x => x.OwnedBy);

        b.HasMany(x => x.Members).WithOne(x => x.Organization).HasForeignKey(x => x.OrgId);
    }
}
