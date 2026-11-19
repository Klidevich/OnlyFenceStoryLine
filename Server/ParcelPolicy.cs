using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;

namespace CustomQuests.E17.Server;

public static class ParcelPolicy
{
    public const string QuestId = "b1e561de6727b0871dba91e9";
    public const string TemplateId = "96a7ba6b4bc23cb1d7fc6717";
    public const string PlacementId = "de0d30cb048f4fe1178b5bcd";
    public const string ReplacementText = "e17 parcel replacement";

    public const long ReplacementCost = 50000;
    public static bool Received(SptProfile profile) =>
        profile.CharacterData?.PmcData?.Variables?.GetValueOrDefault(ProgressPolicy.Id("q2Received")) == 1;
    public enum Decision { MissingProfile, NotActive, Placed, PendingMail, Owned, Replace, InsufficientFunds }
    public static bool PayReplacement(SptProfile profile)
    {
        var items = profile.CharacterData!.PmcData!.Inventory!.Items!;
        var money = items.Where(x => x.Template.ToString() == "5449016a4bdc2d6f028b456f").ToList();
        if (money.Sum(x => Math.Floor(x.Upd?.StackObjectsCount ?? 0)) < ReplacementCost) return false;
        double remaining = ReplacementCost;
        foreach (var item in money)
        {
            var count = Math.Floor(item.Upd?.StackObjectsCount ?? 0);
            var take = Math.Min(count, remaining);
            if (take == count) items.Remove(item); else item.Upd!.StackObjectsCount = count - take;
            remaining -= take; if (remaining <= 0) break;
        }
        return true;
    }

    public static Decision Evaluate(SptProfile? profile, long now)
    {
        var pmc = profile?.CharacterData?.PmcData;
        if (pmc?.Inventory?.Items == null) return Decision.MissingProfile;
        var quest = pmc.Quests?.FirstOrDefault(q => q.QId.ToString() == QuestId);
        if (quest?.Status != QuestStatusEnum.Started) return Decision.NotActive;
        if (quest.CompletedConditions?.Contains(PlacementId) == true ||
            pmc.TaskConditionCounters?.Any(pair => pair.Key.ToString() == PlacementId &&
                pair.Value.SourceId?.ToString() == QuestId && pair.Value.Value >= 1) == true)
            return Decision.Placed;

        // Check mail BEFORE inventory. Native InventoryHelper.MoveItemToProfile
        // adds to the destination before removing the source attachment.
        // Reversing these reads can miss an item being collected between them.
        if (profile!.DialogueRecords?.Values.Any(d => d.Messages?.Any(m =>
                m.RewardCollected != true && m.Items?.Data?.Any(i => i.Template.ToString() == TemplateId) == true &&
                (m.DateTime == null || m.MaxStorageTime == null || m.MaxStorageTime <= 0 || now - m.DateTime < m.MaxStorageTime)) == true) == true)
            return Decision.PendingMail;
        if (pmc.Inventory.Items.Any(i => i.Template.ToString() == TemplateId) ||
            profile.CharacterData?.ScavData?.Inventory?.Items?.Any(i => i.Template.ToString() == TemplateId) == true)
            return Decision.Owned;
        return Decision.Replace;
    }
}
