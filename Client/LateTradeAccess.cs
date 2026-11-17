using System.Reflection;
using EFT.Quests;
using HarmonyLib;
using VisitAPI.Native;
namespace CustomQuests.E17;

internal static class LateTradeAccess
{
    internal static void Install(Harmony harmony)
    {
        var type = AccessTools.TypeByName("OnlyFenceClient.OnlyFenceTraderScreensPatch");
        if (type == null) return;
        var method = AccessTools.DeclaredMethod(type, "ShouldShow", new[] { typeof(string) });
        if (method == null || !method.IsStatic || method.ReturnType != typeof(bool))
        { Plugin.Warn("OnlyFence trader access extension unavailable: unsupported ShouldShow signature"); return; }
        harmony.Patch(method, postfix: new HarmonyMethod(typeof(LateTradeAccess), nameof(AfterShouldShow)));
    }
    static void AfterShouldShow(string id, ref bool __result)
    {
        var qc = QuestOps.Resolve();
        if (qc == null) return;
        if (id == LatePolicy.Trader("Therapist") && LateFlow.Quest(qc, "q14")?.QuestStatus == EQuestStatus.Success ||
            id == LatePolicy.Trader("Prapor") && LateFlow.Quest(qc, "q17")?.QuestStatus == EQuestStatus.Success) __result = true;
    }
}
