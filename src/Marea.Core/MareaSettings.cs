using System.Text.Json;

namespace Marea.Core;

/// <summary>User settings, stored as JSON in %LOCALAPPDATA%\Marea\settings.json.</summary>
public sealed class MareaSettings
{
    /// <summary>Folders Marea is allowed to manage. Nothing outside them is ever touched.</summary>
    public List<string> ManagedRoots { get; set; } = [];

    /// <summary>Free space the tide keeps on each volume, in GiB.</summary>
    public double FreeSpaceTargetGb { get; set; } = 100;

    /// <summary>When true, the tide evaporates automatically below the target. Off by default.</summary>
    public bool AutoTide { get; set; }

    /// <summary>Files used or modified more recently than this are never evaporated.</summary>
    public int MinIdleDays { get; set; } = 7;

    /// <summary>Files smaller than this are ignored.</summary>
    public long MinFileSizeMb { get; set; } = 50;

    /// <summary>Extra public Hugging Face repositories to check (owner/name), on top of the discovered ones.</summary>
    public List<string> HuggingFaceRepos { get; set; } = [];

    /// <summary>Discover repositories from local evidence (HF cache, README base_model, config
    /// _name_or_path, git remotes, Ollama hf.co models) and the same authors' other repos.
    /// Sends only public repository and author names found on disk.</summary>
    public bool DiscoverHubRepos { get; set; } = true;

    /// <summary>Also search the Hub by the names of local model folders. Sends folder names: off by default.</summary>
    public bool HubNameSearch { get; set; }

    /// <summary>Use local Ollama manifests as sources (blobs re-obtainable from their registry by digest).</summary>
    public bool OllamaSource { get; set; } = true;

    /// <summary>Check GitHub once a day for a newer release (notification only, never installs).</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>When the last update check ran (UTC).</summary>
    public DateTime LastUpdateCheckUtc { get; set; }

    /// <summary>Once a day, check that public sources still publish the models (on by default).</summary>
    public bool WatchSources { get; set; } = true;

    /// <summary>Bring back evaporated models whose public source is disappearing, and copy models that
    /// may now be the last copy to the mirror (when one is set). On by default.</summary>
    public bool RescueAtRisk { get; set; } = true;

    /// <summary>Last source watch.</summary>
    public DateTime LastWatchUtc { get; set; }

    /// <summary>Programs (executable paths as Windows reports them) not allowed to bring evaporated
    /// files back: their reads fail, files stay evaporated.</summary>
    public List<string> BlockedReaders { get; set; } = [];

    /// <summary>Programs the user said may read many evaporated files without an alert.</summary>
    public List<string> AllowedReaders { get; set; } = [];

    /// <summary>Newest version announced by the last check, if newer than the running one.</summary>
    public string? AvailableUpdate { get; set; }

    /// <summary>Optional mirror folder (e.g. a NAS share) for the content-addressed store.</summary>
    public string? MirrorRoot { get; set; }

    /// <summary>Download speed used to estimate re-download cost, in MB/s.</summary>
    public double BandwidthMBps { get; set; } = 50;

    /// <summary>Start the agent at sign-in (needed to bring evaporated files back).</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>UI language code (it, en, es, fr, de, pt) or "auto" to follow Windows.</summary>
    public string Language { get; set; } = "auto";

    /// <summary>Colour theme: "system", "light" or "dark".</summary>
    public string Theme { get; set; } = "system";

    /// <summary>True once the first-run guide has been completed.</summary>
    public bool OnboardingDone { get; set; }

    /// <summary>Default location of the settings file.</summary>
    public static string DefaultDirectory =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Marea");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Loads settings from <paramref name="dir"/>; returns defaults when absent or unreadable.</summary>
    public static MareaSettings Load(string dir)
    {
        var path = System.IO.Path.Combine(dir, "settings.json");
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<MareaSettings>(File.ReadAllText(path)) ?? new MareaSettings()
                : new MareaSettings();
        }
        catch (JsonException)
        {
            return new MareaSettings();
        }
    }

    /// <summary>Saves atomically (write temp, then replace).</summary>
    public void Save(string dir)
    {
        Directory.CreateDirectory(dir);
        var path = System.IO.Path.Combine(dir, "settings.json");
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        File.Move(tmp, path, overwrite: true);
    }
}
