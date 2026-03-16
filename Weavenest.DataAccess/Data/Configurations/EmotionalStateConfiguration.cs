using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Weavenest.DataAccess.Models;

namespace Weavenest.DataAccess.Data.Configurations;

public class EmotionalStateConfiguration : IEntityTypeConfiguration<EmotionalState>
{
    public void Configure(EntityTypeBuilder<EmotionalState> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Happiness).IsRequired();
        builder.Property(e => e.Sadness).IsRequired();
        builder.Property(e => e.Disgust).IsRequired();
        builder.Property(e => e.Fear).IsRequired();
        builder.Property(e => e.Surprise).IsRequired();
        builder.Property(e => e.Anger).IsRequired();
        builder.Property(e => e.Timestamp).IsRequired();

        builder.HasIndex(e => e.Timestamp);
    }
}
