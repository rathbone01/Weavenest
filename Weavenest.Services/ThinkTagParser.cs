namespace Weavenest.Services;

public static class ThinkTagParser
{
    public static string StripThinkTags(string content)
    {
        if (string.IsNullOrEmpty(content)) return content;
        content = content.Replace("<think>", "", StringComparison.OrdinalIgnoreCase);
        content = content.Replace("</think>", "", StringComparison.OrdinalIgnoreCase);
        return content.Trim();
    }

    public static (string Content, string? Thinking) Parse(string rawContent)
    {
        if (string.IsNullOrEmpty(rawContent))
            return ("", null);

        var thinking = (string?)null;
        var content = rawContent;

        while (true)
        {
            var startIdx = content.IndexOf("<think>", StringComparison.OrdinalIgnoreCase);
            if (startIdx == -1)
                break;

            var endIdx = content.IndexOf("</think>", startIdx, StringComparison.OrdinalIgnoreCase);
            if (endIdx == -1)
            {
                var thinkBlock = content[(startIdx + "<think>".Length)..];
                thinking = thinking is null ? thinkBlock : thinking + thinkBlock;
                content = content[..startIdx];
                break;
            }

            var thinkContent = content[(startIdx + "<think>".Length)..endIdx];
            thinking = thinking is null ? thinkContent : thinking + thinkContent;
            content = content[..startIdx] + content[(endIdx + "</think>".Length)..];
        }

        // Strip any orphaned </think> tags left behind when Ollama's think:true already
        // extracted the opening block into message.thinking but left the closing tag in content
        content = content.Replace("</think>", "", StringComparison.OrdinalIgnoreCase);

        return (content.Trim(), thinking?.Trim());
    }
}
