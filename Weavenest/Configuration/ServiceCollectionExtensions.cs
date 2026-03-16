using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Weavenest.DataAccess.Data;
using Weavenest.Services;
using Weavenest.Services.Interfaces;
using Weavenest.Services.Models.Options;
using Weavenest.Services.Tools;

namespace Weavenest.Configuration;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWeavenestServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Configuration
        services.Configure<OllamaOptions>(
            configuration.GetSection(OllamaOptions.SectionName));
        services.Configure<SearXNGOptions>(
            configuration.GetSection(SearXNGOptions.SectionName));
        services.Configure<MindSettings>(
            configuration.GetSection(MindSettings.SectionName));

        // HTTP Clients
        services.AddHttpClient("OllamaApi", (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromMinutes(5);
        });

        services.AddHttpClient("SearXNG", (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<SearXNGOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        services.AddHttpClient("WebFetch", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Weavenest/1.0");
        });

        // Singleton services
        services.AddSingleton<IOllamaService, OllamaService>();
        services.AddSingleton<IWebSearchTool, SearXNGSearchTool>();
        services.AddSingleton<IWebFetchTool, HtmlAgilityPackFetchTool>();
        services.AddSingleton<MindStateService>();
        services.AddSingleton<ShortTermMemoryService>();

        // Database — factory only; scoped DbContext is resolved per-scope via the factory
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        services.AddDbContextFactory<WeavenestDbContext>(options =>
            options.UseSqlServer(
                connectionString,
                b => b.MigrationsAssembly("Weavenest.DataAccess")
            ));
        services.AddScoped(sp =>
            sp.GetRequiredService<IDbContextFactory<WeavenestDbContext>>().CreateDbContext());

        // Scoped services
        services.AddScoped<EmotionService>();
        services.AddScoped<LongTermMemoryService>();
        services.AddTransient<PromptAssemblyService>();

        // Tool handlers (auto-collected via IEnumerable<IToolHandler>)
        services.AddScoped<IToolHandler, SpeakToolHandler>();
        services.AddScoped<IToolHandler, StoreMemoryToolHandler>();
        services.AddScoped<IToolHandler, UpdateEmotionToolHandler>();
        services.AddScoped<IToolHandler, RecallToolHandler>();
        services.AddScoped<IToolHandler, ReflectToolHandler>();
        services.AddScoped<IToolHandler, LinkMemoriesToolHandler>();
        services.AddScoped<IToolHandler, SupersedeMemoryToolHandler>();
        services.AddScoped<IToolHandler, WebSearchToolHandler>();
        services.AddScoped<IToolHandler, WebFetchToolHandler>();
        services.AddScoped<ToolDispatchService>();

        // Background consciousness loop
        services.AddHostedService<ConsciousnessLoopService>();

        return services;
    }
}
