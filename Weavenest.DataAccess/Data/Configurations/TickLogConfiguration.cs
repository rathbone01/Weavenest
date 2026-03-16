using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Weavenest.DataAccess.Models;

namespace Weavenest.DataAccess.Data.Configurations;

public class TickLogConfiguration : IEntityTypeConfiguration<TickLog>
{
    public void Configure(EntityTypeBuilder<TickLog> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Timestamp).IsRequired();

        builder.HasIndex(t => t.Timestamp);
    }
}
