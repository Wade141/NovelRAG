using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using NovelRAG.Infrastructure;
using Xunit;

namespace NovelRAG.Tests;

public sealed class ComponentTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "NovelRAG-components-" + Guid.NewGuid());
    public ComponentTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public void Published_catalog_pins_manifest_bytes_and_all_model_layers()
    {
        var catalog = ComponentCatalog.Load();
        Assert.NotEmpty(catalog.Runtime.Files);
        foreach (var model in catalog.Models)
        {
            Assert.Equal(model.Digest, Hash(Encoding.UTF8.GetBytes(model.Manifest)), ignoreCase: true);
            Assert.Equal(model.Size, model.Blobs.Sum(b => b.Size));
        }
    }

    [Fact]
    public async Task Model_import_checks_contents_and_keeps_source_unchanged()
    {
        var source = Path.Combine(root, "source"); var target = Path.Combine(root, "target");
        var good = Encoding.UTF8.GetBytes("model weights"); var digest = Hash(good).ToLowerInvariant();
        var manifest = "{\"config\":{\"digest\":\"sha256:" + digest + "\",\"size\":13},\"layers\":[]}";
        var model = new ModelAsset("fixture:v1", Hash(Encoding.UTF8.GetBytes(manifest)), manifest, 13);
        Directory.CreateDirectory(Path.Combine(source, "blobs"));
        await File.WriteAllBytesAsync(Path.Combine(source, "blobs", "sha256-" + digest), good);
        Assert.True(await AiComponentInstaller.ImportModelAsync(model, source, target, null, default));
        Assert.True(await AiComponentInstaller.ModelValidAsync(model, target, default));
        Assert.Equal(good, await File.ReadAllBytesAsync(Path.Combine(source, "blobs", "sha256-" + digest)));
        await File.WriteAllTextAsync(Path.Combine(target, "blobs", "sha256-" + digest), "broken weight");
        Assert.False(await AiComponentInstaller.ModelValidAsync(model, target, default));
        Assert.True(await AiComponentInstaller.ImportModelAsync(model, source, target, null, default));
        Assert.True(await AiComponentInstaller.ModelValidAsync(model, target, default));
    }

    [Fact]
    public async Task Resume_space_estimate_deducts_stage_download_and_model_partial_files()
    {
        var runtime = new RuntimeAsset("fixture", "https://example.com/engine", 100, new string('0', 64), [new("ollama.exe", 80, new string('0', 64))]);
        var model = new ModelAsset("fixture:v1", new string('0', 64), "{\"config\":{\"digest\":\"sha256:abc\",\"size\":60},\"layers\":[]}", 60);
        var catalog = new ComponentCatalog(1, runtime, [model]);
        using var http = new HttpClient(); var installer = new AiComponentInstaller(catalog, new(http));
        var before = installer.Inspect(root).AdditionalBytes;
        Directory.CreateDirectory(Path.Combine(root, "downloads")); Directory.CreateDirectory(Path.Combine(root, "runtime", "fixture.stage")); Directory.CreateDirectory(Path.Combine(root, "models", "blobs"));
        await File.WriteAllBytesAsync(Path.Combine(root, "downloads", "ollama-fixture.zip.part"), new byte[40]);
        await File.WriteAllBytesAsync(Path.Combine(root, "runtime", "fixture.stage", "ollama.exe"), new byte[30]);
        await File.WriteAllBytesAsync(Path.Combine(root, "models", "blobs", "sha256-abc-partial"), new byte[20]);
        Assert.Equal(before - 90, installer.Inspect(root).AdditionalBytes);
    }

    [Fact]
    public async Task Offline_preflight_does_not_require_missing_model_install_space()
    {
        var settings = new AppSettings { ManagedAi = true, AiSetupChosen = true, AiEnabled = true };
        using var backend = new NovelRAG.AI.Ollama.AiPreparationBackend(root, () => settings, change => { settings = change(settings); return Task.CompletedTask; },
            (_, _) => Task.FromResult(new NovelRAG.Domain.IndexProgress(0, 0, 0)), new SilentLog(), _ => 1);
        Assert.False(backend.Inspect().CanPrepare);
        await backend.CheckAsync(false, new NovelRAG.Application.InlineProgress<NovelRAG.Application.AiTransferProgress>(_ => { }), default);
    }
    private sealed class SilentLog : NovelRAG.Application.IAppLog { public void Write(string name, Exception? error = null, object? data = null) { } }

    [Fact]
    public async Task Interrupted_download_resumes_with_validator_and_verifies_before_activation()
    {
        var bytes = Encoding.UTF8.GetBytes("a verified component"); var path = Path.Combine(root, "engine.zip");
        await File.WriteAllBytesAsync(path + ".part", bytes[..5]);
        await File.WriteAllTextAsync(path + ".etag", "\"version-one\"");
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal(5, request.Headers.Range!.Ranges.Single().From);
            Assert.Equal("\"version-one\"", request.Headers.IfRange!.EntityTag!.Tag);
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(bytes[5..]) };
            response.Headers.ETag = new("\"version-one\""); response.Content.Headers.ContentRange = new(5, bytes.Length - 1, bytes.Length); return response;
        }));
        await new VerifiedDownloader(http).DownloadAsync(new("https://example.com/engine", bytes.Length, Hash(bytes)), path, null, default);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.False(File.Exists(path + ".part"));
    }

    [Fact]
    public async Task Server_ignoring_range_restarts_only_partial_file()
    {
        var bytes = Encoding.UTF8.GetBytes("new version"); var path = Path.Combine(root, "engine");
        await File.WriteAllBytesAsync(path + ".part", [1, 2]); await File.WriteAllTextAsync(path + ".etag", "\"old\"");
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));
        await new VerifiedDownloader(http).DownloadAsync(new("https://example.com/engine", bytes.Length, Hash(bytes)), path, null, default);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Corrupt_download_never_replaces_good_destination()
    {
        var path = Path.Combine(root, "engine"); await File.WriteAllTextAsync(path, "existing");
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) }));
        await Assert.ThrowsAsync<InvalidDataException>(() => new VerifiedDownloader(http).DownloadAsync(new("https://example.com/engine", 3, Hash([4, 5, 6])), path, null, default));
        Assert.Equal("existing", await File.ReadAllTextAsync(path));
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("C:/outside.txt")]
    [InlineData("safe/file:stream")]
    public async Task Archive_cannot_escape_or_write_alternate_stream(string entry)
    {
        var zip = Path.Combine(root, "bad.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        { using var writer = new StreamWriter(archive.CreateEntry(entry).Open()); writer.Write("bad"); }
        await Assert.ThrowsAsync<InvalidDataException>(() => ComponentFiles.ExtractAsync(zip, Path.Combine(root, "stage"), [], default));
        Assert.False(File.Exists(Path.Combine(root, "outside.txt")));
    }

    [Fact]
    public async Task State_roundtrip_is_atomic_and_old_settings_remain_external()
    {
        var store = new AiStateStore(Path.Combine(root, "state.json"));
        await store.SaveAsync(new(NovelRAG.Application.AiStage.Paused, "paused"), default);
        Assert.Equal(NovelRAG.Application.AiStage.Paused, (await store.LoadAsync())!.Stage);
        Assert.False(File.Exists(Path.Combine(root, "state.json.tmp")));
        var path = Path.Combine(root, "settings.json"); await File.WriteAllTextAsync(path, "{\"AiEnabled\":true}");
        var settings = await new ConfigurationStore(path).LoadAsync();
        Assert.False(settings.ManagedAi);
        Assert.True(settings.AiEnabled);
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> action) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(action(request)); }
}
