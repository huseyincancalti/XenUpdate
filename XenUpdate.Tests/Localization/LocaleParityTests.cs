using System.Text.Json;
using Xunit;
using XenUpdate.Core.Interfaces;
using XenUpdate.Core.Models;
using XenUpdate.Infrastructure.Guides;

namespace XenUpdate.Tests.Localization;

/// <summary>
/// Guards the two localization surfaces against drifting apart: the app string catalogs
/// (en.json / tr.json must expose the exact same key set) and the guide catalogs (same guides,
/// same structure, only the words differ). A missing key surfaces at runtime as a raw key name
/// (or English fallback) in the UI — easy to miss in review, cheap to catch here.
/// </summary>
public sealed class LocaleParityTests
{
    [Fact]
    public void AppLocales_EnAndTr_HaveIdenticalKeySets()
    {
        var localesDir = FindRepoPath(Path.Combine("XenUpdate.App", "Assets", "Locales"));
        var enKeys = ReadKeys(Path.Combine(localesDir, "en.json"));
        var trKeys = ReadKeys(Path.Combine(localesDir, "tr.json"));

        var missingInTr = enKeys.Except(trKeys).OrderBy(k => k).ToList();
        var missingInEn = trKeys.Except(enKeys).OrderBy(k => k).ToList();

        Assert.True(missingInTr.Count == 0 && missingInEn.Count == 0,
            $"Locale key drift. Missing in tr.json: [{string.Join(", ", missingInTr)}]. " +
            $"Missing in en.json: [{string.Join(", ", missingInEn)}].");
    }

    [Fact]
    public async Task GuideCatalogs_EnAndTr_HaveSameStructure()
    {
        IGuideCatalog catalog = new EmbeddedGuideCatalog(new NullLogger());
        var en = await catalog.GetGuidesAsync("en");
        var tr = await catalog.GetGuidesAsync("tr");

        Assert.Equal(en.Select(g => g.Id), tr.Select(g => g.Id));

        foreach (var (enGuide, trGuide) in en.Zip(tr))
        {
            Assert.Equal(enGuide.Steps.Count, trGuide.Steps.Count);
            Assert.Equal(enGuide.AppSteps.Count, trGuide.AppSteps.Count);
            Assert.Equal(enGuide.Category, trGuide.Category);
            Assert.Equal(enGuide.IsTroubleshooting, trGuide.IsTroubleshooting);
            Assert.Equal(enGuide.RequiredGpuVendor, trGuide.RequiredGpuVendor);
            Assert.Equal(enGuide.VersionCheckKind, trGuide.VersionCheckKind);
            Assert.Equal(enGuide.OfficialUrl, trGuide.OfficialUrl);
        }
    }

    private static HashSet<string> ReadKeys(string jsonPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
        return doc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Walks up from the test assembly's output directory until the given repo-relative path
    /// exists — the locale JSONs are Content files of the App project, which this test project
    /// doesn't reference, so the source tree is the only sane place to read them from.
    /// </summary>
    private static string FindRepoPath(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException($"Could not locate '{relative}' above {AppContext.BaseDirectory}.");
    }

    private sealed class NullLogger : ILoggerService
    {
        public event Action<LogEntry>? LogEntryAdded { add { } remove { } }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? ex = null) { }
    }
}
