using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Marea.Core;

/// <summary>Localization: one JSON file per language embedded in the assembly (i18n/xx.json),
/// English as fallback. Persistent texts (journal, source labels) are stored as codes
/// "key|arg|arg" and rendered at display time, so switching language re-renders history too.</summary>
public static class Loc
{
    private const char Sep = '|';
    private static readonly Dictionary<string, Dictionary<string, string>> Cache = [];
    private static Dictionary<string, string> _current = Load("en");
    private static readonly Dictionary<string, string> English = Load("en");

    /// <summary>Supported languages (code, native name).</summary>
    public static readonly IReadOnlyList<(string Code, string Name)> Languages =
        [("it", "Italiano"), ("en", "English"), ("es", "Español"), ("fr", "Français"), ("de", "Deutsch")];

    /// <summary>Active language code.</summary>
    public static string Language { get; private set; } = "en";

    /// <summary>Activates a language; "auto" (or unknown) picks the first supported language among the
    /// user's preferred Windows languages, else English.</summary>
    public static void SetLanguage(string? code)
    {
        if (string.IsNullOrEmpty(code) || code == "auto" || Languages.All(l => l.Code != code))
        {
            code = Automatic(PreferredLanguages());
        }
        Language = code;
        _current = Load(code);
    }

    /// <summary>The first supported language in <paramref name="preferred"/> (BCP-47 tags or two-letter
    /// codes, in the user's order), or English.</summary>
    public static string Automatic(IEnumerable<string> preferred)
    {
        foreach (var tag in preferred)
        {
            var two = tag.Split('-', '_')[0].ToLowerInvariant();
            if (Languages.Any(l => l.Code == two))
            {
                return two;
            }
        }
        return "en";
    }

    /// <summary>The system's ordered list of preferred languages, then the display language.</summary>
    private static IEnumerable<string> PreferredLanguages()
    {
        IReadOnlyList<string> list = [];
#if WINDOWS
        try
        {
            list = Windows.System.UserProfile.GlobalizationPreferences.Languages;
        }
        catch (Exception e) when (e is TypeLoadException or System.Runtime.InteropServices.COMException or PlatformNotSupportedException or UnauthorizedAccessException)
        {
            // Not available (e.g. very old Windows): the display language below is enough.
        }
#else
        // Linux: LANGUAGE is an ordered list ("it:en"), then the locale variables in POSIX precedence.
        list = [.. (Environment.GetEnvironmentVariable("LANGUAGE") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries),
            .. new[] { "LC_ALL", "LC_MESSAGES", "LANG" }.Select(Environment.GetEnvironmentVariable).OfType<string>()];
#endif
        return list.Append(CultureInfo.CurrentUICulture.Name);
    }

    /// <summary>The language "automatic" resolves to on this machine.</summary>
    public static string AutomaticLanguage() => Automatic(PreferredLanguages());

    /// <summary>Native name of a supported language code.</summary>
    public static string NativeName(string code) => Languages.FirstOrDefault(l => l.Code == code).Name ?? code;

    /// <summary>Translates <paramref name="key"/> and formats it with <paramref name="args"/>.</summary>
    public static string T(string key, params object?[] args)
    {
        var text = _current.GetValueOrDefault(key) ?? English.GetValueOrDefault(key) ?? key;
        return args.Length == 0 ? text : string.Format(CultureInfo.CurrentCulture, text, args);
    }

    /// <summary>Encodes a translatable message for storage: <c>key|arg|arg</c>.</summary>
    public static string Code(string key, params object?[] args) =>
        args.Length == 0 ? key : key + Sep + string.Join(Sep, args.Select(a => Convert.ToString(a, CultureInfo.InvariantCulture)?.Replace(Sep, '¦')));

    /// <summary>Renders a stored code; plain text (e.g. an exception message) is returned unchanged.</summary>
    public static string Render(string? coded)
    {
        if (string.IsNullOrEmpty(coded))
        {
            return "";
        }
        var parts = coded.Split(Sep);
        if (!English.ContainsKey(parts[0]))
        {
            return coded;
        }
        return T(parts[0], parts.Skip(1).Cast<object?>().ToArray());
    }

    /// <summary>All keys of a language file (used by tests to keep translations complete).</summary>
    public static IReadOnlyCollection<string> Keys(string code) => Load(code).Keys;

    private static Dictionary<string, string> Load(string code)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(code, out var d))
            {
                return d;
            }
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Marea.Core.i18n.{code}.json");
            d = s is null ? [] : JsonSerializer.Deserialize<Dictionary<string, string>>(s) ?? [];
            Cache[code] = d;
            return d;
        }
    }
}
