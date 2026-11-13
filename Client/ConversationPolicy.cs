using System;

namespace CustomQuests.E17;

// Answers are remembered in the journal. Quest actions and status navigation stay repeatable.
internal static class ConversationPolicy
{
    internal static bool Allowed(string target, bool questAction, Func<string, bool> has)
    {
        if (questAction || target == null) return true;
        if (target == "e17_recap" || target.StartsWith("e17_review_", StringComparison.Ordinal) ||
            target is "e17_route" or "e17_q2_remind" or "e17_q2_remind_reported") return false;

        var stage = Q1FlowPolicy.StageFact(target);
        if (stage != null) return !has(stage);
        if (target == "e17_workshop" && has("radioSecond")) return false;
        if (NarrativePolicy.Facts.TryGetValue(target, out var fact)) return !has(fact);
        if (Q2Policy.Facts.TryGetValue(target, out fact)) return !has(fact);
        return true;
    }
}
