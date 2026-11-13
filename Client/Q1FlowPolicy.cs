#nullable enable
using System;

namespace CustomQuests.E17;

// Conversation progress is independent of quest acceptance and of legacy journal facts.
internal static class Q1FlowPolicy
{
    internal static readonly string[] Stages =
    {
        "rescue", "bill", "closed", "act", "original", "proof", "motive",
        "company", "delivery", "van", "briefed"
    };

    internal static string? StageFact(string node)
    {
        foreach (var stage in Stages)
            if (node == "e17_" + stage) return "intro2_" + stage;
        return null;
    }

    internal static string BriefingEntry(Func<string, bool> has)
    {
        for (var i = 0; i < Stages.Length; i++)
            if (!has("intro2_" + Stages[i])) return i == 0 ? "e17_intro" : "e17_resume_" + Stages[i - 1];
        return "e17_offer";
    }

    internal static bool CanAccept(Func<string, bool> has)
    {
        foreach (var stage in Stages) if (!has("intro2_" + stage)) return false;
        return true;
    }

    internal static string ActiveEntry(bool ticketGiven, bool radioGiven) =>
        ticketGiven ? (radioGiven ? "e17_active_both" : "e17_active_ticket") :
        radioGiven ? "e17_active_radio" : "e17_active_none";
}
