using System.IO.Compression;
using System.Text;
using Marea.Core;

namespace Marea.Tests;

/// <summary>Header reading, pickle safety check and grouping of files into models.</summary>
public sealed class ModelInspectionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "marea-inspect-" + Guid.NewGuid().ToString("N")[..8]);

    public ModelInspectionTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    // os.system pickled by Python on Windows (protocol 4): nt.system via STACK_GLOBAL + MEMOIZE.
    private static readonly byte[] OsSystemPickle = Convert.FromHexString(
        "8004951f000000000000008c026e74948c0673797374656d9493948c076563686f20686994859452942e");

    // collections.OrderedDict(a=1), protocol 2 (GLOBAL opcode).
    private static readonly byte[] OrderedDictPickle = Convert.FromHexString(
        "800263636f6c6c656374696f6e730a4f726465726564446963740a71002952710158010000006171024b01732e");

    private static byte[] TorchLikePickle() =>
        [0x80, 0x02, (byte)'c', .. "torch._utils\n_rebuild_tensor_v2\n"u8, (byte)'q', 0, (byte)')', (byte)'R', (byte)'.'];

    private string Write(string name, byte[] data)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllBytes(p, data);
        return p;
    }

    [Fact]
    public void PickleThatRunsACommandIsDangerous()
    {
        var meta = ModelInspector.Inspect(Write("evil.pkl", OsSystemPickle));
        Assert.Equal(SafetyVerdict.Dangerous, meta.Safety);
        Assert.Contains("nt.system", meta.Findings);
    }

    [Fact]
    public void StackGlobalResolvesMemoisedStrings()
    {
        // "builtins" memoised, fetched back with BINGET, then STACK_GLOBAL with "eval".
        byte[] data = [0x80, 4, 0x8c, 8, .. "builtins"u8, 0x94, 0x8c, 1, (byte)'x', (byte)'0',
            (byte)'h', 0, 0x8c, 4, .. "eval"u8, 0x93, (byte)'.'];
        var rep = PickleScanner.Scan(new MemoryStream(data));
        Assert.True(rep.Complete);
        Assert.Contains(("builtins", "eval"), rep.Imports);
        Assert.Equal(SafetyVerdict.Dangerous, ImportRating.Rate(rep.Imports));
    }

    [Fact]
    public void StandardTorchCheckpointHasNoCode()
    {
        var path = Path.Combine(_dir, "model.pt");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using (var s = zip.CreateEntry("archive/data.pkl").Open())
            {
                s.Write(TorchLikePickle());
            }
            using (var s = zip.CreateEntry("archive/data/0").Open())
            {
                s.Write(new byte[64]);
            }
        }
        var meta = ModelInspector.Inspect(path);
        Assert.Equal("PyTorch", meta.Format);
        Assert.Equal(SafetyVerdict.NoCodeFound, meta.Safety);
        Assert.Empty(meta.Findings);
    }

    [Fact]
    public void PlainOrderedDictPickleHasNoCode() =>
        Assert.Equal(SafetyVerdict.NoCodeFound, ModelInspector.Inspect(Write("state.pkl", OrderedDictPickle)).Safety);

    [Fact]
    public void UnknownImportIsSuspiciousNotDangerous()
    {
        byte[] data = [0x80, 2, (byte)'c', .. "pytorch_lightning.callbacks\nModelCheckpoint\n"u8, (byte)'.'];
        var meta = ModelInspector.Inspect(Write("lightning.ckpt", data));
        Assert.Equal(SafetyVerdict.Suspicious, meta.Safety);
    }

    [Fact]
    public void TruncatedPickleIsSuspicious()
    {
        var meta = ModelInspector.Inspect(Write("cut.pt", TorchLikePickle()[..12]));
        Assert.Equal(SafetyVerdict.Suspicious, meta.Safety);
        Assert.Contains("could not be read to the end", meta.Findings);
    }

    [Fact]
    public void GgufHeaderGivesArchitectureQuantizationAndParameters()
    {
        var meta = ModelInspector.Inspect(Write("model.gguf", Gguf("{{ messages[0]['content'] }}")));
        Assert.Equal("GGUF", meta.Format);
        Assert.Equal("llama", meta.Architecture);
        Assert.Equal("tiny", meta.Name);
        Assert.Equal("Q4_K_M", meta.Precision);
        Assert.Equal(48, meta.Parameters);
        Assert.Equal(SafetyVerdict.SafeFormat, meta.Safety);
    }

    [Fact]
    public void GgufChatTemplateEscapingTheSandboxIsSuspicious()
    {
        var meta = ModelInspector.Inspect(Write("bad.gguf", Gguf("{{ cycler.__init__.__globals__.os.popen('id') }}")));
        Assert.Equal(SafetyVerdict.Suspicious, meta.Safety);
        Assert.Contains(meta.Findings, f => f.Contains("__globals__", StringComparison.Ordinal));
    }

    [Fact]
    public void GgufWithAbsurdCountsIsRejectedWithoutAllocating()
    {
        var bad = new MemoryStream();
        using (var w = new BinaryWriter(bad, Encoding.UTF8, true))
        {
            w.Write(GgufReader.Magic);
            w.Write(3u);
            w.Write(ulong.MaxValue);
            w.Write(1ul);
        }
        Assert.Throws<InvalidDataException>(() => GgufReader.Read(new MemoryStream(bad.ToArray())));
    }

    [Fact]
    public void RandomDataIsNotAModel() =>
        Assert.Equal(SafetyVerdict.NotAModel, ModelInspector.Inspect(Write("data.bin", Encoding.ASCII.GetBytes("hello world, not a model"))).Safety);

    [Fact]
    public void MetaRoundTripsThroughJson()
    {
        var m = new ModelMeta("GGUF", "llama", "x", "Q8_0", 7, SafetyVerdict.Suspicious, ["a"]);
        var back = ModelMeta.FromJson(m.ToJson())!;
        Assert.Equal(m.Format, back.Format);
        Assert.Equal(m.Safety, back.Safety);
        Assert.Equal(m.Findings, back.Findings);
    }

    [Fact]
    public void CatalogGroupsHubCacheShardsFoldersAndSingleFiles()
    {
        const string root = @"C:\m";
        FileRecord F(string rel, string sha) => new() { Path = Path.Combine(root, rel), Root = root, Size = 10, Sha256 = sha };
        var files = new[]
        {
            F(@"hub\models--Qwen--Qwen3-8B\blobs\aaa", "1"),
            F(@"hub\models--Qwen--Qwen3-8B\blobs\bbb", "2"),
            F(@"llm\big-00001-of-00002.safetensors", "3"),
            F(@"llm\big-00002-of-00002.safetensors", "4"),
            F(@"sdxl\unet\diffusion_pytorch_model.safetensors", "5"),
            F(@"sdxl\vae\diffusion_pytorch_model.safetensors", "6"),
            F(@"loras\style.safetensors", "7"),
        };
        var meta = new Dictionary<string, ModelMeta>
        {
            ["7"] = new("safetensors", null, null, "BF16", 100, SafetyVerdict.SafeFormat, []),
        };
        var models = ModelCatalog.Build(files, meta, (dir, name) => name == "model_index.json" && dir == Path.Combine(root, "sdxl"));
        Assert.Equal(4, models.Count);
        Assert.Equal(2, models.Single(m => m.Name == "Qwen/Qwen3-8B" && m.Kind == ModelKind.HuggingFace).Files.Count);
        Assert.Equal(2, models.Single(m => m.Name == "big").Files.Count);
        Assert.Equal(2, models.Single(m => m.Name == "sdxl" && m.Kind == ModelKind.Folder).Files.Count);
        var lora = models.Single(m => m.Name == "style");
        Assert.Equal(["safetensors"], lora.Formats);
        Assert.Equal(100, lora.Parameters);
        Assert.Equal(SafetyVerdict.SafeFormat, lora.Safety);
    }

    [Fact]
    public void SharedHubBlobsAreNamedAfterTheRepoThatUsesThem()
    {
        // huggingface_hub 2.x: bytes once in hub/blobs/<xx>/<hash>, repos listed in "<hash>.refs".
        var hub = Path.Combine(Path.GetTempPath(), "hub");
        var shared = Path.Combine(hub, "blobs", "78", "789fdf");
        var files = new[]
        {
            new FileRecord { Path = shared, Root = hub, Size = 90, Sha256 = "s" },
            new FileRecord { Path = Path.Combine(hub, "blobs", "12", "nolist"), Root = hub, Size = 5, Sha256 = "n" },
        };
        string? Read(string p) => p == shared + ".refs"
            ? "models--sentence-transformers--all-MiniLM-L6-v2/blobs/53aa51\nmodels--other--repo/blobs/53aa51\n"
            : null;
        var models = ModelCatalog.Build(files, new Dictionary<string, ModelMeta>(), (_, _) => false, readText: Read);
        var m = models.Single(x => x.Kind == ModelKind.HuggingFace);
        Assert.Equal("sentence-transformers/all-MiniLM-L6-v2", m.Name);
        Assert.Equal(Path.Combine(hub, "models--sentence-transformers--all-MiniLM-L6-v2"), m.Folder);
        Assert.Contains(models, x => x.Kind == ModelKind.File && x.Name == "nolist");
    }

    [Fact]
    public void GenericFileNamesTakeTheFolderName()
    {
        const string root = @"C:\m";
        var files = new[]
        {
            new FileRecord { Path = @"C:\m\flux-dev\model.safetensors", Root = root, Size = 1, Sha256 = "a" },
            new FileRecord { Path = @"C:\m\model.safetensors", Root = root, Size = 1, Sha256 = "b" },
        };
        var names = ModelCatalog.Build(files, new Dictionary<string, ModelMeta>(), (_, _) => false).Select(m => m.Name).ToList();
        Assert.Contains("flux-dev", names);
        Assert.Contains("model", names);
    }

    [Fact]
    public void OllamaNamesAreShortForTheLibrary()
    {
        Assert.Equal("llama3.2:3b", OllamaNames.Display(["registry.ollama.ai", "library", "llama3.2", "3b"]));
        Assert.Equal("hf.co/org/model:Q4", OllamaNames.Display(["hf.co", "org", "model", "Q4"]));
    }

    private static byte[] Gguf(string template)
    {
        var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8, true);
        void Str(string s)
        {
            var b = Encoding.UTF8.GetBytes(s);
            w.Write((ulong)b.Length);
            w.Write(b);
        }
        w.Write(GgufReader.Magic);
        w.Write(3u);
        w.Write(2ul); // tensors
        w.Write(5ul); // key-values
        Str("general.architecture"); w.Write(8u); Str("llama");
        Str("general.name"); w.Write(8u); Str("tiny");
        Str("general.file_type"); w.Write(4u); w.Write(15u);
        Str("tokenizer.ggml.tokens"); w.Write(9u); w.Write(8u); w.Write(2ul); Str("a"); Str("b");
        Str("tokenizer.chat_template"); w.Write(8u); Str(template);
        Str("t1"); w.Write(2u); w.Write(4ul); w.Write(8ul); w.Write(0u); w.Write(0ul);
        Str("t2"); w.Write(1u); w.Write(16ul); w.Write(0u); w.Write(128ul);
        w.Flush();
        return ms.ToArray();
    }
}
