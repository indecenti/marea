using System.Text.RegularExpressions;
using Marea.Core;

namespace Marea.Tests;

/// <summary>Every language must have exactly the same keys and the same {n} placeholders as English.</summary>
public sealed partial class LocalizationTests
{
    [GeneratedRegex(@"\{(\d+)\}")]
    private static partial Regex Placeholder();

    public static TheoryData<string> Languages() => new(Loc.Languages.Select(l => l.Code));

    [Theory]
    [MemberData(nameof(Languages))]
    public void LanguageHasSameKeysAsEnglish(string code)
    {
        Assert.Equal(Loc.Keys("en").OrderBy(k => k), Loc.Keys(code).OrderBy(k => k));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void PlaceholdersMatchEnglish(string code)
    {
        Loc.SetLanguage("en");
        var en = Loc.Keys("en").ToDictionary(k => k, k => Loc.T(k));
        Loc.SetLanguage(code);
        foreach (var key in en.Keys)
        {
            var want = Placeholder().Matches(en[key]).Select(m => m.Value).Distinct().OrderBy(x => x);
            var got = Placeholder().Matches(Loc.T(key)).Select(m => m.Value).Distinct().OrderBy(x => x);
            Assert.True(want.SequenceEqual(got), $"{code}:{key}");
        }
        Loc.SetLanguage("en");
    }

    [Fact]
    public void CodesRenderInTheActiveLanguageAndPlainTextPassesThrough()
    {
        var coded = Loc.Code("src.copy", @"C:\a|b.bin");
        Loc.SetLanguage("it");
        Assert.StartsWith("copia identica", Loc.Render(coded));
        Loc.SetLanguage("de");
        Assert.StartsWith("identische Kopie", Loc.Render(coded));
        Loc.SetLanguage("pt");
        Assert.Equal("pt", Loc.Language);
        Assert.StartsWith("cópia idêntica", Loc.Render(coded));
        Loc.SetLanguage("zh");
        Assert.Equal("zh", Loc.Language);
        Assert.Contains("副本", Loc.Render(coded));
        Assert.Equal("Access denied", Loc.Render("Access denied"));
        Loc.SetLanguage("en");
    }

    [Fact]
    public void UnknownLanguageFallsBackSafely()
    {
        Loc.SetLanguage("xx");
        Assert.Contains(Loc.Language, Loc.Languages.Select(l => l.Code));
        Loc.SetLanguage("en");
    }

    [Theory]
    [InlineData(new[] { "it-IT", "en-US" }, "it")]
    [InlineData(new[] { "ja-JP", "de-CH", "en-US" }, "de")]
    [InlineData(new[] { "pt-BR", "zh-Hans-CN" }, "pt")]
    [InlineData(new[] { "pt-PT", "en-US" }, "pt")]
    [InlineData(new[] { "zh-Hans-CN", "en-US" }, "zh")]
    [InlineData(new[] { "ja-JP", "ko-KR" }, "en")]
    [InlineData(new string[0], "en")]
    [InlineData(new[] { "fr" }, "fr")]
    public void AutomaticLanguageTakesTheFirstSupportedPreferredOrEnglish(string[] preferred, string expected) =>
        Assert.Equal(expected, Loc.Automatic(preferred));
}
