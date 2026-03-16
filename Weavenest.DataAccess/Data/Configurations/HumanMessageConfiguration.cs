using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Weavenest.DataAccess.Models;

namespace Weavenest.DataAccess.Data.Configurations;

public class HumanMessageConfiguration : IEntityTypeConfiguration<HumanMessage>
{
    public void Configure(EntityTypeBuilder<HumanMessage> builder)
    {
        builder.HasKey(h => h.Id);

        builder.Property(h => h.Content).IsRequired();
        builder.Property(h => h.Timestamp).IsRequired();
        builder.Property(h => h.Processed).IsRequired().HasDefaultValue(false);

        builder.HasIndex(h => h.Processed);
        builder.HasIndex(h => h.Timestamp);
    }
}
