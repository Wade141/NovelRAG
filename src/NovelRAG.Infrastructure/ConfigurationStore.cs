using System.Text.Json;

namespace NovelRAG.Infrastructure;

public sealed record AppSettings
{
    public int Version { get; init; } = 1;
    public bool PetVisible { get; init; } = true;
    public double PetScale { get; init; } = 1;
    public bool EdgeSnap { get; init; } = true;
    public double? PetX { get; init; }
    public double? PetY { get; init; }
    public string? MonitorName { get; init; }
    public long? LastProjectId { get; init; }
    public string Endpoint { get; init; } = "http://127.0.0.1:11434";
    public string EmbeddingModel { get; init; } = "embeddinggemma:latest";
    public string ChatModel { get; init; } = "qwen3:4b";
    public bool AiEnabled { get; init; }
    public bool ManagedAi { get; init; }
    public bool AiSetupChosen { get; init; }
    public string? AiDirectory { get; init; }
    public string ModelsPath { get; init; } = Environment.GetEnvironmentVariable("OLLAMA_MODELS") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ollama", "models");
    public void Validate()
    {
        if (Version != 1) throw new ArgumentException("设置版本不兼容。");
        if (AiDirectory is not null && (!Path.IsPathFullyQualified(AiDirectory) || AiDirectory.StartsWith("\\\\", StringComparison.Ordinal)))
            throw new ArgumentException("AI 组件需要保存在本机磁盘的完整路径中。");
        if (!double.IsFinite(PetScale) || PetScale is < 0.6 or > 2) throw new ArgumentException("桌宠缩放范围为 0.6–2。");
        if (PetX is not null && !double.IsFinite(PetX.Value) || PetY is not null && !double.IsFinite(PetY.Value)) throw new ArgumentException("桌宠位置无效。");
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri) || !uri.IsLoopback || uri.Scheme != "http" || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.AbsolutePath != "/")
            throw new ArgumentException("V1 只支持本机 HTTP Ollama 地址，例如 http://127.0.0.1:11434。");
        foreach (var model in new[] { EmbeddingModel, ChatModel })
            if (string.IsNullOrWhiteSpace(model) || model.Length > 200 || model.Contains("cloud", StringComparison.OrdinalIgnoreCase) || model.Contains('/') || model.Any(char.IsWhiteSpace))
                throw new ArgumentException("请使用已安装的本地模型名称，不能选择云端模型。");
    }
}
public sealed class ConfigurationStore(string path)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task<AppSettings> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(path)) return new();
        var settings = JsonSerializer.Deserialize<AppSettings>(await File.ReadAllTextAsync(path, ct)) ?? throw new InvalidDataException("设置文件为空。");
        settings.Validate(); return settings;
    }
    public async Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        settings.Validate(); await gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }), ct);
            File.Move(path + ".tmp", path, true);
        }
        finally { gate.Release(); }
    }
}
