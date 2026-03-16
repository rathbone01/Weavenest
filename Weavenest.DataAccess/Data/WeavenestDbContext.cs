using Microsoft.EntityFrameworkCore;
using Weavenest.DataAccess.Models;

namespace Weavenest.DataAccess.Data;

public class WeavenestDbContext : DbContext
{
    public WeavenestDbContext(DbContextOptions<WeavenestDbContext> options)
        : base(options) { }

    public DbSet<EmotionalState> EmotionalStates => Set<EmotionalState>();
    public DbSet<LongTermMemory> LongTermMemories => Set<LongTermMemory>();
    public DbSet<TickLog> TickLogs => Set<TickLog>();
    public DbSet<HumanMessage> HumanMessages => Set<HumanMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WeavenestDbContext).Assembly);
    }
}
