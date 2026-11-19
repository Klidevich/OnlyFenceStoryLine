using System.Text.Json;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Utils.Cloners;

namespace CustomQuests.E17.Server;

public sealed record NarrativeMigrationResult(bool Changed, string? Warning = null);
public sealed class NarrativeMigrationRecord
{
    public int Version { get; set; } = 1;
    // Historical discovery evidence, not new dialogue/read/choice facts.
    public HashSet<string> RetiredDiscoveries { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>One-time v0.27 compatibility on native profile state. Caller holds ParcelRequestGate.</summary>
public static class NarrativeMigration
{
    public const string RecordKey = "customquestsE17NarrativeV027";
    public const string RadioTemplate = "e208363938a46fbc1507af21";
    const string Q4LedgerKey = "customquestsE17Q4V022";
    public static readonly (string Quest, string Old, string New)[] ItemMappings =
    [
        ("q5", "transportLog", "armorBundle"),
        ("q6", "toolReturnInvoice", "proceedsPackage"),
        ("q6", "l6Envelope", "expeditorReply")
    ];
    static readonly string[] RetiredConditions =
    [
        "q1/find/radio", "q1/give/radio",
        "q4/businessReply/find", "q4/businessReply/give",
        "q7/find/transportReturnLog", "q7/give/transportReturnLog", "q7/find/transportRefusal"
    ];
    static readonly HashSet<string> RetiredTemplates = new(StringComparer.Ordinal)
    {
        RadioTemplate, Q4Policy.BusinessReply,
        LateIds.Id("q7/item/transportReturnLog"), LateIds.Id("q7/item/transportRefusal")
    };
    public static async Task<NarrativeMigrationResult> Migrate(SptProfile? profile, ICloner cloner, Func<Task> save)
    {
        if (profile?.ProfileInfo == null || profile.ProfileInfo.InvalidOrUnloadableProfile == true || profile.CharacterData?.PmcData == null)
            return new(false, "profile is missing or invalid; narrative migration was not applied");
        if (profile.ExtensionData?.TryGetValue(RecordKey, out var raw) == true)
        {
            try
            {
                var record = raw as NarrativeMigrationRecord ?? JsonSerializer.Deserialize<NarrativeMigrationRecord>(JsonSerializer.Serialize(raw));
                return record?.Version >= 1 ? new(false) : new(false, "unrecognized narrative migration marker; original state retained");
            }
            catch (JsonException) { return new(false, "invalid narrative migration marker; original state retained"); }
        }
        // Plan on a full clone. In particular, Q4Policy.Read may return a typed mutable ledger.
        var planned = cloner.Clone(profile) ?? throw new InvalidOperationException("Unable to clone narrative migration profile.");
        var warning = Validate(planned);
        if (warning != null) return new(false, warning);
        if (!Relevant(planned)) return new(false);
        var recordNew = new NarrativeMigrationRecord();
        Apply(planned, recordNew);
        planned.ExtensionData ??= new();
        planned.ExtensionData[RecordKey] = JsonSerializer.SerializeToElement(recordNew);
        var characters = profile.CharacterData;
        var dialogue = profile.DialogueRecords;
        var extension = profile.ExtensionData;
        // SPT 4.1.6 advances its cached hash before I/O. Persist the original first:
        // after a failed final write, the retry must cycle baseline -> migrated hash.
        await save();
        try
        {
            profile.CharacterData = planned.CharacterData;
            profile.DialogueRecords = planned.DialogueRecords;
            profile.ExtensionData = planned.ExtensionData;
            await save();
        }
        catch
        {
            profile.CharacterData = characters;
            profile.DialogueRecords = dialogue;
            profile.ExtensionData = extension;
            throw;
        }
        return new(true);
    }
    static IEnumerable<List<Item>> ItemCollections(SptProfile p)
    {
        if (p.CharacterData?.PmcData?.Inventory?.Items is { } pmc) yield return pmc;
        if (p.CharacterData?.ScavData?.Inventory?.Items is { } scav) yield return scav;
        if (p.DialogueRecords != null)
            foreach (var dialogue in p.DialogueRecords.Values)
            foreach (var mail in dialogue.Messages ?? [])
                if (mail.Items?.Data is { } items) yield return items;
    }
    static bool Relevant(SptProfile p) => p.CharacterData!.PmcData!.Quests?.Any(q =>
        new[] { "q1", "q4", "q5", "q6", "q7" }.Any(k => q.QId.ToString() == LateIds.Id(k))) == true ||
        ItemCollections(p).Any(items => items.Any(i => RetiredTemplates.Contains(i.Template.ToString()) ||
            ItemMappings.Any(m => i.Template.ToString() == LateIds.Id($"{m.Quest}/item/{m.Old}"))));
    static string? Validate(SptProfile p)
    {
        foreach (var items in ItemCollections(p))
            if (items.Select(i => i.Id).Distinct().Count() != items.Count)
                return "duplicate inventory/mail item IDs; narrative migration retained the original state";
        var pmc = p.CharacterData!.PmcData!;
        if (pmc.Quests?.GroupBy(q => q.QId).Any(g => g.Count() > 1) == true)
            return "duplicate quest states; narrative migration retained the original state";
        foreach (var (quest, old, target) in ItemMappings)
        foreach (var stage in new[] { "find", "give" })
        foreach (var name in new[] { old, target })
        {
            var key = LateIds.Id($"{quest}/{stage}/{name}");
            if (pmc.TaskConditionCounters?.TryGetValue(key, out var counter) == true && counter.SourceId?.ToString() != LateIds.Id(quest))
                return "quest counter source mismatch; narrative migration retained the original state";
        }
        return null;
    }
    static void Apply(SptProfile p, NarrativeMigrationRecord record)
    {
        var pmc = p.CharacterData!.PmcData!;
        // Capture the old evidence before dropping obsolete conditions/items.
        foreach (var key in RetiredConditions)
            if (Done(pmc, key)) record.RetiredDiscoveries.Add(key);
        foreach (var items in ItemCollections(p))
            foreach (var item in items.Where(i => RetiredTemplates.Contains(i.Template.ToString())))
                record.RetiredDiscoveries.Add("item:" + item.Template);

        if (pmc.Quests?.Any(q => q.QId.ToString() == Q4Policy.QuestId) == true)
        {
            var state = Q4Policy.Read(p);
            // A historical successful hand-in may prove extraction; finding/owning
            // the personal copy alone never proves that its text was read.
            var explicitRead = pmc.Variables?.GetValueOrDefault(ProgressPolicy.Id("q4CopyRead")) == 1;
            if (p.ExtensionData?.TryGetValue(Q4LedgerKey, out var old) == true)
            {
                var json = JsonSerializer.SerializeToElement(old);
                explicitRead |= json.TryGetProperty(nameof(Q4Ledger.CopyRead), out var read) && read.ValueKind == JsonValueKind.True;
            }
            state.CopyRead = Q4Policy.Success(p) || explicitRead;
            if (Q4Policy.Success(p)) { state.Delivery = true; state.PersonalExtracted = true; }
            Q4Policy.Write(p, state);
        }
        foreach (var (quest, old, target) in ItemMappings)
        {
            MapCondition(pmc, $"{quest}/find/{old}", $"{quest}/find/{target}");
            if (old != "l6Envelope") MapCondition(pmc, $"{quest}/give/{old}", $"{quest}/give/{target}");
            foreach (var items in ItemCollections(p))
                foreach (var item in items.Where(i => i.Template.ToString() == LateIds.Id($"{quest}/item/{old}")))
                    item.Template = LateIds.Id($"{quest}/item/{target}");
        }
        // Personal letter discovery survives loss. Possession in the PMC inventory
        // is native evidence of discovery even when an old FindItem counter was absent.
        foreach (var (quest, _, target) in ItemMappings)
            if (pmc.Inventory?.Items?.Any(i => i.Template.ToString() == LateIds.Id($"{quest}/item/{target}")) == true)
                EnsureDiscovery(pmc, $"{quest}/find/{target}");
        foreach (var condition in RetiredConditions) RemoveCondition(pmc, condition);
        foreach (var items in ItemCollections(p)) RemoveRetiredTrees(items);

        // These old presentation flags described different offers. Only available
        // (not accepted/completed) quests must hear the new offer before accepting.
        ResetAvailableBriefing(pmc, "q5", ["late_q5_offer", "late_q5_number", "late_q5_ledger"]);
        ResetAvailableBriefing(pmc, "q6", ["late_q6_offer", "late_q6_intro", "late_q6_marking"]);
        ResetAvailableBriefing(pmc, "q7", ["late_q7_terms", "late_q7_pledge", "late_q7_request"]);
        // Do not infer strelokNote, Vadim's spoken answers, new reading, Choice7 or
        // Keeper from any of the replaced letters. The late ledger is untouched.
    }
    static bool Done(PmcData pmc, string key)
    {
        var quest = LateIds.Id(key.Split('/')[0]); var id = LateIds.Id(key);
        return pmc.Quests?.Any(q => q.QId.ToString() == quest && q.CompletedConditions?.Contains(id) == true) == true ||
            pmc.TaskConditionCounters?.TryGetValue(id, out var counter) == true && counter.SourceId?.ToString() == quest && counter.Value >= 1;
    }
    static void MapCondition(PmcData pmc, string oldKey, string newKey)
    {
        var quest = LateIds.Id(oldKey.Split('/')[0]); var old = LateIds.Id(oldKey); var target = LateIds.Id(newKey);
        var state = pmc.Quests?.FirstOrDefault(q => q.QId.ToString() == quest);
        if (state?.CompletedConditions?.RemoveAll(x => x == old) > 0 && !state.CompletedConditions.Contains(target)) state.CompletedConditions.Add(target);
        if (pmc.TaskConditionCounters?.Remove(old, out var previous) == true)
        {
            var carried = previous with { Id = target, SourceId = quest };
            if (pmc.TaskConditionCounters.TryGetValue(target, out var existing))
            {
                // Idempotent merge, never a second hand-in and never a summed count.
                if ((previous.Value ?? 0) > (existing.Value ?? 0)) existing.Value = previous.Value;
            }
            else pmc.TaskConditionCounters[target] = carried;
        }
    }
    static void EnsureDiscovery(PmcData pmc, string key)
    {
        if (Done(pmc, key)) return;
        var quest = LateIds.Id(key.Split('/')[0]);
        if (pmc.Quests?.Any(q => q.QId.ToString() == quest) != true) return;
        pmc.TaskConditionCounters ??= new(); var id = LateIds.Id(key);
        pmc.TaskConditionCounters[id] = new TaskConditionCounter { Id = id, SourceId = quest, Type = "FindItem", Value = 1 };
    }
    static void RemoveCondition(PmcData pmc, string key)
    {
        var quest = LateIds.Id(key.Split('/')[0]); var id = LateIds.Id(key);
        pmc.Quests?.FirstOrDefault(q => q.QId.ToString() == quest)?.CompletedConditions?.RemoveAll(x => x == id);
        if (pmc.TaskConditionCounters?.TryGetValue(id, out var c) == true && c.SourceId?.ToString() == quest)
            pmc.TaskConditionCounters.Remove(id);
    }
    static void RemoveRetiredTrees(List<Item> items)
    {
        var remove = items.Where(i => RetiredTemplates.Contains(i.Template.ToString())).Select(i => i.Id.ToString()).ToHashSet(StringComparer.Ordinal);
        bool changed;
        do
        {
            changed = false;
            foreach (var item in items)
                if (item.ParentId != null && remove.Contains(item.ParentId)) changed |= remove.Add(item.Id.ToString());
        } while (changed);
        items.RemoveAll(i => remove.Contains(i.Id.ToString()));
    }
    static void ResetAvailableBriefing(PmcData pmc, string key, string[] facts)
    {
        if (pmc.Quests?.Any(q => q.QId.ToString() == LateIds.Id(key) && q.Status == QuestStatusEnum.AvailableForStart) != true) return;
        foreach (var fact in facts) pmc.Variables?.Remove(ProgressPolicy.Id(fact));
    }
}
