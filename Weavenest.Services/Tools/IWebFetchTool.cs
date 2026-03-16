namespace Weavenest.Services.Tools;

public interface IWebFetchTool
{
    Task<string> FetchAsync(string url, CancellationToken ct = default);
}
