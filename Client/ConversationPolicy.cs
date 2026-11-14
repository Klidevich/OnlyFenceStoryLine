using System;

namespace CustomQuests.E17;

// Answers are remembered in the journal. Quest actions and status navigation stay repeatable.
internal static class ConversationPolicy
{
    internal static bool Allowed(string target, bool questAction, Func<string, bool> has)
    {
        if (questAction || target == null) return true;
        if (target == "e17_recap" || target.StartsWith("e17_review_", StringComparison.Ordinal)) return false;

        var stage = Q1FlowPolicy.StageFact(target);
        if (stage != null) return !Q1FlowPolicy.Seen(target.Substring("e17_".Length), has);
        if (NarrativePolicy.Facts.TryGetValue(target, out var fact)) return !has(fact);
        if (Q2Policy.Facts.TryGetValue(target, out fact)) return !has(fact);
        return true;
    }
}
