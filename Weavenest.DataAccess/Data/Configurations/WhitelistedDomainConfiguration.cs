using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Weavenest.DataAccess.Models;

namespace Weavenest.DataAccess.Data.Configurations;

public class WhitelistedDomainConfiguration : IEntityTypeConfiguration<WhitelistedDomain>
{
    public void Configure(EntityTypeBuilder<WhitelistedDomain> builder)
    {
        builder.ToTable("WhitelistedDomains");

        builder.HasKey(w => w.Id);

        builder.Property(w => w.Domain)
            .IsRequired()
            .HasMaxLength(253);

        builder.Property(w => w.CreatedAt)
            .IsRequired();

        builder.HasIndex(w => new { w.SessionId, w.Domain })
            .IsUnique();

        builder.HasOne<ChatSession>()
            .WithMany()
            .HasForeignKey(w => w.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
