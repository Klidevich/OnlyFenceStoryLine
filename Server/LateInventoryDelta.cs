using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Utils.Cloners;

namespace CustomQuests.E17.Server;

public static class LateInventoryDelta
{
    public static ItemEventRouterResponse Create(MongoId session, PmcData pmc, IEnumerable<Item> before, ICloner cloner)
    {
        var old = before.ToDictionary(x => x.Id);
        var current = (pmc.Inventory?.Items ?? []).ToDictionary(x => x.Id);
        var changes = new ItemChanges
        {
            NewItems = current.Values.Where(x => !old.ContainsKey(x.Id)).Select(x => cloner.Clone(x)!).ToList(),
            DeletedItems = old.Keys.Where(id => !current.ContainsKey(id)).Select(id => new DeletedItem { Id = id }).ToList(),
            // Late operations change counts or remove items; all addresses and other Upd values remain native-owned.
            ChangedItems = current.Values.Where(x => old.TryGetValue(x.Id, out var prior) && prior.Upd?.StackObjectsCount != x.Upd?.StackObjectsCount)
                .Select(x => new Item { Id = x.Id, ParentId = x.ParentId, SlotId = x.SlotId, Upd = new Upd { StackObjectsCount = x.Upd?.StackObjectsCount } }).ToList()
        };
        return new ItemEventRouterResponse
        {
            Warnings = [],
            ProfileChanges = new()
            {
                [session] = new ProfileChange
                {
                    Id = session, Items = changes, Quests = [], RagFairOffers = [], WeaponBuilds = [], EquipmentBuilds = [],
                    Production = new(), Improvements = new(), Health = null!, TraderRelations = new(), QuestsStatus = []
                }
            }
        };
    }
    public static bool HasChanges(ItemEventRouterResponse delta) => delta.ProfileChanges.Values.Any(p =>
        p.Items?.NewItems?.Count > 0 || p.Items?.ChangedItems?.Count > 0 || p.Items?.DeletedItems?.Count > 0);
}
