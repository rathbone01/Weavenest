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

        services.AddSingleton<IOllamaService, OllamaService>();

        services.AddScoped<IChatRepository, InMemoryChatRepository>();

        services.AddScoped<ChatStateNotifier>();

        services.AddScoped<CircuitSettings>();

        return services;
    }
}
