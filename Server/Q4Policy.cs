using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;
using System.Text.Json;

namespace CustomQuests.E17.Server;

public static class Q4Policy
{
    public const string QuestId = "8e3f7ba9a8877778bd0f9035";
    public const string TemplateId = "258aa01fe0c068cfd26744d1";
    public const string ClaimGiveId = "2f7372f31d44cb8145b1bb94";
    public const string PlacementId = "59f3ea45693024d45ed3f928";
    public const string ReplyReadyFact = "q4ReplyReady";
    public const string MailText = "e17 request issue";
    const string DurableReplyKey = "customquestsE17Q4ReplyReady";
    public enum Decision { MissingProfile, NotActive, AwaitingClaim, Placed, PendingMail, Owned, Issue }

    public static bool ConditionDone(SptProfile? profile, string condition) =>
        profile?.CharacterData?.PmcData is { } pmc &&
        (pmc.Quests?.Any(q => q.QId.ToString() == QuestId && q.CompletedConditions?.Contains(condition) == true) == true ||
         pmc.TaskConditionCounters?.Any(p => p.Key.ToString() == condition && p.Value.SourceId?.ToString() == QuestId && p.Value.Value >= 1) == true);

    public static Decision Evaluate(SptProfile? profile, long now)
    {
        var pmc = profile?.CharacterData?.PmcData;
        if (pmc?.Inventory?.Items == null) return Decision.MissingProfile;
        if (pmc.Quests?.FirstOrDefault(q => q.QId.ToString() == QuestId)?.Status != QuestStatusEnum.Started) return Decision.NotActive;
        if (ConditionDone(profile, PlacementId)) return Decision.Placed;
        if (!ConditionDone(profile, ClaimGiveId)) return Decision.AwaitingClaim;
        // Mail before inventory: native attachment collection inserts the item first.
        if (profile!.DialogueRecords?.Values.Any(d => d.Messages?.Any(m => m.RewardCollected != true &&
            m.Items?.Data?.Any(i => i.Template.ToString() == TemplateId) == true &&
            (m.DateTime == null || m.MaxStorageTime == null || m.MaxStorageTime <= 0 || now - m.DateTime < m.MaxStorageTime)) == true) == true)
            return Decision.PendingMail;
        if (pmc.Inventory.Items.Any(i => i.Template.ToString() == TemplateId) ||
            profile.CharacterData?.ScavData?.Inventory?.Items?.Any(i => i.Template.ToString() == TemplateId) == true) return Decision.Owned;
        return Decision.Issue;
    }

    // Called only AFTER native end-local has merged and saved the submitted PMC raid.
    // Death is deliberately irrelevant; a counter committed by that raid proves the plant.
    public static bool RecordEndedRaid(SptProfile? profile, string? serverId, string? submittedProfileId)
    {
        var pmc = profile?.CharacterData?.PmcData;
        var parts = serverId?.Split('.');
        if (pmc == null || pmc.Id.ToString() != submittedProfileId) return false;
        if (!HasReplyProof(profile))
        {
            if (parts is not { Length: >= 2 } || !parts[0].Equals("woods", StringComparison.OrdinalIgnoreCase) ||
                !parts[1].Contains("pmc", StringComparison.OrdinalIgnoreCase) || !ConditionDone(profile, PlacementId)) return false;
            // Native end-local replaces PMC Variables from the submitted raid snapshot.
            // Keep authoritative proof outside that replaceable snapshot, then mirror it.
            profile!.ExtensionData![DurableReplyKey] = true;
        }
        RestoreReplyProof(profile);
        return true;
    }
    public static bool HasReplyProof(SptProfile? profile) => profile?.ExtensionData?.TryGetValue(DurableReplyKey, out var value) == true &&
        (value is true || value is JsonElement { ValueKind: JsonValueKind.True });
    public static bool RestoreReplyProof(SptProfile? profile)
    {
        if (!HasReplyProof(profile) || profile?.CharacterData?.PmcData is not { } pmc) return false;
        pmc.Variables ??= new();
        var changed = pmc.Variables.GetValueOrDefault(ProgressPolicy.Id(ReplyReadyFact)) != 1;
        pmc.Variables[ProgressPolicy.Id(ReplyReadyFact)] = 1;
        return changed;
    }
    // A saved q4 plant can only arrive through native end-local: there is no
    // E17 endpoint that writes plant counters/conditions. Recover the narrow
    // crash window between native raid save and our following proof save.
    public static bool RecoverSavedPlacement(SptProfile? profile)
    {
        var recovered = false;
        if (!HasReplyProof(profile) && ConditionDone(profile, PlacementId))
        {
            profile!.ExtensionData![DurableReplyKey] = true;
            recovered = true;
        }
        return RestoreReplyProof(profile) || recovered;
    }
}
