using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using EFT;
using EFT.InventoryLogic;
using EFT.Communications;
using EFT.Quests;
using HarmonyLib;
using JsonType;
using UnityEngine;
using VisitAPI.Native;
using VisitAPI.ChapterUI;

namespace CustomQuests.E17;

// Client integration for the content included in this release.
[BepInPlugin("com.klidevich.customquests.e17", "OnlyFenceStoryLine", "0.1.1")]
[BepInDependency("com.sora.visitapi")]
public sealed class Plugin : BaseUnityPlugin
{
    internal static Plugin Instance;
    internal static void Warn(string message) => Instance?.Logger.LogWarning(message);
    internal const string ChapterId = "806248ae7f482f8beb294c88";
    internal static readonly HashSet<string> Quests = new()
    {
        "c50c495eb5a978728eaf9d59", "b1e561de6727b0871dba91e9"
    };
    internal static readonly Dictionary<string, string> ItemQuests = new()
    {
        ["582c4d5c43fdd55ee98b20bc"] = "c50c495eb5a978728eaf9d59",
        ["e208363938a46fbc1507af21"] = "c50c495eb5a978728eaf9d59",
        ["90eb63a906ed4dff2fbbf0d8"] = "b1e561de6727b0871dba91e9"
    };
    InventoryController watched;
    QuestController controller;
    float nextRefresh;
    Harmony harmony;
    float nextErrorLog;
    string lastHeld = "";

    void Awake()
    {
        Instance = this;
        E17Text.Lookup = source => VisitAPI.Loc.Key(E17Text.Prefix + source) ?? source;
        harmony = new Harmony("com.klidevich.customquests.e17");
        harmony.PatchAll(typeof(Plugin).Assembly);
        Logger.LogInfo("OnlyFenceStoryLine runtime 0.1.1 loaded; current objectives, localized title and quest inventory delivery.");
    }

    void OnDestroy()
    {
        VisitFlow.Clear();
        IntroController.Dispose();
        ParcelStatusSync.Dispose();
        if (watched != null) watched.AddItemEvent -= OnAdded;
        harmony?.UnpatchSelf();
    }

    void Update()
    {
        try { Tick(); }
        catch (Exception e)
        {
            if (Time.unscaledTime < nextErrorLog) return;
            nextErrorLog = Time.unscaledTime + 10f;
            Logger.LogError("E17 update failed: " + e);
        }
    }

    void Tick()
    {
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.25f;
        var qc = QuestOps.Resolve();
        if (qc == null || !QuestOps.IsLive(qc)) return;
        NarrativeUI.Tick(qc);
        ProgressSync.Tick(qc);
        if (!ReferenceEquals(controller, qc))
        {
            if (watched != null) watched.AddItemEvent -= OnAdded;
            controller = qc;
            watched = qc.InventoryController;
            if (watched != null) watched.AddItemEvent += OnAdded;
        }
        Refresh(qc);
        Q2Flow.Observe(qc);
    }

    void OnAdded(AddItemEventArgs e)
    {
        if (e?.Status != CommandStatus.Succeed || e.Item == null || !ItemQuests.ContainsKey(e.Item.StringTemplateId)) return;
        if (controller == null || !QuestOps.IsLive(controller)) return;
        try
        {
            var tpl = e.Item.StringTemplateId;
            var q = controller.Quests?.GetConditional(ItemQuests[tpl]);
            if (!Running(q)) return;
            var first = !Found(q, tpl, controller.Profile);
            Refresh(controller);
            if (first && Found(q, tpl, controller.Profile))
                NotificationManager.DisplayMessageNotification((tpl + " Name").Localized() + E17Text.Get("t350b114ddc8c2ac9"));
        }
        catch (Exception error) { Logger.LogError("E17 discovery event failed: " + error); }
    }

    static bool Running(Quest q) => q != null && (q.QuestStatus == EQuestStatus.Started || q.QuestStatus == EQuestStatus.AvailableForFinish);

    // A native TaskConditionCounter records discovery, independently of current possession.
    // SPT copies these counters on raid end, including death; the FindItem CompletedConditions
    // cleanup can still run. Respawn eligibility below uses possession + actual handover instead.
    void Refresh(QuestController qc)
    {
        var inventory = qc.InventoryController?.Inventory;
        if (inventory == null || qc.Profile == null) return;
        var held = new HashSet<string>(inventory.GetPlayerItems(EPlayerItems.AllExceptHideoutStashes).Select(i => i.StringTemplateId));
        var signature = qc.Profile.Id + ":" + string.Join(",", held.Where(ItemQuests.ContainsKey).OrderBy(x => x));
        var inventoryChanged = signature != lastHeld;
        lastHeld = signature;
        foreach (var q in qc.Quests.Where(q => Quests.Contains(q.Id) && Running(q)).ToList())
        {
            var changed = false;
            foreach (var c in q.GetConditions<ConditionFindItem>(EQuestStatus.AvailableForFinish))
            {
                if (!c.target.Any(t => ItemQuests.TryGetValue(t, out var owner) && owner == q.Id)) continue;
                var counter = qc.Profile.GetTaskConditionCounter(q, c.id);
                if (c.target.Any(held.Contains) && counter.Value < 1)
                {
                    counter.Value = 1;
                    changed = true;
                }
                if (!q.ProgressCheckers.TryGetValue(c, out var checker)) continue;
                var previous = checker.HasGetter() ? checker.CurrentValue : 0;
                checker.SetCurrentValueGetter(_ => counter.Value >= 1 ? 1 : 0);
                changed |= previous != checker.CurrentValue;
            }
            if (changed)
            {
                q.UpdateCompletedConditions(EQuestStatus.AvailableForFinish);
                q.CheckForStatusChange(fromServer: false, canFail: false);
                ChapterEvents.Raise();
            }
        }
        if (inventoryChanged) ChapterEvents.Raise();
    }

    internal static bool Found(Quest q, string tpl, Profile profile) =>
        profile != null && q.GetConditions<ConditionFindItem>(EQuestStatus.AvailableForFinish)
            .Where(c => c.target.Contains(tpl)).Any(c => profile.GetTaskConditionCounter(q, c.id).Value >= 1);

    [HarmonyPatch(typeof(QuestOps), nameof(QuestOps.Finish))]
    static class KeepChapterOpen
    {
        // These are the first three assignments, not the end of the E-17 story.
        static bool Prefix(Quest quest) => quest?.Id != ChapterId;
    }

    [HarmonyPatch(typeof(ChapterStates), nameof(ChapterStates.Snap))]
    static class ShowSingleItemCounters
    {
        static void Postfix(Quest quest, Condition cond, ref ChapterStates.RowSnap __result)
        {
            if (quest == null || !Quests.Contains(quest.Id) || cond is not ConditionItem || cond.value != 1) return;
            if (__result.State != ChapterStates.ERow.Active && __result.State != ChapterStates.ERow.Done) return;
            __result.Counting = true;
            __result.Cur = __result.State == ChapterStates.ERow.Done ? 1 : 0;
            __result.Fill = __result.Cur;
        }
    }

    [HarmonyPatch(typeof(ChapterModel), nameof(ChapterModel.Notes))]
    static class LostItemHints
    {
        static void Postfix(ChapterModel __instance, ref IEnumerable<(string id, string text, Quest quest, List<(ChapterModel.ELink type, string tpl, string raw)> links)> __result)
        {
            if (__instance.Quest?.Id != ChapterId) return;
            __result = __result.Concat(Hints(__instance));
        }

        static IEnumerable<(string id, string text, Quest quest, List<(ChapterModel.ELink type, string tpl, string raw)> links)> Hints(ChapterModel chapter)
        {
            var qc = QuestOps.Resolve();
            if (qc?.Profile == null || qc.InventoryController?.Inventory == null) yield break;
            var held = new HashSet<string>(qc.InventoryController.Inventory.GetPlayerItems(EPlayerItems.AllExceptHideoutStashes).Select(i => i.StringTemplateId));
            foreach (var q in chapter.Subs.Where(Running))
                foreach (var give in q.GetConditions<ConditionHandoverItem>(EQuestStatus.AvailableForFinish))
                {
                    if (q.IsConditionDone(give)) continue;
                    var tpl = give.target.FirstOrDefault(t => ItemQuests.ContainsKey(t));
                    if (tpl == null || held.Contains(tpl) || !Found(q, tpl, qc.Profile)) continue;
                    var name = (tpl + " Name").Localized();
                    yield return (q.Id + ":missing:" + tpl, name + E17Text.Get("tb05fe0931d9b2238"), q,
                        new List<(ChapterModel.ELink, string, string)>());
                }
        }
    }

    [HarmonyPatch(typeof(StoryList), nameof(StoryList.Hidden))]
    static class HideRegularStoryEntries
    {
        static void Postfix(Quest q, ref bool __result)
        {
            if (q != null && (q.Id == ChapterId || Quests.Contains(q.Id))) __result = true;
        }
    }

    [HarmonyPatch(typeof(QuestBook), nameof(QuestBook.GetQuests), typeof(string))]
    static class HideTraderStoryEntries
    {
        // VisitAPI's list filter is optional. E17 is always presented through STORY and VISIT.
        static void Postfix(ref IEnumerable<Quest> __result)
        {
            if (__result != null) __result = __result.Where(q => q == null || (q.Id != ChapterId && !Quests.Contains(q.Id)));
        }
    }

    [HarmonyPatch(typeof(Conditional<Quest>), nameof(Conditional<Quest>.CheckVisibilityStatus))]
    static class RevealClues
    {
        static void Postfix(Conditional<Quest> __instance, Condition condition, ref bool __result)
        {
            if (__instance is not Quest q || !Quests.Contains(q.Id)) return;
            var qc = QuestOps.Resolve();
            if (qc?.Profile == null || qc.Quests?.GetConditional(q.Id) != q) return;
            if (q.Id == "c50c495eb5a978728eaf9d59" && condition is ConditionItem item && item.target.Contains("e208363938a46fbc1507af21"))
            {
                var radio = Found(q, "e208363938a46fbc1507af21", qc.Profile);
                __result = radio || (condition is ConditionFindItem && Found(q, "582c4d5c43fdd55ee98b20bc", qc.Profile));
            }
            if (q.Id == "b1e561de6727b0871dba91e9" && condition is ConditionLeaveItemAtLocation)
                __result = Found(q, "90eb63a906ed4dff2fbbf0d8", qc.Profile);
        }
    }

    [HarmonyPatch(typeof(GameWorld), nameof(GameWorld.SpawnLootItem))]
    static class RestoreLostFinds
    {
        static void Prefix(JsonLootItem lootItem, Player questPlayer)
        {
            if (questPlayer == null || lootItem?.Item == null) return;
            var tpl = lootItem.Item.StringTemplateId;
            if (!ItemQuests.TryGetValue(tpl, out var questId)) return;
            var q = questPlayer.QuestController?.Quests?.GetConditional(questId);
            if (!Running(q)) return;
            var inventory = questPlayer.Profile?.Inventory;
            if (inventory == null || inventory.GetPlayerItems(EPlayerItems.QuestItems).Any(i => i.StringTemplateId == tpl)) return;
            // The workshop note is not handed in; after discovery its text is sufficient.
            if (tpl == "90eb63a906ed4dff2fbbf0d8" && Found(q, tpl, questPlayer.Profile)) return;
            if (q.GetConditions<ConditionHandoverItem>(EQuestStatus.AvailableForFinish)
                .Any(c => c.target.Contains(tpl) && q.IsConditionDone(c))) return;
            MongoID me = questPlayer.ProfileId;
            if (lootItem.ValidProfiles == null) lootItem.ValidProfiles = new[] { me };
            else if (!lootItem.ValidProfiles.Contains(me)) lootItem.ValidProfiles = lootItem.ValidProfiles.Concat(new[] { me }).ToArray();
        }
    }
}
