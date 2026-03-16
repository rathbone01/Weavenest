using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Weavenest.DataAccess.Data;
using Weavenest.DataAccess.Models;
using Weavenest.Services.Interfaces;
using Weavenest.Services.Models.Options;

namespace Weavenest.Services;

public class LongTermMemoryService
{
    private readonly WeavenestDbContext _db;
    private readonly IOllamaService _ollama;
    private readonly MindSettings _settings;
    private readonly ILogger<LongTermMemoryService> _logger;

    public LongTermMemoryService(
        WeavenestDbContext db,
        IOllamaService ollama,
        IOptions<MindSettings> settings,
        ILogger<LongTermMemoryService> logger)
    {
        _db = db;
        _ollama = ollama;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<LongTermMemory> StoreAsync(
        MemoryCategory category,
        string content,
        string[] tags,
        int importance,
        float confidence,
        EmotionalState? emotionalContext = null)
    {
        var memory = new LongTermMemory
        {
            Id = Guid.NewGuid(),
            Category = category,
            Content = content,
            TagsJson = JsonSerializer.Serialize(tags),
            Importance = Math.Clamp(importance, 1, 5),
            Confidence = Math.Clamp(confidence, 0f, 1f),
            CreatedAt = DateTime.UtcNow,
            LastAccessedAt = DateTime.UtcNow,
            EmotionalContextJson = emotionalContext is not null
                ? JsonSerializer.Serialize(new
                {
                    emotionalContext.Happiness,
                    emotionalContext.Sadness,
                    emotionalContext.Disgust,
                    emotionalContext.Fear,
                    emotionalContext.Surprise,
                    emotionalContext.Anger
                })
                : null,
            IsSuperseded = false
        };

        // Generate semantic embedding — failures are non-fatal; memory is stored without it
        var embedding = await _ollama.GenerateEmbeddingAsync(content);
        if (embedding is not null)
            memory.EmbeddingJson = JsonSerializer.Serialize(embedding);

        _db.LongTermMemories.Add(memory);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Stored {Category} memory: {Preview} (confidence: {Confidence:F2}, embedding: {HasEmbed})",
            category, content.Length > 60 ? content[..60] + "..." : content, confidence, embedding is not null);

        return memory;
    }

    public async Task<List<LongTermMemory>> RetrieveRelevantAsync(string[] queryTags, int? count = null)
    {
        var retrievalCount = count ?? _settings.LongTermMemoryRetrievalCount;

        var memories = await _db.LongTermMemories
            .Where(m => !m.IsSuperseded)
            .ToListAsync();

        if (memories.Count == 0) return [];

        // Generate a query embedding from the tags joined as a sentence
        float[]? queryEmbedding = null;
        if (queryTags.Length > 0)
        {
            queryEmbedding = await _ollama.GenerateEmbeddingAsync(string.Join(" ", queryTags));
        }

        var now = DateTime.UtcNow;
        var maxAge = memories.Max(m => (now - m.LastAccessedAt).TotalHours);
        if (maxAge < 1.0) maxAge = 1.0;

        var scored = memories.Select(m =>
        {
            var memoryTags = ParseTags(m.TagsJson);
            var tagOverlap = queryTags.Length > 0
                ? (float)memoryTags.Intersect(queryTags, StringComparer.OrdinalIgnoreCase).Count() / queryTags.Length
                : 0f;

            var ageHours = (now - m.LastAccessedAt).TotalHours;
            var recencyScore = 1.0f - (float)(ageHours / maxAge);

            var semanticScore = 0f;
            if (queryEmbedding is not null && m.EmbeddingJson is not null)
            {
                var memEmbed = ParseEmbedding(m.EmbeddingJson);
                if (memEmbed is not null)
                    semanticScore = CosineSimilarity(queryEmbedding, memEmbed);
            }

            var score = _settings.RelevanceWeight * tagOverlap
                      + _settings.SemanticWeight * semanticScore
                      + _settings.RecencyWeight * recencyScore;

            return (Memory: m, Score: score, TagOverlap: tagOverlap, SemanticScore: semanticScore);
        })
        // Include if there's any tag match OR strong semantic similarity — exclude pure recency matches
        .Where(x => x.TagOverlap > 0 || x.SemanticScore > 0.4f)
        .OrderByDescending(x => x.Score)
        .Take(retrievalCount)
        .ToList();

        // Update LastAccessedAt for retrieved memories
        foreach (var (memory, _, _, _) in scored)
        {
            memory.LastAccessedAt = now;
        }
        await _db.SaveChangesAsync();

        return scored.Select(x => x.Memory).ToList();
    }

    public async Task<List<LongTermMemory>> RecallByKeywordAsync(string keyword, int count = 10)
    {
        return await _db.LongTermMemories
            .Where(m => !m.IsSuperseded && m.Content.Contains(keyword))
            .OrderByDescending(m => m.LastAccessedAt)
            .Take(count)
            .ToListAsync();
    }

    public async Task SupersedeAsync(Guid oldId, LongTermMemory newMemory)
    {
        var old = await _db.LongTermMemories.FindAsync(oldId);
        if (old is null)
        {
            _logger.LogWarning("Cannot supersede memory {Id}: not found", oldId);
            return;
        }

        old.IsSuperseded = true;

        newMemory.Id = Guid.NewGuid();
        newMemory.CreatedAt = DateTime.UtcNow;
        newMemory.LastAccessedAt = DateTime.UtcNow;

        // Link old to new
        var oldLinks = ParseGuids(old.LinkedMemoryIdsJson);
        oldLinks.Add(newMemory.Id);
        old.LinkedMemoryIdsJson = JsonSerializer.Serialize(oldLinks);

        var newLinks = ParseGuids(newMemory.LinkedMemoryIdsJson);
        newLinks.Add(oldId);
        newMemory.LinkedMemoryIdsJson = JsonSerializer.Serialize(newLinks);

        _db.LongTermMemories.Add(newMemory);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Superseded memory {OldId} with {NewId}", oldId, newMemory.Id);
    }

    public async Task LinkAsync(Guid id1, Guid id2)
    {
        var mem1 = await _db.LongTermMemories.FindAsync(id1);
        var mem2 = await _db.LongTermMemories.FindAsync(id2);

        if (mem1 is null || mem2 is null)
        {
            _logger.LogWarning("Cannot link memories: one or both not found ({Id1}, {Id2})", id1, id2);
            return;
        }

        var links1 = ParseGuids(mem1.LinkedMemoryIdsJson);
        if (!links1.Contains(id2))
        {
            links1.Add(id2);
            mem1.LinkedMemoryIdsJson = JsonSerializer.Serialize(links1);
        }

        var links2 = ParseGuids(mem2.LinkedMemoryIdsJson);
        if (!links2.Contains(id1))
        {
            links2.Add(id1);
            mem2.LinkedMemoryIdsJson = JsonSerializer.Serialize(links2);
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation("Linked memories {Id1} <-> {Id2}", id1, id2);
    }

    public async Task<LongTermMemory?> GetByIdAsync(Guid id)
    {
        return await _db.LongTermMemories.FindAsync(id);
    }

    public string FormatMemoriesForPrompt(List<LongTermMemory> memories)
    {
        if (memories.Count == 0)
            return "No relevant long-term memories found.";

        var lines = memories.Select(m =>
        {
            var tags = ParseTags(m.TagsJson);
            var age = DateTime.UtcNow - m.CreatedAt;
            var ageStr = age.TotalDays > 1 ? $"{age.TotalDays:F0} days ago" :
                         age.TotalHours > 1 ? $"{age.TotalHours:F0} hours ago" : "recently";
            return $"- [id:{m.Id}] [{m.Category}] (confidence: {m.Confidence:F1}, {ageStr}) {m.Content} [tags: {string.Join(", ", tags)}]";
        });

        return string.Join("\n", lines);
    }

    private static float[]? ParseEmbedding(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<float[]>(json); }
        catch { return null; }
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0f;
        float dot = 0f, normA = 0f, normB = 0f;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        return normA == 0f || normB == 0f ? 0f : dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
    }

    private static string[] ParseTags(string json)
    {
        try { return JsonSerializer.Deserialize<string[]>(json) ?? []; }
        catch { return []; }
    }

    private static List<Guid> ParseGuids(string json)
    {
        try { return JsonSerializer.Deserialize<List<Guid>>(json) ?? []; }
        catch { return []; }
    }
}
