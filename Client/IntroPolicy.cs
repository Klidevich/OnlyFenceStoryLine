using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace CustomQuests.E17;

internal static class IntroPolicy
{
    internal const string JournalKey = "3f65b8e8d0fc380166038eed";
    internal const float FadeSeconds = 0.8f;
    internal static string[] Pages(string localizedJournal) => (localizedJournal ?? "")
        .Replace("\r\n", "\n").Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries)
        .Where(p => !p.TrimStart().StartsWith("<b>", StringComparison.Ordinal)).Select(p => p.Trim()).Where(p => p.Length > 0).ToArray();
    // Recomputed from the selected language, including longer English translations.
    internal static float ReadingSeconds(string page) => Math.Max(8f, Regex.Matches(page ?? "", @"\S+").Count * 60f / 150f + 2f);
}
