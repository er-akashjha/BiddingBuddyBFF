using BiddingBuddy.Bff.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BiddingBuddy.Bff.Infrastructure.Persistence.Configurations;

// Every column explicitly .HasColumnName-mapped (no snake_case convention plugin in this project).

public class GrantNarrativeSectionConfiguration : IEntityTypeConfiguration<GrantNarrativeSection>
{
    public void Configure(EntityTypeBuilder<GrantNarrativeSection> b)
    {
        b.ToTable("grant_narrative_sections");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.ApplicationId).HasColumnName("application_id");
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.SectionKey).HasColumnName("section_key").IsRequired();
        b.Property(x => x.Title).HasColumnName("title").IsRequired();
        b.Property(x => x.Content).HasColumnName("content");
        b.Property(x => x.WordCount).HasColumnName("word_count").HasDefaultValue(0);
        b.Property(x => x.TargetWords).HasColumnName("target_words");
        b.Property(x => x.Status).HasColumnName("status").HasDefaultValue("not_started");
        b.Property(x => x.SortOrder).HasColumnName("sort_order").HasDefaultValue(0);
        b.Property(x => x.UpdatedBy).HasColumnName("updated_by");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        b.HasIndex(x => new { x.ApplicationId, x.SectionKey }).IsUnique();
        b.HasOne(x => x.Application).WithMany(a => a.NarrativeSections).HasForeignKey(x => x.ApplicationId);
    }
}

public class GrantBudgetLineItemConfiguration : IEntityTypeConfiguration<GrantBudgetLineItem>
{
    public void Configure(EntityTypeBuilder<GrantBudgetLineItem> b)
    {
        b.ToTable("grant_budget_line_items");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.ApplicationId).HasColumnName("application_id");
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.Category).HasColumnName("category").IsRequired();
        b.Property(x => x.Description).HasColumnName("description").IsRequired();
        b.Property(x => x.Amount).HasColumnName("amount").HasPrecision(15, 2).HasDefaultValue(0m);
        b.Property(x => x.SortOrder).HasColumnName("sort_order").HasDefaultValue(0);
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        b.HasIndex(x => x.ApplicationId);
        b.HasOne(x => x.Application).WithMany(a => a.BudgetLineItems).HasForeignKey(x => x.ApplicationId);
    }
}

public class GrantReviewConfiguration : IEntityTypeConfiguration<GrantReview>
{
    public void Configure(EntityTypeBuilder<GrantReview> b)
    {
        b.ToTable("grant_reviews");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.ApplicationId).HasColumnName("application_id");
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.ReviewerId).HasColumnName("reviewer_id");
        b.Property(x => x.Status).HasColumnName("status").HasDefaultValue("pending");
        b.Property(x => x.Comments).HasColumnName("comments");
        b.Property(x => x.ReviewedAt).HasColumnName("reviewed_at");
        b.Property(x => x.CreatedBy).HasColumnName("created_by");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        b.HasIndex(x => x.ApplicationId);
        b.HasOne(x => x.Application).WithMany(a => a.Reviews).HasForeignKey(x => x.ApplicationId);
        b.HasOne(x => x.Reviewer).WithMany().HasForeignKey(x => x.ReviewerId).IsRequired(false);
    }
}

public class GrantSubmissionConfiguration : IEntityTypeConfiguration<GrantSubmission>
{
    public void Configure(EntityTypeBuilder<GrantSubmission> b)
    {
        b.ToTable("grant_submissions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.ApplicationId).HasColumnName("application_id");
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.Portal).HasColumnName("portal").HasDefaultValue("grants_gov");
        b.Property(x => x.Status).HasColumnName("status").HasDefaultValue("draft");
        b.Property(x => x.SubmittedAt).HasColumnName("submitted_at");
        b.Property(x => x.ConfirmationNumber).HasColumnName("confirmation_number");
        b.Property(x => x.SubmittedBy).HasColumnName("submitted_by");
        b.Property(x => x.AmountAwarded).HasColumnName("amount_awarded").HasPrecision(15, 2);
        b.Property(x => x.Notes).HasColumnName("notes");
        b.Property(x => x.FileManifest).HasColumnName("file_manifest").HasColumnType("jsonb");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        b.HasIndex(x => x.ApplicationId);
        b.HasOne(x => x.Application).WithMany(a => a.Submissions).HasForeignKey(x => x.ApplicationId);
    }
}
