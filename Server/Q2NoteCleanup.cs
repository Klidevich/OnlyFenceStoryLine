using SPTarkov.Server.Core.Callbacks;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Enums;

namespace CustomQuests.E17.Server;

public static class Q2NoteCleanup
{
    public const string NoteTemplate = "90eb63a906ed4dff2fbbf0d8";

    // Called inside ParcelRequestGate, with the live PMC and the native response holder.
    public static async Task<ItemEventRouterResponse> Complete(PmcData pmc, MongoId questId, MongoId session,
        ItemEventRouterResponse output, Func<ValueTask<ItemEventRouterResponse>> next,
        InventoryHelper inventory, CancellationToken ct)
    {
        if (questId.ToString() != ParcelPolicy.QuestId) return await next();
        ct.ThrowIfCancellationRequested();
        var alreadyComplete = Success(pmc);
        // Native QuestHelper.CompleteQuest applies rewards again even for Success quests.
        var result = alreadyComplete ? output : await next();
        // Once native completion committed, finish its synchronous cleanup even if the request was cancelled meanwhile.
        if (!Success(pmc) || ItemEventCallbacks.IsCriticalError(result.Warnings)) return result;

        var items = pmc.Inventory?.Items;
        if (items == null || !result.ProfileChanges.TryGetValue(session, out var changes) || changes.Items?.DeletedItems == null)
            return result;
        var roots = new HashSet<string>();
        void Root(string? id, string template)
        {
            if (!string.IsNullOrEmpty(id) && items.Count(i => i.Id.ToString() == id && i.Template.ToString() == template) == 1)
                roots.Add(id);
        }
        Root(pmc.Inventory!.QuestRaidItems?.ToString(), "5963866286f7747bf429b572");
        Root(pmc.Inventory.QuestStashItems?.ToString(), "5963866b86f7747bfa1c4462");
        var notes = items.Where(i => i.Template.ToString() == NoteTemplate && i.ParentId != null && roots.Contains(i.ParentId) &&
            !roots.Contains(i.Id.ToString()) && items.Count(other => other.Id == i.Id) == 1 &&
            !items.Any(child => child.ParentId == i.Id.ToString())).Select(i => i.Id).ToArray();
        foreach (var id in notes) inventory.RemoveItem(pmc, id, session, result);
        return result;
    }

    static bool Success(PmcData pmc) => pmc.Quests?.Any(q => q.QId.ToString() == ParcelPolicy.QuestId &&
        q.Status == QuestStatusEnum.Success) == true;
}
