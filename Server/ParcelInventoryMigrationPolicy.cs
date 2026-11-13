using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace CustomQuests.E17.Server;

public sealed record ParcelInventoryMigrationResult(int Moved, string? Warning = null);

// Run before the native profile response is cloned. Planning never mutates the live inventory.
public static class ParcelInventoryMigrationPolicy
{
    public const string RevisionKey = "customquestsE17ParcelRevision";

    public static async Task<ParcelInventoryMigrationResult> Migrate(SptProfile? profile,
        TemplateTable templates, InventoryHelper inventory, Func<Task> save)
    {
        var source = profile?.CharacterData?.PmcData?.Inventory;
        var items = source?.Items;
        if (items == null) return new(0);
        var targetId = source!.QuestStashItems?.ToString();
        var carriedId = source.QuestRaidItems?.ToString();
        var parcels = items.Where(i => i.Template.ToString() == ParcelPolicy.TemplateId &&
            i.ParentId != targetId && i.ParentId != carriedId).ToList();
        if (parcels.Count == 0) return new(0);
        if (items.Select(i => i.Id).Distinct().Count() != items.Count)
            return new(0, "duplicate inventory item IDs; original parcel addresses retained");
        var target = items.SingleOrDefault(i => i.Id.ToString() == targetId);
        if (target == null || !templates.Items.TryGetValue(target.Template, out var targetTemplate))
            return new(0, "quest stash root or template is missing; original parcel addresses retained");
        var grid = targetTemplate.Properties?.Grids?.SingleOrDefault();
        if (grid?.Name != "hideout" || grid.Properties?.CellsH is not > 0 || grid.Properties.CellsV is not > 0)
            return new(0, "quest stash grid is invalid; original parcel addresses retained");
        if (!templates.Items.TryGetValue(ParcelPolicy.TemplateId, out var parcelTemplate) ||
            parcelTemplate.Properties?.QuestItem != true)
            return new(0, "quest parcel template is unavailable; original parcel addresses retained");

        var width = grid.Properties.CellsH.Value;
        var height = grid.Properties.CellsV.Value;
        var occupied = new bool[height, width];
        // The native occupancy helper skips malformed positions; fail closed instead of overlapping them.
        foreach (var item in items.Where(i => i.ParentId == targetId))
        {
            var at = item.GetParsedLocation();
            if (item.SlotId != "hideout" || at?.X == null || at.Y == null ||
                !templates.Items.ContainsKey(item.Template))
                return new(0, "quest stash contains an invalid item address; original parcel addresses retained");
            var size = inventory.GetItemSize(item.Template, item.Id, items);
            var w = at.IsVertical() ? size.Item2 : size.Item1;
            var h = at.IsVertical() ? size.Item1 : size.Item2;
            if (w <= 0 || h <= 0 || at.X < 0 || at.Y < 0 || at.X + w > width || at.Y + h > height)
                return new(0, "quest stash contains an out-of-bounds item; original parcel addresses retained");
            for (var y = at.Y.Value; y < at.Y.Value + h; y++)
                for (var x = at.X.Value; x < at.X.Value + w; x++)
                {
                    if (occupied[y, x]) return new(0, "quest stash contains overlapping items; original parcel addresses retained");
                    occupied[y, x] = true;
                }
        }

        var planned = items.Select(i => i with { }).ToList();
        var occupancy = inventory.GetContainerMap(width, height, planned, target.Id);
        var changes = new List<(Item Original, Item Planned)>();
        foreach (var parcel in parcels)
        {
            var copy = planned.Single(i => i.Id == parcel.Id);
            var tree = planned.GetItemWithChildren(copy.Id);
            var placed = inventory.PlaceItemInContainer(occupancy, tree, targetId!, "hideout");
            if (placed.Success != true || copy.ParentId != targetId || copy.SlotId != "hideout")
                return new(0, "quest stash is full; original parcel addresses retained");
            changes.Add((parcel, copy));
        }

        var before = changes.Select(c => (c.Original, c.Original.ParentId, c.Original.SlotId, c.Original.Location)).ToList();
        var extensions = profile!.ExtensionData ??= new();
        var hadRevision = extensions.TryGetValue(RevisionKey, out var priorRevision);
        foreach (var change in changes)
        {
            change.Original.ParentId = change.Planned.ParentId;
            change.Original.SlotId = change.Planned.SlotId;
            change.Original.Location = change.Planned.Location;
        }
        // SPT remembers the serialized hash before writing. A fresh nonce is required on every retry.
        extensions[RevisionKey] = Guid.NewGuid().ToString("N");
        try { await save(); }
        catch
        {
            foreach (var item in before)
            {
                item.Original.ParentId = item.ParentId;
                item.Original.SlotId = item.SlotId;
                item.Original.Location = item.Location;
            }
            if (hadRevision) extensions[RevisionKey] = priorRevision;
            else extensions.Remove(RevisionKey);
            throw;
        }
        return new(changes.Count);
    }
}
