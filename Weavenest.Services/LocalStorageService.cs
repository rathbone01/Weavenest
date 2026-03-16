using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Weavenest.Services.Interfaces;

namespace Weavenest.Services;

public class LocalStorageService(IJSRuntime jsRuntime, ILogger<LocalStorageService> logger) : ILocalStorageService
{
    public async Task<T> GetItem<T>(string key)
    {
        try
        {
            var json = await jsRuntime.InvokeAsync<string>("localStorage.getItem", key);
            if (json is null)
                return default!;

            return JsonSerializer.Deserialize<T>(json)!;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting local storage item with key: {Key}", key);
            return default!;
        }
    }

    public async Task SetItem<T>(string key, T value)
    {
        try
        {
            var json = JsonSerializer.Serialize(value);
            await jsRuntime.InvokeVoidAsync("localStorage.setItem", key, json);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error setting local storage item with key: {Key}", key);
        }
    }

    public async Task RemoveItem(string key)
    {
        try
        {
            await jsRuntime.InvokeVoidAsync("localStorage.removeItem", key);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error removing local storage item with key: {Key}", key);
        }
    }
}
