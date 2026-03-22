using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Weavenest.DataAccess.Data;
using Weavenest.DataAccess.Interfaces;
using Weavenest.DataAccess.Repositories;
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
        services.Configure<OllamaOptions>(
            configuration.GetSection(OllamaOptions.SectionName));
        services.Configure<JwtOptions>(
            configuration.GetSection(JwtOptions.SectionName));
        services.Configure<SearXNGOptions>(
            configuration.GetSection(SearXNGOptions.SectionName));

        services.AddSingleton<IOllamaService, OllamaService>();

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

        services.AddSingleton<IWebSearchTool, SearXNGSearchTool>();
        services.AddSingleton<IWebFetchTool, HtmlAgilityPackFetchTool>();
        services.AddScoped<IAgenticChatService, AgenticChatService>();

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        services.AddDbContextFactory<WeavenestDbContext>(options =>
    options.UseSqlServer(
        connectionString,
        b => b.MigrationsAssembly("Weavenest.DataAccess")
    ));

        services.AddScoped<IChatRepository, EfChatRepository>();
        services.AddScoped<IFolderRepository, EfFolderRepository>();
        services.AddScoped<IUserRepository, EfUserRepository>();

        services.AddScoped<ILocalStorageService, LocalStorageService>();
        services.AddScoped<IUserIdentityService, UserIdentityService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<TokenService>();
        services.AddScoped<AuthenticationStateProvider, CustomAuthenticationStateProvider>();
        services.AddScoped(sp =>
            (CustomAuthenticationStateProvider)sp.GetRequiredService<AuthenticationStateProvider>());

        services.AddScoped<ChatStateNotifier>();
        services.AddScoped<CircuitSettings>();
        services.AddScoped<IEncryptionService, EncryptionService>();
        services.AddScoped<DataMigrationService>();

        return services;
    }
}
