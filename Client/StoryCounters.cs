using System;
using EFT;
using EFT.Quests;

namespace CustomQuests.E17;

// These facts have no native quest condition template. GetTaskConditionCounter
// always resolves that template, even when a saved counter already exists.
internal static class StoryCounters
{
    internal static int Value(Profile profile, string questId, MongoID id) =>
        Existing(profile, questId, id)?.Value ?? 0;

    internal static bool Mark(Profile profile, string questId, MongoID id, string type)
    {
        var counter = Existing(profile, questId, id);
        if (counter == null)
        {
            profile.TaskConditionCounters.Add(id, new TaskConditionCounter(new TaskConditionCounterDescriptor
            {
                Id = id, SourceId = questId, Type = type, Value = 1
            }));
            return true;
        }
        if (counter.Value >= 1) return false;
        counter.Value = 1;
        return true;
    }

    static TaskConditionCounter Existing(Profile profile, string questId, MongoID id)
    {
        if (profile == null) throw new ArgumentNullException(nameof(profile));
        if (string.IsNullOrEmpty(questId)) throw new ArgumentException("A story counter requires its quest owner.", nameof(questId));
        if (!profile.TaskConditionCounters.TryGetValue(id, out var counter)) return null;
        // Do not accept or overwrite a proof owned by a different quest.
        if (counter == null || counter.Id != id || !string.Equals(counter.SourceId, questId, StringComparison.Ordinal))
            throw new InvalidOperationException("E17 story counter does not belong to the expected quest: " + id);
        return counter;
    }
}
