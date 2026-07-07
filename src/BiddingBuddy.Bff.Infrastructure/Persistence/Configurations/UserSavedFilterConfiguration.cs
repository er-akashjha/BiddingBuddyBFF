using System.Text.Json;
using BiddingBuddy.Bff.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BiddingBuddy.Bff.Infrastructure.Persistence.Configurations;

public class UserSavedFilterConfiguration : IEntityTypeConfiguration<UserSavedFilter>
{
    // Serialize the filter blob ourselves (STJ) and store it as a JSON string in a
    // jsonb column — the same jsonb-as-string pattern every other jsonb column here
    // uses (Notification.Payload, Tender.RawData, …). This avoids Npgsql's dynamic
    // POCO→jsonb serialization, which is NOT enabled on this DbContext
    // (UseNpgsql without EnableDynamicJson) and would throw on write.
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<UserSavedFilter> b)
    {
        b.ToTable("user_saved_filters");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.UserId).HasColumnName("user_id");
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.Kind).HasColumnName("kind").IsRequired();
        b.Property(x => x.Name).HasColumnName("name");

        var converter = new ValueConverter<SavedFilterState, string>(
            v => JsonSerializer.Serialize(v, JsonOpts),
            v => JsonSerializer.Deserialize<SavedFilterState>(v, JsonOpts) ?? new SavedFilterState());

        // Compare/snapshot by serialized JSON so change tracking is correct for the
        // mutable reference type.
        var comparer = new ValueComparer<SavedFilterState>(
            (a, c) => JsonSerializer.Serialize(a, JsonOpts) == JsonSerializer.Serialize(c, JsonOpts),
            v => JsonSerializer.Serialize(v, JsonOpts).GetHashCode(),
            v => JsonSerializer.Deserialize<SavedFilterState>(JsonSerializer.Serialize(v, JsonOpts), JsonOpts) ?? new SavedFilterState());

        b.Property(x => x.Filters)
            .HasColumnName("filters")
            .HasColumnType("jsonb")
            .HasConversion(converter, comparer)
            .IsRequired();

        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        b.HasIndex(x => new { x.UserId, x.OrgId });

        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
        b.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrgId);
    }
}
