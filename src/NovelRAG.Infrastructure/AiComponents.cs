using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using NovelRAG.Application;

namespace NovelRAG.Infrastructure;

public sealed record DownloadAsset(string Url, long Size, string Sha256);
public sealed record ComponentFile(string Path, long Size, string Sha256);
public sealed record RuntimeAsset(string Version, string Url, long Size, string Sha256, ComponentFile[] Files);
public sealed record ModelAsset(string Name, string Digest, string Manifest, long Size)
{
    public string ManifestPath => System.IO.Path.Combine("manifests", "registry.ollama.ai", "library", Name.Split(':')[0], Name.Split(':')[1]);
    public IReadOnlyList<ComponentFile> Blobs
    {
        get
        {
            using var doc = JsonDocument.Parse(Manifest);
            return doc.RootElement.GetProperty("layers").EnumerateArray().Append(doc.RootElement.GetProperty("config"))
                .Select(e => new ComponentFile("blobs/" + e.GetProperty("digest").GetString()!.Replace(':', '-'), e.GetProperty("size").GetInt64(), e.GetProperty("digest").GetString()![7..])).ToArray();
        }
    }
}
public sealed record ComponentCatalog(int Version, RuntimeAsset Runtime, ModelAsset[] Models)
{
    public static ComponentCatalog Load()
    {
        using var stream = typeof(ComponentCatalog).Assembly.GetManifestResourceStream("NovelRAG.Infrastructure.ai-components.json")!;
        return JsonSerializer.Deserialize<ComponentCatalog>(stream)!;
    }
}
public sealed class AiStateStore(string path)
{
    public async Task<AiPreparationStatus?> LoadAsync(CancellationToken ct = default)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<AiPreparationStatus>(await File.ReadAllTextAsync(path, ct)) : null; }
        catch (JsonException) { return new(AiStage.Paused, "准备记录需要重新检查，已有组件会保留"); }
    }
    public async Task SaveAsync(AiPreparationStatus state, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(state), ct);
        File.Move(path + ".tmp", path, true);
    }
}
public static class ComponentFiles
{
    public static async Task<bool> ValidAsync(string path, long size, string hash, CancellationToken ct)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != size) return false;
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(file, ct)).Equals(hash, StringComparison.OrdinalIgnoreCase);
    }
    public static string SafeChild(string directory, string relative)
    {
        if (Path.IsPathRooted(relative) || relative.Contains(':') || relative.Split('/', '\\').Any(p => p == "..")) throw new InvalidDataException("组件路径无效。");
        var root = Path.GetFullPath(directory); var target = Path.GetFullPath(Path.Combine(root, relative));
        if (!target.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("组件路径越界。");
        for (var current = target; current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("AI 组件目录不能包含链接或重解析点，请选择普通本机文件夹。");
        return target;
    }
    public static async Task ExtractAsync(string zipPath, string stage, IReadOnlyList<ComponentFile> expected, CancellationToken ct)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            SafeChild(stage, entry.FullName);
            if (entry.Name.Length == 0) continue;
            if (!entries.TryAdd(entry.FullName.Replace('\\', '/'), entry)) throw new InvalidDataException("组件包有重复文件。");
        }
        if (entries.Count != expected.Count) throw new InvalidDataException("组件包文件清单不匹配。");
        foreach (var item in expected)
        {
            ct.ThrowIfCancellationRequested();
            if (!entries.TryGetValue(item.Path.Replace('\\', '/'), out var entry) || entry.Length != item.Size) throw new InvalidDataException("组件包大小不匹配。");
            var target = SafeChild(stage, item.Path); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (await ValidAsync(target, item.Size, item.Sha256, ct)) continue;
            await using (var input = entry.Open())
            await using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 131072, true))
                await input.CopyToAsync(output, ct);
            if (!await ValidAsync(target, item.Size, item.Sha256, ct)) throw new InvalidDataException("解压后的组件校验失败。");
        }
    }
}
