namespace CustomQuests.E17;

internal static class IntroPolicy
{
    internal const string JournalKey = "3f65b8e8d0fc380166038eed";
    internal const string NotificationKey = "te9e9f19e3941817f";
    internal static bool ShouldNotify(bool resolved, bool eligible, bool used, bool menuVisible, int chapterStatus) =>
        resolved && eligible && !used && menuVisible && chapterStatus is 2 or 3 or 4;
}
