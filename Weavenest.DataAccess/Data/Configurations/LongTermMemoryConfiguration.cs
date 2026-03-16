using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Weavenest.DataAccess.Models;

namespace Weavenest.DataAccess.Data.Configurations;

public class LongTermMemoryConfiguration : IEntityTypeConfiguration<LongTermMemory>
{
    public void Configure(EntityTypeBuilder<LongTermMemory> builder)
    {
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Category).IsRequired();
        builder.Property(m => m.Content).IsRequired();
        builder.Property(m => m.TagsJson).IsRequired().HasDefaultValue("[]");
        builder.Property(m => m.Importance).IsRequired().HasDefaultValue(3);
        builder.Property(m => m.Confidence).IsRequired().HasDefaultValue(0.5f);
        builder.Property(m => m.CreatedAt).IsRequired();
        builder.Property(m => m.LastAccessedAt).IsRequired();
        builder.Property(m => m.LinkedMemoryIdsJson).IsRequired().HasDefaultValue("[]");
        builder.Property(m => m.IsSuperseded).IsRequired().HasDefaultValue(false);

        builder.HasIndex(m => m.LastAccessedAt);
        builder.HasIndex(m => m.IsSuperseded);
        builder.HasIndex(m => m.Category);
    }
}
