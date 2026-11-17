using System;
using EFT;
using EFT.Interactive;
using HarmonyLib;
using UnityEngine;
using VisitAPI.Native;

namespace CustomQuests.E17;

// Feed the original Sense -> OnSenseChanged -> ActionPanel pointer, not a second HUD marker.
internal static class QuestItemSense
{
    [ThreadStatic] static Player sensingPlayer;
    static readonly RaycastHit[] Hits = new RaycastHit[128];
    static int lootMask, obstructionMask;

    [HarmonyPatch(typeof(Player), nameof(Player.InteractionRaycast))]
    static class LocalRay
    {
        static void Prefix(Player __instance, out Player __state)
        {
            __state = sensingPlayer;
            sensingPlayer = Raid.Started && !Raid.InHideout && ReferenceEquals(GamePlayerOwner.MyPlayer, __instance) ? __instance : null;
        }
        static Exception Finalizer(Exception __exception, Player __state)
        {
            sensingPlayer = __state;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(GameWorld), nameof(GameWorld.InteractionSense))]
    static class NativeSense
    {
        static void Postfix(Vector3 origin, Vector3 direction, float radius, float distance, ref bool __result)
        {
            if (__result || sensingPlayer == null || distance <= 0f || radius <= 0f) return;
            if (lootMask == 0)
            {
                lootMask = LayerMask.GetMask("Interactive", "Loot");
                obstructionMask = LayerMask.GetMask("HighPolyCollider", "TransparentCollider");
            }
            // Static quest items use a trigger bounding collider. Explicitly include it instead
            // of depending on Physics.queriesHitTriggers or only the first broad-phase hit.
            // Keep the caller's native distance/radius (including Perception), and require LOS.
            var count = Physics.SphereCastNonAlloc(origin, radius, direction, Hits, distance, lootMask, QueryTriggerInteraction.Collide);
            try
            {
                for (var i = 0; i < count; i++)
                {
                    var hit = Hits[i];
                    if (hit.collider == null) continue;
                    var loot = hit.collider.GetComponentInParent<LootItem>();
                    if (loot == null || !loot.isActiveAndEnabled || loot.Item?.QuestItem != true ||
                        !Plugin.ItemQuests.ContainsKey(loot.Item.StringTemplateId) ||
                        !loot.IsValidForProfile(sensingPlayer.ProfileId)) continue;
                    if (Physics.Linecast(origin, hit.point, obstructionMask)) continue;
                    __result = true;
                    return;
                }
            }
            finally { Array.Clear(Hits, 0, count); }
        }
    }
}
