using NovelRAG.Application;
using NovelRAG.Infrastructure;
using Xunit;

namespace NovelRAG.Tests;

public sealed class RetrievalOptionsTests
{
    public static IEnumerable<object[]> InvalidOptions()
    {
        yield return [new RetrievalOptions { VectorWeight = -1 }];
        yield return [new RetrievalOptions { FtsWeight = double.NaN }];
        yield return [new RetrievalOptions { MetadataWeight = double.PositiveInfinity }];
        yield return [new RetrievalOptions { VectorWeight = 0, FtsWeight = 0, MetadataWeight = 0 }];
        yield return [new RetrievalOptions { RrfK = 0 }];
        yield return [new RetrievalOptions { RrfK = double.NaN }];
        yield return [new RetrievalOptions { MinSimilarity = -0.1 }];
        yield return [new RetrievalOptions { MinSimilarity = 1.1 }];
        yield return [new RetrievalOptions { MinSimilarity = double.NaN }];
        yield return [new RetrievalOptions { SearchTopK = 0 }];
        yield return [new RetrievalOptions { SearchTopK = 101 }];
        yield return [new RetrievalOptions { RagTopK = 0 }];
        yield return [new RetrievalOptions { RagTopK = 21 }];
        yield return [new RetrievalOptions { SearchTopK = 5, RagTopK = 6 }];
    }
    [Theory, MemberData(nameof(InvalidOptions))]
    public void Invalid_values_are_rejected(RetrievalOptions options) => Assert.Throws<ArgumentException>(options.Validate);

    [Fact]
    public void Defaults_and_large_finite_weights_normalize_without_overflow()
    {
        var defaults = new RetrievalOptions(); defaults.Validate();
        Assert.Equal((0.5, 0.35, 0.15), defaults.NormalizedWeights());
        Assert.Equal((0.5, 0.5, 0.0), new RetrievalOptions { VectorWeight = double.MaxValue, FtsWeight = double.MaxValue, MetadataWeight = 0 }.NormalizedWeights());
    }
    [Fact]
    public async Task Old_settings_get_defaults_and_invalid_save_preserves_file()
    {
        var root = Path.Combine(Path.GetTempPath(), "NovelRAG-options-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "settings.json"); await File.WriteAllTextAsync(path, "{\"Version\":1,\"PetScale\":1.2}");
            var store = new ConfigurationStore(path); var original = await store.LoadAsync();
            Assert.Equal(new RetrievalOptions(), original.Retrieval);
            var changed = original with { Retrieval = new RetrievalOptions { SearchTopK = 12, RagTopK = 4, MetadataWeight = 2 } };
            await store.SaveAsync(changed); Assert.Equal(changed, await new ConfigurationStore(path).LoadAsync());
            var before = await File.ReadAllTextAsync(path);
            await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(changed with { Retrieval = changed.Retrieval with { VectorWeight = -1 } }));
            await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(changed with { Retrieval = null! }));
            Assert.Equal(before, await File.ReadAllTextAsync(path));
        }
        finally { Directory.Delete(root, true); }
    }
}
