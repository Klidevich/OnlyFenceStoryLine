using System;
using EFT;
using EFT.Interactive;
using EFT.Quests;
using EFT.UI;
using HarmonyLib;

namespace CustomQuests.E17;

// Keep the native five-second plant/removal transaction; only explain its item requirement.
[HarmonyPatch(typeof(InteractionContextHelper), nameof(InteractionContextHelper.GetAvailableActions),
    new[] { typeof(GamePlayerOwner), typeof(PlaceItemTrigger) })]
internal static class Q2PlacementFeedback
{
    internal const string ZoneId = "af68292f93ff46c546260c99";
    static void Postfix(GamePlayerOwner owner, PlaceItemTrigger itemTrigger, ref AvailableInteractionState __result)
    {
        if (itemTrigger?.Id != ZoneId || owner?.Player == null) return;
        var qc = owner.Player.QuestController;
        var q = Q2Flow.Quest(qc);
        if (q?.QuestStatus != EQuestStatus.Started || !Q2Flow.Found(qc) || Q2Flow.Placed(q)) return;
        if (__result != null)
        {
            foreach (var action in __result.Actions)
                if (action.Name == "PLACE") action.Name = E17Text.Get("t5a36d84dd30dd9b4");
            return;
        }
        __result = new AvailableInteractionState();
        __result.Actions.Add(new InteractionAction
        {
            Name = E17Text.Get("t65d7ac5e35a2f7d1"),
            Action = () => owner.DisplayPreloaderUiNotification(E17Text.Get("ta57d3a6d7a9b4438"))
        });
    }
}
