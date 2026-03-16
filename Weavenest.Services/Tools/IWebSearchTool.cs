namespace Weavenest.Services.Tools;

public interface IWebSearchTool
{
    Task<string> SearchAsync(string query, CancellationToken ct = default);
}
