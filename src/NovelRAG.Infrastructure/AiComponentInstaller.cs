using System.Text;
using NovelRAG.Application;

namespace NovelRAG.Infrastructure;

public sealed record AiPreflight(long DownloadBytes, long InstalledBytes, long AdditionalBytes, long FreeBytes, string Directory, bool CanPrepare, string Message, bool Supported = true);

public sealed class AiComponentInstaller(ComponentCatalog catalog, VerifiedDownloader downloader, Func<string, long>? availableSpace = null)
{
    public static IReadOnlyList<string> KnownSources()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            var dir = new DirectoryInfo(start);
            for (var n = 0; n < 7 && dir is not null; n++, dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "NovelRAG.sln"))) result.Add(Path.Combine(dir.FullName, ".tools"));
        }
        return result.ToArray();
    }
    public AiPreflight Inspect(string root)
    {
        root = Path.GetFullPath(root); ComponentFiles.SafeChild(root, "runtime");
        Directory.CreateDirectory(root);
        var probe = Path.Combine(root, ".write-check-" + Guid.NewGuid().ToString("N")); File.WriteAllText(probe, ""); File.Delete(probe);
        var runtimeDir = Path.Combine(root, "runtime", catalog.Runtime.Version);
        var runtimeMissing = catalog.Runtime.Files.Any(f => !HasSize(Path.Combine(runtimeDir, f.Path), f.Size));
        var sources = KnownSources();
        var zip = Path.Combine(root, "downloads", "ollama-" + catalog.Runtime.Version + ".zip");
        var importZip = !File.Exists(zip + ".part") && sources.Any(s => HasSize(Path.Combine(s, "ollama-windows-amd64.zip"), catalog.Runtime.Size));
        var reusableZip = HasSize(zip, catalog.Runtime.Size) || importZip;
        long download = runtimeMissing && !reusableZip ? RemainingWrite(zip + ".part", catalog.Runtime.Size) : 0;
        long additional = runtimeMissing ? RuntimeAdditional(root, HasSize(zip, catalog.Runtime.Size), importZip) : 0;
        foreach (var blob in catalog.Models.SelectMany(m => m.Blobs).DistinctBy(b => b.Path))
        {
            if (HasSize(Path.Combine(root, "models", blob.Path), blob.Size)) continue;
            var path = Path.Combine(root, "models", blob.Path);
            var importing = !File.Exists(path + "-partial") && sources.Any(s => HasSize(Path.Combine(s, "models", blob.Path), blob.Size));
            additional += RemainingWrite(path + (importing ? ".import" : "-partial"), blob.Size);
            if (!sources.Any(s => HasSize(Path.Combine(s, "models", blob.Path), blob.Size))) download += blob.Size;
        }
        var installed = catalog.Runtime.Files.Sum(f => f.Size) + catalog.Models.Sum(m => m.Size);
        var free = availableSpace?.Invoke(root) ?? new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace;
        additional += SafetyReserve;
        return new(download, installed, additional, free, root, free >= additional,
            free >= additional ? "准备完成后可离线使用；已有文件会再次校验，损坏时需补充下载。" : "磁盘空间不足，请释放空间或选择其他磁盘。");
    }
    private static bool HasSize(string path, long size) => File.Exists(path) && new FileInfo(path).Length == size;
    private const long SafetyReserve = 512L * 1024 * 1024;
    private static long RemainingWrite(string path, long size)
    {
        // Existing temporary files will be reused or truncated before writing, never duplicated.
        ComponentFiles.SafeChild(Path.GetDirectoryName(Path.GetFullPath(path))!, Path.GetFileName(path));
        return Math.Max(0, size - (File.Exists(path) ? Math.Min(size, new FileInfo(path).Length) : 0));
    }
    private long RuntimeAdditional(string root, bool zipReady, bool importing)
    {
        var stage = Path.Combine(root, "runtime", catalog.Runtime.Version + ".stage");
        var zip = Path.Combine(root, "downloads", "ollama-" + catalog.Runtime.Version + ".zip");
        return catalog.Runtime.Files.Sum(f => RemainingWrite(ComponentFiles.SafeChild(stage, f.Path), f.Size)) +
            (zipReady ? 0 : RemainingWrite(zip + (importing ? ".import" : ".part"), catalog.Runtime.Size));
    }
    private void EnsureSpace(string root, long additional)
    {
        var free = availableSpace?.Invoke(root) ?? new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))!).AvailableFreeSpace;
        if (free < additional + SafetyReserve) throw new InvalidOperationException($"磁盘空间不足，此步骤还需要约 {(additional + SafetyReserve) / (1024d * 1024 * 1024):0.0} GB 空间。请释放空间后继续；已可用的功能不受影响。");
    }
    public void EnsureModelSpace(ModelAsset model, string directory, bool allowImports = true)
    {
        var sources = KnownSources(); long needed = 0;
        foreach (var blob in model.Blobs)
        {
            var target = ComponentFiles.SafeChild(directory, blob.Path);
            if (HasSize(target, blob.Size)) continue;
            var importing = allowImports && !File.Exists(target + "-partial") && sources.Any(s => HasSize(Path.Combine(s, "models", blob.Path), blob.Size));
            needed += RemainingWrite(target + (importing ? ".import" : "-partial"), blob.Size);
        }
        EnsureSpace(directory, needed);
    }
    public string RuntimeDirectory(string root) => Path.Combine(root, "runtime", catalog.Runtime.Version);
    public async Task<string> PrepareRuntimeAsync(string root, bool allowDownload, IProgress<AiTransferProgress> progress, CancellationToken ct)
    {
        var target = ComponentFiles.SafeChild(root, "runtime/" + catalog.Runtime.Version);
        progress.Report(new("正在检查已有引擎…"));
        if (await RuntimeValidAsync(target, ct)) return Path.Combine(target, "ollama.exe");
        if (!allowDownload) throw new InvalidOperationException("引擎尚未准备好，请点击继续准备。");
        var zip = ComponentFiles.SafeChild(root, "downloads/ollama-" + catalog.Runtime.Version + ".zip");
        var zipReady = await ComponentFiles.ValidAsync(zip, catalog.Runtime.Size, catalog.Runtime.Sha256, ct);
        string? importSource = null;
        // Prefer existing partial transfer; otherwise only a verified import counts as reusable.
        if (!zipReady && !File.Exists(zip + ".part"))
            foreach (var source in KnownSources())
            {
                var cached = Path.Combine(source, "ollama-windows-amd64.zip");
                if (await ComponentFiles.ValidAsync(cached, catalog.Runtime.Size, catalog.Runtime.Sha256, ct)) { importSource = cached; break; }
            }
        EnsureSpace(root, RuntimeAdditional(root, zipReady, importSource is not null));
        if (!zipReady && importSource is not null)
        {
            progress.Report(new("正在复用已下载的引擎…"));
            await CopyVerifiedAsync(importSource, zip, catalog.Runtime.Size, catalog.Runtime.Sha256, ct);
        }
        await downloader.DownloadAsync(new(catalog.Runtime.Url, catalog.Runtime.Size, catalog.Runtime.Sha256), zip, progress, ct);
        var stage = ComponentFiles.SafeChild(root, "runtime/" + catalog.Runtime.Version + ".stage");
        EnsureSpace(root, catalog.Runtime.Files.Sum(f => RemainingWrite(ComponentFiles.SafeChild(stage, f.Path), f.Size)));
        progress.Report(new("正在安装并校验引擎…"));
        await ComponentFiles.ExtractAsync(zip, stage, catalog.Runtime.Files, ct);
        ct.ThrowIfCancellationRequested();
        // Both directories are app-owned children of the verified runtime root.
        if (Directory.Exists(target))
        {
            var previous = ComponentFiles.SafeChild(root, "runtime/" + catalog.Runtime.Version + ".previous-" + Guid.NewGuid().ToString("N"));
            Directory.Move(target, previous);
            try { Directory.Move(stage, target); }
            catch { Directory.Move(previous, target); throw; }
            DeleteOwnedTree(previous);
        }
        else Directory.Move(stage, target);
        File.Delete(zip); // Verified installed files replace this recoverable download cache.
        return Path.Combine(target, "ollama.exe");
    }
    public async Task<bool> RuntimeValidAsync(string directory, CancellationToken ct)
    {
        foreach (var file in catalog.Runtime.Files)
            if (!await ComponentFiles.ValidAsync(ComponentFiles.SafeChild(directory, file.Path), file.Size, file.Sha256, ct)) return false;
        return true;
    }
    private static void DeleteOwnedTree(string path)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("组件目录出现链接，请检查目录。");
            if (Directory.Exists(entry)) DeleteOwnedTree(entry); else File.Delete(entry);
        }
        Directory.Delete(path);
    }
    public static async Task<bool> ModelValidAsync(ModelAsset model, string directory, CancellationToken ct)
    {
        var manifest = ComponentFiles.SafeChild(directory, model.ManifestPath);
        if (!await ComponentFiles.ValidAsync(manifest, Encoding.UTF8.GetByteCount(model.Manifest), model.Digest, ct)) return false;
        foreach (var blob in model.Blobs)
            if (!await ComponentFiles.ValidAsync(ComponentFiles.SafeChild(directory, blob.Path), blob.Size, blob.Sha256, ct)) return false;
        return true;
    }
    public static async Task<bool> ImportModelAsync(ModelAsset model, string source, string directory, IProgress<AiTransferProgress>? progress, CancellationToken ct)
    {
        foreach (var blob in model.Blobs)
        {
            var target = ComponentFiles.SafeChild(directory, blob.Path);
            if (await ComponentFiles.ValidAsync(target, blob.Size, blob.Sha256, ct)) continue;
            if (File.Exists(target + "-partial")) return false; // Resume the network layer rather than allocate a second copy.
            var original = ComponentFiles.SafeChild(source, blob.Path);
            if (!await ComponentFiles.ValidAsync(original, blob.Size, blob.Sha256, ct)) return false;
            progress?.Report(new("正在校验并复用已有模型组件…"));
            await CopyVerifiedAsync(original, target, blob.Size, blob.Sha256, ct);
        }
        var manifest = ComponentFiles.SafeChild(directory, model.ManifestPath); Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
        await File.WriteAllTextAsync(manifest + ".tmp", model.Manifest, new UTF8Encoding(false), ct); File.Move(manifest + ".tmp", manifest, true); return true;
    }
    private static async Task CopyVerifiedAsync(string source, string target, long size, string hash, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true))
        await using (var output = new FileStream(target + ".import", FileMode.Create, FileAccess.Write, FileShare.None, 131072, true))
            await input.CopyToAsync(output, ct);
        if (!await ComponentFiles.ValidAsync(target + ".import", size, hash, ct)) throw new InvalidDataException("复用组件校验失败。");
        File.Move(target + ".import", target, true);
    }
}
