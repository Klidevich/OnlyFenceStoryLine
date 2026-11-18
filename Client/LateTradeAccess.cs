using System;
using System.Collections.Generic;
using EFT;
using HarmonyLib;
using VisitAPI.Native;
namespace CustomQuests.E17;

internal static class LateTradeAccess
{
    // OnlyFence filters these vanilla traders independently of the native unlock flag.
    // Keep its modded-trader setting intact; do not implement or persist story gates here.
    static readonly HashSet<string> StandardTraders = new(StringComparer.OrdinalIgnoreCase)
    {
        "54cb50c76803fa8b248b4571", "54cb57776803fa99248b456e",
        "58330581ace78e27b8b10cee", "5935c25fb3acc3127c3d8cd9",
        "5a7c2eca46aef81a7ca2145d", "5ac3b934156ae10c4430e83c",
        "5c0647fdd443bc2504c2d371", "6617beeaa9cfa777ca915b7c"
    };
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
        if (__result || !StandardTraders.Contains(id)) return;
        var profile = VisitBackend.Profile;
        if (profile?.TradersInfo != null && profile.TradersInfo.TryGetValue(new MongoID(id), out var info) &&
            info?.Unlocked == true) __result = true;
    }
}
