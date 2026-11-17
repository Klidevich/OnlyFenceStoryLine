namespace CustomQuests.E17;

// The chapter opens after the menu delay; accepting work remains a separate player action.
internal static class VisitEntryPolicy
{
    internal const string FenceId = "579dc571d53a0658a154fbec";
    internal const string FirstQuestId = "c50c495eb5a978728eaf9d59";
    internal static bool NeedsBriefing(int firstQuestStatus) => firstQuestStatus is 0 or 1;
    internal static bool OffersNewQuest(int questStatus) => questStatus == 1;
    internal static bool NeedsVisit(int questStatus, bool hasReadyHandover, bool hasReadyReport = false) =>
        OffersNewQuest(questStatus) || questStatus == 3 || questStatus == 2 && (hasReadyHandover || hasReadyReport);
}
