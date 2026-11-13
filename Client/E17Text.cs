using System;

namespace CustomQuests.E17;

internal static class E17Text
{
    internal const string Prefix = "customquests.e17.ui.";
    // The game supplies its current locale lookup; the pure scenario checker supplies a test locale.
    internal static Func<string, string> Lookup = source => source;
    internal static string Get(string source) => Lookup(source);
}
