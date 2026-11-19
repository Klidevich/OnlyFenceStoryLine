using System;
using System.Linq;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using EFT.Quests;
using EFT.UI;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using VisitAPI.Native;
namespace CustomQuests.E17;

// Native plant animation and inventory transaction, with story/generation guards before consumption.
[HarmonyPatch(typeof(InteractionContextHelper), nameof(InteractionContextHelper.GetAvailableActions),
    new[] { typeof(GamePlayerOwner), typeof(PlaceItemTrigger) })]
internal static class LatePlacements
{
    static JObject[] At(string zone) => LatePolicy.Quests.SelectMany(q => q["conditions"].OfType<JObject>())
        .Where(c => (string)c["type"] == "LeaveItemAtLocation" && LatePolicy.Id("zone/" + (string)c["zone"]) == zone).ToArray();
    static int Generation(QuestController qc, string field) => (int)(qc?.Profile?.ProfileVariables?
        .GetVariableValue(Vars.Id("cq_e17_v1_server_" + field)) ?? 0);
    static string Proof(QuestController qc, string key)
    {
        if (key == "q20/place/finalAuthorization")
        {
            var generation = Generation(qc, "q20_authorizationGeneration");
            return generation > 0 ? LatePolicy.Id("q20/authorization/" + generation) : null;
        }
        if (key.StartsWith("q18/", StringComparison.Ordinal) && key.IndexOf("family", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            var generation = Generation(qc, "q18_familyGeneration");
            return generation > 0 ? LatePolicy.Id("q18/redeposit/family/" + generation) : null;
        }
        return null;
    }
    static bool CanPlace(QuestController qc, JObject definition)
    {
        if (!QuestOps.IsLive(qc) || QuestOps.IsLobby(qc) || !LateFlow.Test(qc, definition["when"])) return false;
        var key = (string)definition["key"]; var q = LateFlow.Quest(qc, key.Split('/')[0]);
        var condition = q?.GetConditions<ConditionLeaveItemAtLocation>(EQuestStatus.AvailableForFinish)
            .FirstOrDefault(c => c.id == LatePolicy.Id(key));
        if (q == null || condition == null) return false;
        var proof = Proof(qc, key);
        if (key == "q20/place/finalAuthorization" && proof == null) return false;
        if (proof != null && StoryCounters.Value(qc.Profile, q.Id, proof) >= 1) return false;
        var repeat = key == "q18/place/familyClosedCopy" && proof != null &&
            LateFlow.Atom(qc, "server:q18.familyCanRedeliver") && (ActiveOrComplete(q));
        return repeat || LateFlow.Active(q) && !q.IsConditionDone(condition);
    }
    static bool ActiveOrComplete(Quest quest) => LateFlow.Active(quest) || quest.QuestStatus == EQuestStatus.Success;
    static bool Prefix(GamePlayerOwner owner, PlaceItemTrigger itemTrigger, ref AvailableInteractionState __result)
    {
        if (itemTrigger == null || owner?.Player == null) return true;
        var candidates = At(itemTrigger.Id);
        if (candidates.Length == 0) return true;
        __result = null;
        var qc = owner.Player.QuestController;
        foreach (var definition in candidates)
        {
            if (!CanPlace(qc, definition)) continue;
            var tpl = LatePolicy.Id((string)definition["item"]);
            var carried = owner.Player.Profile.Inventory.QuestRaidItems;
            var item = carried == null ? null : new[] { carried }.GetAllItemsFromCollections().FirstOrDefault(i => i.StringTemplateId == tpl);
            if (item == null) continue;
            __result = new AvailableInteractionState();
            __result.Actions.Add(new InteractionAction { Name = "PLACE", Action = () => Plant(owner, itemTrigger, definition, item) });
            break;
        }
        return false;
    }
    static void Plant(GamePlayerOwner owner, PlaceItemTrigger zone, JObject definition, Item item)
    {
        var qc = owner.Player.QuestController;
        if (!CanPlace(qc, definition) || owner.Player.PlaceItemZone != zone) return;
        if (owner.Player.CurrentState is not IdlePlayerState)
        { owner.DisplayPreloaderUiNotification("You can't plant quest item while moving".Localized()); return; }
        var seconds = definition["plantTime"]?.Value<float>() ?? 5f;
        var key = (string)definition["key"]; var proof = Proof(qc, key);
        owner.ShowObjectivesPanel("Hiding objective {0:F1}", seconds);
        owner.Player.CurrentManagedState.Plant(true, false, seconds, successful =>
        {
            owner.CloseObjectivesPanel();
            if (!successful || !CanPlace(qc, definition) || owner.Player.PlaceItemZone != zone || Proof(qc, key) != proof) return;
            owner.Player.InventoryController.TryRunNetworkTransaction(ItemManipulator.Remove(item, owner.Player.InventoryController, simulate: true), result =>
            {
                if (!result.Succeed) return;
                owner.Player.PlantItemNetwork(item.TemplateId, zone.Id, true);
                if (proof != null)
                {
                    var q = LateFlow.Quest(qc, key.Split('/')[0]);
                    StoryCounters.Mark(qc.Profile, q.Id, proof, "LeaveItemAtLocation");
                }
                owner.ClearInteractionState();
            });
        });
    }
}
