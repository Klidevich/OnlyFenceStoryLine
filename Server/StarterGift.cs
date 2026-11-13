using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;

namespace CustomQuests.E17.Server;

public static class StarterGift
{
    public const string FenceId = "579dc571d53a0658a154fbec";
    public const string MessageText = "e17 starter gift";
    public const string PistolId = "5448bd6b4bdc2dfc2f8b4569";
    public const string SlideId = "6374a822e629013b9c0645c8";
    public const string GripId = "6374a7e7417239a7bf00f042";
    public const string RearSightId = "63c6adcfb4ba094317063742";
    public const string MagazineId = "5448c12b4bdc2d02308b456f";
    public const string AmmoId = "5737201124597760fc4431f1";

    // The client treats zero as unlimited, but SPT 4.1.6 expires zero-duration
    // attachments. Use the client's positive fallback (about 68 years).
    // The template timestamp is server startup, not the character creation time.
    public const long StorageTime = int.MaxValue;

    public static int AddToTemplates(IEnumerable<ProfileSides> profiles, long now)
    {
        var added = 0;
        foreach (var profile in profiles)
        {
            if (AddToSide(profile.Bear, now)) added++;
            if (AddToSide(profile.Usec, now)) added++;
        }
        return added;
    }

    static bool AddToSide(TemplateSide? side, long now)
    {
        if (side == null) return false;
        side.Dialogues ??= [];
        if (!side.Dialogues.TryGetValue(FenceId, out var dialogue))
        {
            dialogue = new Dialogue
            {
                Id = FenceId, Type = MessageType.NpcTraderMessage,
                Messages = [], Pinned = false, New = 0, AttachmentsNew = 0
            };
            side.Dialogues.Add(FenceId, dialogue);
        }
        dialogue.Messages ??= [];
        if (dialogue.Messages.Any(message => message.TemplateId == MessageText)) return false;

        var stash = new MongoId();
        var pistol = new MongoId();
        var slide = new MongoId();
        dialogue.Messages.Add(new Message
        {
            Id = new MongoId(), UserId = FenceId, MessageType = MessageType.NpcTraderMessage,
            DateTime = now, TemplateId = MessageText, Text = "",
            HasRewards = true, RewardCollected = false, MaxStorageTime = StorageTime,
            Items = new MessageItems
            {
                Stash = stash,
                Data =
                [
                    new Item
                    {
                        Id = pistol, Template = PistolId, ParentId = stash, SlotId = "main",
                        Upd = new Upd
                        {
                            StackObjectsCount = 1,
                            Repairable = new UpdRepairable { Durability = 100, MaxDurability = 100 }
                        }
                    },
                    new Item { Id = slide, Template = SlideId, ParentId = pistol, SlotId = "mod_reciever" },
                    new Item { Id = new MongoId(), Template = GripId, ParentId = pistol, SlotId = "mod_pistolgrip" },
                    new Item { Id = new MongoId(), Template = RearSightId, ParentId = slide, SlotId = "mod_sight_rear" },
                    new Item { Id = new MongoId(), Template = MagazineId, ParentId = stash, SlotId = "main", Upd = new Upd { StackObjectsCount = 1 } },
                    new Item { Id = new MongoId(), Template = MagazineId, ParentId = stash, SlotId = "main", Upd = new Upd { StackObjectsCount = 1 } },
                    new Item { Id = new MongoId(), Template = AmmoId, ParentId = stash, SlotId = "main", Upd = new Upd { StackObjectsCount = 40 } }
                ]
            }
        });
        dialogue.New = (dialogue.New ?? 0) + 1;
        dialogue.AttachmentsNew = (dialogue.AttachmentsNew ?? 0) + 1;
        return true;
    }
}
