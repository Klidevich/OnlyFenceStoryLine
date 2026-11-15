using System;

namespace CustomQuests.E17;

// New answers are recorded once; explicitly selected rereading never advances story progress.
internal static class ConversationPolicy
{
    internal static bool Allowed(string target, bool questAction, Func<string, bool> has)
    {
        if (questAction || target == null) return true;
        if (target == "e17_recap") return Q1FlowPolicy.HasReview(has);
        if (target.StartsWith("e17_review_", StringComparison.Ordinal))
        {
            var topic = target.Substring("e17_review_".Length);
            return topic is "ours" or "archive" or "books"
                ? NarrativePolicy.Facts.TryGetValue("e17_" + topic, out var seenFact) && has(seenFact)
                : Q1FlowPolicy.Seen(topic, has);
        }
        if (target is "e17_act" or "e17_delivery" or "e17_resume_act" or "e17_resume_delivery" or
            "e17_q2_cargo" or "e17_q2_result_resume" or "e17_q2_instructions") return false;

        var stage = Q1FlowPolicy.StageFact(target);
        if (stage != null) return !Q1FlowPolicy.Seen(target.Substring("e17_".Length), has);
        // A route reminder is a deliberate action, not a new journal fact.
        if (target is "e17_route" or "e17_q2_remind" or "e17_q2_remind_reported") return true;
        if (NarrativePolicy.Facts.TryGetValue(target, out var fact)) return !has(fact);
        if (Q2Policy.Facts.TryGetValue(target, out fact)) return !has(fact);
        return true;
    }
}
