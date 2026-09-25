using System.Text;
using System.Text.Json;

namespace PMate.Services;

/// <summary>Reads the block editor's JSON (a Tiptap / ProseMirror document) and returns its plain text.</summary>
public static class NoteText
{
    /// <summary>Largest page we accept. Images are not stored in the page, so real notes are far smaller.</summary>
    public const int MaxContentLength = 2_000_000;

    // Blocks that hold text directly. Each one becomes its own line.
    private static readonly HashSet<string> TextBlocks = ["paragraph", "heading", "codeBlock"];

    /// <summary>The text of the page, one line per paragraph, heading or code line. Throws <see cref="AppException"/> if the JSON is not a page.</summary>
    public static string FromContent(string content)
    {
        if (content.Length > MaxContentLength) throw new AppException("This note is too large to save.");
        JsonDocument doc;
        try { doc = JsonDocument.Parse(content); }
        catch (JsonException) { throw new AppException("The note content is not valid."); }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var type) || type.GetString() != "doc")
                throw new AppException("The note content is not valid.");
            var sb = new StringBuilder();
            Append(root, sb);
            return sb.ToString().TrimEnd();
        }
    }

    private static void Append(JsonElement node, StringBuilder sb)
    {
        if (node.ValueKind != JsonValueKind.Object) return;
        if (node.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String) { sb.Append(text.GetString()); return; }
        var type = node.TryGetProperty("type", out var t) ? t.GetString() : null;
        if (type == "hardBreak") { sb.Append('\n'); return; }
        if (node.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            foreach (var child in content.EnumerateArray()) Append(child, sb);
        if (type is not null && TextBlocks.Contains(type)) sb.Append('\n');
    }
}
