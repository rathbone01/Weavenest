using System.Text.Json;
using Weavenest.Services.Models;

namespace Weavenest.Services.Tools;

public interface IToolHandler
{
    string Name { get; }
    string Description { get; }
    OllamaToolParameters ParameterSchema { get; }
    Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct);
}
