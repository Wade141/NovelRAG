using System.Text;
using System.Text.Json;
using NovelRAG.Application;
using NovelRAG.Domain;

namespace NovelRAG.Infrastructure;

public sealed class JsonExporter : IExporter
{
    public Task ExportAsync(ExportDocument data, string path, CancellationToken ct = default) =>
        AtomicExport.WriteAsync(path, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }), ct);
}
public sealed class MarkdownExporter : IExporter
{
    public Task ExportAsync(ExportDocument data, string path, CancellationToken ct = default)
    {
        var output = new StringBuilder("# NovelRAG 灵感导出\n\n");
        foreach (var group in data.Ideas.GroupBy(i => i.ProjectId))
        {
            output.AppendLine("# " + (data.Projects.FirstOrDefault(p => p.Id == group.Key)?.Name ?? "未分类"));
            foreach (var idea in group)
            {
                output.AppendLine($"\n## {idea.CreatedAt:O} · #{idea.Id}{(idea.Archived ? " · 已归档" : "")}\n");
                output.AppendLine("标签：" + string.Join(" ", idea.Tags.Select(t => "#" + t.Name)));
                output.AppendLine($"\n更新：{idea.UpdatedAt:O}\n\n{idea.Content}\n");
            }
        }
        return AtomicExport.WriteAsync(path, output.ToString(), ct);
    }
}
internal static class AtomicExport
{
    public static async Task WriteAsync(string path, string content, CancellationToken ct)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(temp, content, new UTF8Encoding(false), ct); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
