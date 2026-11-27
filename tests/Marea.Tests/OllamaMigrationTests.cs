using Marea.Core;

namespace Marea.Tests;

/// <summary>What Ollama 0.40's migration leaves on disk, laid out exactly as a real migrated
/// qwen3.5:9b was: a legacy anchor, a manifest list with ggml and llamacpp children, a hidden shadow tag.</summary>
public sealed class OllamaMigrationTests : IDisposable
{
    private const string ModelType = "application/vnd.ollama.image.model";
    private const string ManifestType = "application/vnd.docker.distribution.manifest.v2+json";
    private static readonly string Original = H('1'), Converted = H('2'), Projector = H('3'), License = H('4'),
        ListBlob = H('5'), GgmlChild = H('6'), LlamaChild = H('7'), Plain = H('8');

    private readonly string _dir = TestFiles.TempDir("ollama");

    public OllamaMigrationTests()
    {
        var layers = (string hex, string type) => $$"""{"mediaType":"{{type}}","digest":"sha256:{{hex}}","size":10}""";
        string Manifest(params string[] l) => $$"""{"schemaVersion":2,"mediaType":"{{ManifestType}}","layers":[{{string.Join(",", l)}}]}""";
        var license = layers(License, "application/vnd.ollama.image.license");
        Tag("registry.ollama.ai/library/qwen3.5/9b", Manifest(layers(Original, ModelType), license,
            layers(ListBlob, "application/vnd.ollama.manifest.list.v2+json"), layers(GgmlChild, ManifestType), layers(LlamaChild, ManifestType)));
        var ggml = $$"""{"mediaType":"{{ManifestType}}","digest":"sha256:{{GgmlChild}}","runner":"ggml","format":"gguf"}""";
        var llama = $$"""{"mediaType":"{{ManifestType}}","digest":"sha256:{{LlamaChild}}","runner":"llamacpp","format":"gguf"}""";
        Blob(ListBlob, $$"""{"schemaVersion":2,"mediaType":"application/vnd.ollama.manifest.list.v2+json","manifests":[{{ggml}},{{llama}}]}""");
        Blob(GgmlChild, Manifest(layers(Original, ModelType), license));
        var converted = new[] { layers(Converted, ModelType), layers(Projector, "application/vnd.ollama.image.projector"), license };
        Blob(LlamaChild, Manifest(converted));
        Tag($"registry.ollama.ai/library/llamacpp/{LlamaChild}", Manifest(converted));
        Tag("registry.ollama.ai/library/gemma3/1b", Manifest(layers(Plain, ModelType)));
    }

    public void Dispose() => Directory.Delete(_dir, true);

    private static string H(char c) => new(c, 64);

    private void Tag(string rel, string json)
    {
        var path = Path.Combine(_dir, "manifests", rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }

    private void Blob(string hex, string json)
    {
        Directory.CreateDirectory(Path.Combine(_dir, "blobs"));
        File.WriteAllText(Path.Combine(_dir, "blobs", "sha256-" + hex), json);
    }

    private FileRecord File_(string hex) =>
        new() { Path = Path.Combine(_dir, "blobs", "sha256-" + hex), Root = _dir, Size = 1 << 30, Sha256 = hex };

    [Fact]
    public void OnlyTheOriginalWeightsOfAMigratedModelAreARollbackCopy()
    {
        var layout = OllamaLayout.Read(_dir);
        Assert.Equal([Original], layout.Rollback);
        Assert.Equal("qwen3.5:9b", layout.Children[Converted].Parent.Name);
    }

    [Fact]
    public void WeightsAnotherTagStillUsesAreNeverARollbackCopy()
    {
        Tag("registry.ollama.ai/library/qwen3.5/old", $$"""{"layers":[{"mediaType":"{{ModelType}}","digest":"sha256:{{Original}}","size":10}]}""");
        Assert.Empty(OllamaLayout.Read(_dir).Rollback);
    }

    [Fact]
    public void ConvertedBlobsAreNamedAfterThePulledTagNotTheHiddenOne()
    {
        var tags = OllamaNames.Read(_dir);
        Assert.Equal(["qwen3.5:9b"], tags[Converted]);
        Assert.Equal(["qwen3.5:9b"], tags[Projector]);
        Assert.Equal(["qwen3.5:9b"], tags[Original]);
        Assert.DoesNotContain(tags.Values.SelectMany(n => n), n => n.StartsWith("llamacpp:", StringComparison.Ordinal));
    }

    [Fact]
    public void HiddenShadowTagsAndManifestLayersAreNotRegistrySources()
    {
        var blobs = OllamaRegistry.ReadManifests(_dir);
        Assert.DoesNotContain(blobs, b => b.Repository == "library/llamacpp");
        Assert.DoesNotContain(blobs, b => b.Sha256 == Converted || b.Sha256 == ListBlob || b.Sha256 == LlamaChild);
        Assert.Contains(blobs, b => b.Sha256 == Original && b.Repository == "library/qwen3.5");
        Assert.Contains(blobs, b => b.Sha256 == Plain);
    }

    [Fact]
    public void TheModelsPageShowsOneModelWithItsRollbackCopy()
    {
        var models = ModelCatalog.Build([File_(Original), File_(Converted), File_(Plain)], new Dictionary<string, ModelMeta>());
        var qwen = Assert.Single(models, m => m.Name == "qwen3.5:9b");
        Assert.Equal(2, qwen.Files.Count);
        Assert.Equal(Original, Assert.Single(qwen.RollbackFiles).Sha256);
        Assert.Empty(Assert.Single(models, m => m.Name == "gemma3:1b").RollbackFiles);
    }

    [Fact]
    public void AFolderWithoutMigrationsHasNoRollbackCopies()
    {
        Directory.Delete(Path.Combine(_dir, "manifests", "registry.ollama.ai", "library", "qwen3.5"), true);
        Assert.Empty(OllamaLayout.Read(_dir).Rollback);
        Assert.Same(OllamaLayout.Empty, OllamaLayout.Read(Path.Combine(_dir, "nowhere")));
    }
}
