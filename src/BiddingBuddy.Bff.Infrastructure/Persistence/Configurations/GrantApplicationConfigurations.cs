using BiddingBuddy.Bff.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BiddingBuddy.Bff.Infrastructure.Persistence.Configurations;

// Every column is explicitly .HasColumnName-mapped (no snake_case convention plugin in this project).

public class GrantApplicationConfiguration : IEntityTypeConfiguration<GrantApplication>
{
    public void Configure(EntityTypeBuilder<GrantApplication> b)
    {
        b.ToTable("grant_applications");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.MongoGrantId).HasColumnName("mongo_grant_id");
        b.Property(x => x.Title).HasColumnName("title").IsRequired();
        b.Property(x => x.AgencyName).HasColumnName("agency_name");
        b.Property(x => x.OpportunityNumber).HasColumnName("opportunity_number");
        b.Property(x => x.Stage).HasColumnName("stage").HasDefaultValue("Qualifying");
        b.Property(x => x.StatusCategory).HasColumnName("status_category")
            .HasComputedColumnSql(
                "CASE WHEN stage IN ('Awarded', 'Declined', 'Dropped') THEN 'closed' ELSE 'open' END",
                stored: true);
        b.Property(x => x.AmountRequested).HasColumnName("amount_requested").HasPrecision(15, 2);
        b.Property(x => x.Deadline).HasColumnName("deadline");
        b.Property(x => x.AssignedTo).HasColumnName("assigned_to");
        b.Property(x => x.Readiness).HasColumnName("readiness").HasDefaultValue(0);
        b.Property(x => x.CostSharePct).HasColumnName("cost_share_pct").HasPrecision(5, 2);
        b.Property(x => x.TribalSetAside).HasColumnName("tribal_set_aside").HasDefaultValue(false);
        b.Property(x => x.Tags).HasColumnName("tags");
        b.Property(x => x.SourceUrl).HasColumnName("source_url");
        b.Property(x => x.Notes).HasColumnName("notes");
        b.Property(x => x.CreatedBy).HasColumnName("created_by");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        b.HasIndex(x => new { x.OrgId, x.CreatedAt });
        b.HasIndex(x => new { x.OrgId, x.Stage });
        b.HasIndex(x => x.AssignedTo);

        b.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrgId);
        b.HasOne(x => x.AssignedUser).WithMany().HasForeignKey(x => x.AssignedTo).IsRequired(false);
        b.HasMany(x => x.Activities).WithOne(x => x.Application).HasForeignKey(x => x.ApplicationId);
        b.HasMany(x => x.ChecklistItems).WithOne(x => x.Application).HasForeignKey(x => x.ApplicationId);
    }
}

public class GrantApplicationActivityConfiguration : IEntityTypeConfiguration<GrantApplicationActivity>
{
    public void Configure(EntityTypeBuilder<GrantApplicationActivity> b)
    {
        b.ToTable("grant_application_activities");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.ApplicationId).HasColumnName("application_id");
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.ActorId).HasColumnName("actor_id");
        b.Property(x => x.Action).HasColumnName("action").IsRequired();
        b.Property(x => x.FromValue).HasColumnName("from_value");
        b.Property(x => x.ToValue).HasColumnName("to_value");
        b.Property(x => x.Note).HasColumnName("note");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        b.HasIndex(x => x.ApplicationId);
        b.HasOne(x => x.Actor).WithMany().HasForeignKey(x => x.ActorId).IsRequired(false);
    }
}

public class GrantApplicationChecklistItemConfiguration : IEntityTypeConfiguration<GrantApplicationChecklistItem>
{
    public void Configure(EntityTypeBuilder<GrantApplicationChecklistItem> b)
    {
        b.ToTable("grant_application_checklist_items");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.ApplicationId).HasColumnName("application_id");
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.Title).HasColumnName("title").IsRequired();
        b.Property(x => x.IsDone).HasColumnName("is_done").HasDefaultValue(false);
        b.Property(x => x.DueDate).HasColumnName("due_date");
        b.Property(x => x.AssignedTo).HasColumnName("assigned_to");
        b.Property(x => x.SortOrder).HasColumnName("sort_order").HasDefaultValue(0);
        b.Property(x => x.DoneAt).HasColumnName("done_at");
        b.Property(x => x.DoneBy).HasColumnName("done_by");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        b.HasIndex(x => x.ApplicationId);
    }
}
