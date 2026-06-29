using BiddingBuddy.Bff.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BiddingBuddy.Bff.Infrastructure.Persistence.Configurations;

public class BidConfiguration : IEntityTypeConfiguration<Bid>
{
    public void Configure(EntityTypeBuilder<Bid> b)
    {
        b.ToTable("bids");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.TenderId).HasColumnName("tender_id");
        b.Property(x => x.Title).HasColumnName("title").IsRequired();
        b.Property(x => x.Description).HasColumnName("description");
        b.Property(x => x.Stage).HasColumnName("stage").HasDefaultValue("identified");
        b.Property(x => x.StatusCategory).HasColumnName("status_category")
            .HasComputedColumnSql(
                "CASE WHEN stage IN ('won','lost','dropped') THEN 'closed' ELSE 'open' END",
                stored: true);
        b.Property(x => x.Priority).HasColumnName("priority").HasDefaultValue("medium");
        b.Property(x => x.AssignedTo).HasColumnName("assigned_to");
        b.Property(x => x.CreatedBy).HasColumnName("created_by");
        b.Property(x => x.DueDate).HasColumnName("due_date");
        b.Property(x => x.TenderValue).HasColumnName("tender_value").HasPrecision(15, 2);
        b.Property(x => x.OurBidValue).HasColumnName("our_bid_value").HasPrecision(15, 2);
        b.Property(x => x.WinProbability).HasColumnName("win_probability").HasPrecision(5, 2);
        b.Property(x => x.ProgressPct).HasColumnName("progress_pct").HasDefaultValue(0);
        b.Property(x => x.LossReason).HasColumnName("loss_reason");
        b.Property(x => x.WonValue).HasColumnName("won_value").HasPrecision(15, 2);
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        b.HasIndex(x => x.OrgId);
        b.HasIndex(x => new { x.OrgId, x.Stage });
        b.HasIndex(x => new { x.OrgId, x.StatusCategory });
        b.HasIndex(x => x.AssignedTo);

        b.HasOne(x => x.Organization).WithMany(x => x.Bids).HasForeignKey(x => x.OrgId);
        b.HasOne(x => x.Tender).WithMany().HasForeignKey(x => x.TenderId).IsRequired(false);
        b.HasOne(x => x.AssignedUser).WithMany().HasForeignKey(x => x.AssignedTo).IsRequired(false);
        b.HasMany(x => x.Activities).WithOne(x => x.Bid).HasForeignKey(x => x.BidId);
        b.HasMany(x => x.ChecklistItems).WithOne(x => x.Bid).HasForeignKey(x => x.BidId);
    }
}
