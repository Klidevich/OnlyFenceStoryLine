#nullable enable
using System;

namespace CustomQuests.E17;

// Conversation progress is independent of quest acceptance and of legacy journal facts.
internal static class Q1FlowPolicy
{
    internal static readonly string[] Stages = { "rescue", "closed", "company", "van" };

    internal static string? StageFact(string node)
    {
        foreach (var stage in Stages)
            if (node == "e17_" + stage) return Fact(stage);
        return null;
    }

    static string Fact(string stage) => stage is "closed" or "company" ? "intro4_" + stage : "intro3_" + stage;

    // A legacy split answer satisfies a merged block only when every earlier piece was shown.
    internal static bool Seen(string stage, Func<string, bool> has) => has(Fact(stage)) || stage switch
    {
        "rescue" => has("intro2_rescue") && has("intro2_bill"),
        "closed" => has("intro2_closed") && has("intro2_act"),
        "company" => has("intro2_company") && (has("intro3_delivery") || has("intro2_delivery") && has("intro2_briefed")),
        "van" => has("intro2_van") && has("intro2_briefed"),
        _ => false
    };

    internal static string BriefingEntry(Func<string, bool> has)
    {
        for (var i = 0; i < Stages.Length; i++)
            if (!Seen(Stages[i], has)) return i == 0 ? "e17_intro" : "e17_resume_" + Stages[i - 1];
        return "e17_offer";
    }

    internal static bool CanAccept(Func<string, bool> has)
    {
        foreach (var stage in Stages) if (!Seen(stage, has)) return false;
        return true;
    }

    internal static bool BooksKnown(Func<string, bool> has) => has("intro4_closed") || has("booksKnown") || has("books");
    internal static bool HasReview(Func<string, bool> has)
    {
        foreach (var stage in Stages) if (Seen(stage, has)) return true;
        return false;
    }

    internal static string ActiveEntry(bool ticketGiven, bool radioGiven) =>
        ticketGiven ? (radioGiven ? "e17_active_both" : "e17_active_ticket") :
        radioGiven ? "e17_active_radio" : "e17_active_none";
}
