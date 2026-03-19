namespace Weavenest.Services;

/// <summary>
/// Detects repeated content in Jeremy's outputs to prevent thought loops.
/// Tracks cross-tick repetition of conscious/spoken content and detects
/// repeated substrings within large single outputs (subconscious).
/// </summary>
public class RepeatGuardService
{
    private readonly object _lock = new();

    // Rolling buffer of recent outputs for cross-tick comparison
    private readonly List<string> _recentConscious = [];
    private readonly List<string> _recentSpoken = [];
    private readonly List<string> _recentSubconscious = [];
    private const int MaxHistory = 8;

    // Minimum substring length to count as a "repeated block" within a single output
    private const int MinRepeatedSubstringLength = 80;

    // Similarity threshold (0-1) above which two outputs are considered "the same thought"
    private const double SimilarityThreshold = 0.55;

    /// <summary>
    /// Checks all outputs from the current tick for repetition against recent history
    /// and for internal repetition within the subconscious.
    /// Returns a warning string to inject into the next prompt, or null if no issues.
    /// </summary>
    public string? CheckAndRecord(string? subconscious, string? conscious, string? spoken)
    {
        var warnings = new List<string>();

        lock (_lock)
        {
            // 1. Check conscious output against recent conscious outputs
            if (!string.IsNullOrWhiteSpace(conscious))
            {
                var match = FindSimilar(_recentConscious, conscious);
                if (match is not null)
                    warnings.Add($"Your conscious thought is very similar to a recent one. Break the loop — think about something genuinely different.");

                AddToBuffer(_recentConscious, conscious);
            }

            // 2. Check spoken output against recent spoken outputs
            if (!string.IsNullOrWhiteSpace(spoken))
            {
                var match = FindSimilar(_recentSpoken, spoken);
                if (match is not null)
                    warnings.Add($"You just said something very similar to the human recently. Don't repeat yourself — say something new or stay silent.");

                AddToBuffer(_recentSpoken, spoken);
            }

            // 3. Check subconscious for internal repetition (repeated substrings)
            if (!string.IsNullOrWhiteSpace(subconscious))
            {
                var repeatedBlock = FindRepeatedSubstring(subconscious);
                if (repeatedBlock is not null)
                    warnings.Add($"Your subconscious is looping — the phrase \"{Truncate(repeatedBlock, 60)}\" appeared multiple times. Force yourself onto a new track.");

                // Also check cross-tick subconscious similarity
                var subMatch = FindSimilar(_recentSubconscious, subconscious);
                if (subMatch is not null)
                    warnings.Add("Your subconscious processing is repeating the same patterns as recent ticks. Shift your attention to something completely different.");

                AddToBuffer(_recentSubconscious, subconscious);
            }
        }

        // 4. Check if tool call results in spoken text also repeat the conscious text
        // (model sometimes echoes the same text as both inner monologue and speech)
        if (!string.IsNullOrWhiteSpace(conscious) && !string.IsNullOrWhiteSpace(spoken))
        {
            var echoSim = ComputeSimilarity(conscious, spoken);
            if (echoSim > 0.7)
                warnings.Add("You're echoing your inner thoughts directly as speech. Your spoken words should be distinct from your inner monologue.");
        }

        if (warnings.Count == 0)
            return null;

        return "[LOOP DETECTED] " + string.Join(" ", warnings) + " This is a system warning — you MUST change your thought pattern this tick.";
    }

    /// <summary>
    /// Finds the longest substring that appears at least twice in the text,
    /// above the minimum length threshold. Uses a sliding window + hash approach.
    /// </summary>
    private static string? FindRepeatedSubstring(string text)
    {
        if (text.Length < MinRepeatedSubstringLength * 2)
            return null;

        // Normalize whitespace for comparison
        var normalized = NormalizeWhitespace(text);
        if (normalized.Length < MinRepeatedSubstringLength * 2)
            return null;

        // Binary search for the longest repeated substring length
        int lo = MinRepeatedSubstringLength, hi = normalized.Length / 2;
        string? bestMatch = null;

        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            var found = FindRepeatedOfLength(normalized, mid);
            if (found is not null)
            {
                bestMatch = found;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return bestMatch;
    }

    /// <summary>
    /// Checks if any substring of exactly `length` characters appears at least twice.
    /// Uses a rolling hash (Rabin-Karp style) for efficiency.
    /// </summary>
    private static string? FindRepeatedOfLength(string text, int length)
    {
        if (length > text.Length / 2)
            return null;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i <= text.Length - length; i++)
        {
            var window = text.Substring(i, length);
            if (!seen.Add(window))
                return window;
        }

        return null;
    }

    /// <summary>
    /// Finds an entry in the history buffer that is similar to the candidate.
    /// Returns the matching entry or null.
    /// </summary>
    private static string? FindSimilar(List<string> history, string candidate)
    {
        foreach (var previous in history)
        {
            var sim = ComputeSimilarity(previous, candidate);
            if (sim >= SimilarityThreshold)
                return previous;
        }

        return null;
    }

    /// <summary>
    /// Computes a similarity score (0-1) between two strings using trigram Jaccard similarity.
    /// This is fast even for large strings and handles fuzzy matching well.
    /// </summary>
    private static double ComputeSimilarity(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return 0;

        var normA = NormalizeWhitespace(a.ToLowerInvariant());
        var normB = NormalizeWhitespace(b.ToLowerInvariant());

        // For very short strings, use simple containment check
        if (normA.Length < 30 || normB.Length < 30)
        {
            if (normA.Contains(normB) || normB.Contains(normA))
                return 0.9;
            return 0;
        }

        // Trigram Jaccard similarity
        var trigramsA = ExtractTrigrams(normA);
        var trigramsB = ExtractTrigrams(normB);

        if (trigramsA.Count == 0 || trigramsB.Count == 0)
            return 0;

        var intersection = trigramsA.Intersect(trigramsB).Count();
        var union = trigramsA.Union(trigramsB).Count();

        return union == 0 ? 0 : (double)intersection / union;
    }

    private static HashSet<string> ExtractTrigrams(string text)
    {
        var trigrams = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i <= text.Length - 3; i++)
            trigrams.Add(text.Substring(i, 3));
        return trigrams;
    }

    private static string NormalizeWhitespace(string text)
    {
        var chars = new char[text.Length];
        var j = 0;
        var lastWasSpace = false;

        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace)
                {
                    chars[j++] = ' ';
                    lastWasSpace = true;
                }
            }
            else
            {
                chars[j++] = c;
                lastWasSpace = false;
            }
        }

        return new string(chars, 0, j).Trim();
    }

    private static void AddToBuffer(List<string> buffer, string entry)
    {
        buffer.Add(entry);
        if (buffer.Count > MaxHistory)
            buffer.RemoveAt(0);
    }

    private static string Truncate(string text, int maxLen)
    {
        if (text.Length <= maxLen) return text;
        return text[..maxLen] + "...";
    }
}
