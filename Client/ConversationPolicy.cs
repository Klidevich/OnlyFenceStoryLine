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
            if (topic == "ours") return has("introOursV018");
            return topic is "archive" or "books"
                ? NarrativePolicy.Facts.TryGetValue("e17_" + topic, out var seenFact) && has(seenFact)
                : Q1FlowPolicy.Seen(topic, has);
        }
        if (target is "e17_act" or "e17_delivery" or "e17_resume_act" or "e17_resume_delivery" or "e17_q2_friend") return false;
        if (target == "e17_ours") return !has("introOursV018");
        if (target == "e17_q1_separate") return !has("q1Separate");
        if (target == "e17_q1_person") return !has("q1Person");

        var stage = Q1FlowPolicy.StageFact(target);
        if (stage != null) return !Q1FlowPolicy.Seen(target.Substring("e17_".Length), has);
        // A route reminder is a deliberate action, not a new journal fact.
        if (target is "e17_route" or "e17_q2_remind" or "e17_q2_remind_reported") return true;
        if (NarrativePolicy.Facts.TryGetValue(target, out var fact)) return !has(fact);
        if (Q2Policy.Facts.TryGetValue(target, out fact)) return !has(fact);
        return true;
    }
}
