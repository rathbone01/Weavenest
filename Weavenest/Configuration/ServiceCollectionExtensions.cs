using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Weavenest.DataAccess.Data;
using Weavenest.DataAccess.Repositories;
using Weavenest.Services;
using Weavenest.Services.Interfaces;
using Weavenest.Services.Models.Options;

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

        services.AddSingleton<IOllamaService, OllamaService>();

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

        return services;
    }
}
