namespace Marea.Core;

/// <summary>Finds and records where each content can be obtained from, then classifies files.
/// Every source is a proof (equal sha256, or a recipe that rebuilt the exact bytes).</summary>
/// <remarks><paramref name="ollama"/> confirms blobs that Ollama's migration listed only in child manifests;
/// without it those blobs get no source (the safe answer).</remarks>
public sealed class SourceResolver(Ledger ledger, MareaSettings settings, HuggingFaceClient hub, OllamaRegistry? ollama = null)
{
    /// <summary>Refreshes Hub matches, mirror presence and recipes, then reclassifies every file.</summary>
    public async Task RefreshAsync(IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        var files = ledger.Files().Where(f => f.Sha256.Length == 64).ToList();
        var shas = files.Select(f => f.Sha256).ToHashSet();

        var repos = await CandidateReposAsync(files, progress, ct);
        using (var gate = new SemaphoreSlim(4))
        {
            await Task.WhenAll(repos.Select(async repo =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    progress?.Report(new ScanProgress(Loc.T("phase.hub"), repo, 0, 0));
                    foreach (var hf in await hub.ListLfsFilesAsync(repo, ct))
                    {
                        if (shas.Contains(hf.Sha256))
                        {
                            ledger.PutSource(new Source(hf.Sha256, SourceKind.HuggingFace, hf.Locator, hf.Size, DateTime.UtcNow));
                        }
                    }
                }
                catch (Exception e) when (e is HttpRequestException or ArgumentException or System.Text.Json.JsonException or TaskCanceledException)
                {
                    ledger.Log("j.source_unreachable", repo, 0, false, e.Message);
                }
                finally
                {
                    gate.Release();
                }
            }));
        }

        if (settings.OllamaSource)
        {
            await RefreshOllamaAsync(files, shas, ct);
        }

        if (settings.MirrorRoot is { Length: > 0 } mirrorRoot && Directory.Exists(mirrorRoot))
        {
            var mirror = new MirrorStore(mirrorRoot);
            foreach (var f in files.Where(f => mirror.Has(f.Sha256, f.Size)))
            {
                ledger.PutSource(new Source(f.Sha256, SourceKind.Mirror, mirror.ObjectPath(f.Sha256), f.Size, DateTime.UtcNow));
            }
        }

        await FindRecipesAsync(files, progress, ct);

        foreach (var f in ledger.Files())
        {
            (f.Class, f.SourceLabel) = Classify(f);
            ledger.Upsert(f);
        }
    }

    /// <summary>Records registry sources for Ollama blobs. Blobs found only in a migrated model's child
    /// manifests may have been converted on this PC, so each is confirmed on the registry first.</summary>
    private async Task RefreshOllamaAsync(List<FileRecord> files, HashSet<string> shas, CancellationToken ct)
    {
        foreach (var f in files)
        {
            // Earlier versions took Ollama's hidden "llamacpp:<digest>" tag for a registry repository.
            foreach (var s in ledger.SourcesOf(f.Sha256).Where(s => s.Kind == SourceKind.Ollama
                         && s.Locator.StartsWith("registry.ollama.ai/library/llamacpp@", StringComparison.Ordinal)))
            {
                ledger.DropSource(s);
            }
        }
        var listed = OllamaRegistry.ReadManifests(OllamaRegistry.DefaultModelsDir).Where(b => shas.Contains(b.Sha256)).ToList();
        foreach (var blob in listed)
        {
            ledger.PutSource(new Source(blob.Sha256, SourceKind.Ollama, blob.Locator, blob.Size, DateTime.UtcNow));
        }
        if (ollama is null)
        {
            return;
        }
        var known = listed.Select(b => b.Sha256).ToHashSet(StringComparer.Ordinal);
        foreach (var (hex, child) in OllamaLayout.Read(OllamaRegistry.DefaultModelsDir).Children)
        {
            if (!shas.Contains(hex) || known.Contains(hex) || child.Parent.Host is not ("registry.ollama.ai" or "hf.co")
                || !OllamaBlob.RepoPath().IsMatch(child.Parent.Repository))
            {
                continue;
            }
            var blob = new OllamaBlob(child.Parent.Host, child.Parent.Repository, hex, child.Size);
            if (await ollama.VerifyAsync(blob, ct))
            {
                ledger.PutSource(new Source(hex, SourceKind.Ollama, blob.Locator, blob.Size, DateTime.UtcNow));
            }
        }
    }

    /// <summary>Repositories to check: the user's list, local evidence, the same authors' related repos
    /// and, only if enabled, a Hub search by local folder names. Capped to keep scans quick.</summary>
    private async Task<List<string>> CandidateReposAsync(List<FileRecord> files, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        var repos = new HashSet<string>(settings.HuggingFaceRepos.Where(RepoDiscovery.IsRepoId), StringComparer.OrdinalIgnoreCase);
        if (!settings.DiscoverHubRepos && !settings.HubNameSearch)
        {
            return [.. repos];
        }
        var localNames = RepoDiscovery.LocalModelNames(files);
        if (settings.DiscoverHubRepos)
        {
            var evidence = RepoDiscovery.LocalEvidence(settings.ManagedRoots, settings.OllamaSource ? OllamaRegistry.DefaultModelsDir : null);
            repos.UnionWith(evidence);
            foreach (var author in evidence.Select(r => r[..r.IndexOf('/')]).Distinct(StringComparer.OrdinalIgnoreCase).Take(20))
            {
                progress?.Report(new ScanProgress(Loc.T("phase.discover"), author, 0, 0));
                try
                {
                    var all = await hub.ListByAuthorAsync(author, 100, ct);
                    // Small authors: take everything; large ones: only repos that look like local models.
                    repos.UnionWith(all.Count <= 50 ? all : all.Where(r => RepoDiscovery.LooksRelated(r, localNames)));
                }
                catch (Exception e) when (e is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
                {
                    ledger.Log("j.source_unreachable", author, 0, false, e.Message);
                }
            }
        }
        if (settings.HubNameSearch)
        {
            var folders = files.Select(f => Path.GetFileName(Path.GetDirectoryName(f.Path)) ?? "")
                               .Where(n => n.Length >= 4).Distinct(StringComparer.OrdinalIgnoreCase).Take(40);
            foreach (var folder in folders)
            {
                try
                {
                    repos.UnionWith(await hub.SearchAsync(folder, 5, ct));
                }
                catch (Exception e) when (e is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
                {
                    ledger.Log("j.source_unreachable", folder, 0, false, e.Message);
                }
            }
        }
        return repos.Take(200).ToList();
    }

    /// <summary>Tries to express unsourced safetensors files with tensors of the other local files.</summary>
    private async Task FindRecipesAsync(List<FileRecord> files, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        var local = files.Where(f => f.Residency == Residency.Local && f.Path.EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase)).ToList();
        var parsed = local.Select(f => SafetensorsFile.TryRead(f.Path)).Where(x => x is not null).Cast<SafetensorsFile>().ToList();
        foreach (var f in local)
        {
            if (ledger.GetRecipe(f.Sha256) is { } existing && Recipe.FromJson(existing).SourcesUnchanged())
            {
                continue;
            }
            if (ledger.SourcesOf(f.Sha256).Any(s => s.Kind != SourceKind.Recipe) || ledger.WithSha(f.Sha256).Count > 1)
            {
                continue; // cheaper sources already exist
            }
            var target = parsed.FirstOrDefault(p => string.Equals(p.Path, f.Path, StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                continue;
            }
            // Several local models often share tensor names (sft vs turbo, fp32 vs bf16): try each
            // candidate model (folder) on its own, cheapest check first, full verification last.
            var names = target.Tensors.Select(t => t.Name).ToHashSet();
            var groups = parsed.Where(p => !string.Equals(p.Path, target.Path, StringComparison.OrdinalIgnoreCase))
                .GroupBy(p => Path.GetDirectoryName(p.Path) ?? "", StringComparer.OrdinalIgnoreCase)
                .Select(g => (Files: g.ToList(), Covered: g.SelectMany(x => x.Tensors).Select(t => t.Name).Where(names.Contains).Distinct().Count()))
                .Where(g => g.Covered == names.Count)
                .OrderByDescending(g => g.Files.Sum(x => x.Tensors.Count(t => t.Dtype == "F32")));
            foreach (var (candidates, _) in groups)
            {
                var recipe = RecipeBuilder.TryBuild(target, f.Sha256, candidates);
                if (recipe is null || !await RecipeBuilder.SpotCheckAsync(recipe, target.Path, ct))
                {
                    continue;
                }
                var p = new Progress<long>(b => progress?.Report(new ScanProgress(Loc.T("phase.recipe"), f.Path, b, f.Size)));
                if (await RecipeBuilder.VerifyAsync(recipe, p, ct))
                {
                    ledger.PutRecipe(f.Sha256, recipe.ToJson());
                    ledger.PutSource(new Source(f.Sha256, SourceKind.Recipe, f.Sha256, f.Size, DateTime.UtcNow));
                    ledger.Log("j.recipe_verified", f.Path, f.Size, true, recipe.Describe());
                    break;
                }
            }
        }
    }

    /// <summary>Best recoverability of a file given what is on disk right now.</summary>
    public (Recoverability, string) Classify(FileRecord f)
    {
        var copies = ledger.WithSha(f.Sha256).Where(o => !string.Equals(o.Path, f.Path, StringComparison.OrdinalIgnoreCase)
                                                        && o.Residency == Residency.Local).ToList();
        if (copies.Count > 0)
        {
            return (Recoverability.Duplicate, Loc.Code("src.copy", copies[0].Path));
        }
        var sources = ledger.SourcesOf(f.Sha256);
        if (sources.FirstOrDefault(s => s.Kind == SourceKind.Recipe) is not null
            && ledger.GetRecipe(f.Sha256) is { } json && Recipe.FromJson(json) is { } r && r.SourcesUnchanged())
        {
            return (Recoverability.Derivable, r.Describe());
        }
        if (sources.FirstOrDefault(s => s.Kind == SourceKind.Mirror) is { } m)
        {
            return (Recoverability.Reobtainable, Loc.Code("src.mirror", m.Locator));
        }
        var publicSources = sources.Where(s => s.Kind is SourceKind.HuggingFace or SourceKind.Ollama).ToList();
        if (publicSources.Count > 0 && publicSources.All(s => ledger.HealthState(s.Locator) == SourceHealth.Gone))
        {
            return (Recoverability.Unique, Loc.Code("src.public_gone"));
        }
        if (sources.FirstOrDefault(s => s.Kind == SourceKind.Ollama && ledger.HealthState(s.Locator) != SourceHealth.Gone) is { } o)
        {
            var blob = OllamaBlob.Parse(o.Locator, o.Size);
            return (Recoverability.Reobtainable, Loc.Code("src.ollama", $"{blob.Host}/{blob.Repository}"));
        }
        if (sources.FirstOrDefault(s => s.Kind == SourceKind.HuggingFace && ledger.HealthState(s.Locator) != SourceHealth.Gone) is { } h)
        {
            var hf = HubFile.Parse(h.Locator, h.Sha256, h.Size);
            return (Recoverability.Reobtainable, Loc.Code("src.hf", $"{hf.Repo}/{hf.Path}"));
        }
        return (Recoverability.Unique, Loc.Code("src.none"));
    }
}
