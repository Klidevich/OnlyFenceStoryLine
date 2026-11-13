using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.Quests;
using EFT.UI;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VisitAPI.ChapterUI;
using VisitAPI.Native;

namespace CustomQuests.E17;

internal static class NarrativeUI
{
    [HarmonyPatch(typeof(ChapterModel), nameof(ChapterModel.Conditions))]
    static class CurrentQuestConditions
    {
        static void Postfix(ChapterModel __instance,
            ref IEnumerable<(Quest quest, Condition cond, bool primary)> __result)
        {
            if (__instance.Quest?.Id != Plugin.ChapterId) return;
            // VisitAPI includes completed prerequisites and expanded history here.
            // Keep that history in Notes, not in the current objectives of E17.
            __result = __result.Where(row => row.quest.QuestStatus != EQuestStatus.Success);
        }
    }
    static readonly MenuStartDelay StartDelay = new();
    internal static void Tick(QuestController qc)
    {
        var inMenu = IntroController.MainMenuVisible;
        var chapter = qc.Quests?.GetConditional(Plugin.ChapterId);
        if (StartDelay.ShouldStart(qc, QuestOps.IsLobby(qc), inMenu,
                chapter?.QuestStatus == EQuestStatus.AvailableForStart, Time.unscaledTime))
            QuestOps.Accept(qc, chapter, "E17 delayed menu start");
        IntroController.Tick(qc, inMenu);
    }

    [HarmonyPatch(typeof(ChapterModel), nameof(ChapterModel.Notes))]
    static class ConditionalJournal
    {
        static void Postfix(ChapterModel __instance, ref IEnumerable<(string id, string text, Quest quest, List<(ChapterModel.ELink type, string tpl, string raw)> links)> __result)
        {
            if (__instance.Quest?.Id != Plugin.ChapterId) return;
            var qc = QuestOps.Resolve(); var q = VisitFlow.First(qc);
            if (qc?.Profile == null) return;
            var facts = new HashSet<string>(NarrativePolicy.JournalFacts.Where(f => VisitFlow.Has(qc, f)));
            bool Has(string key) => facts.Contains(key);
            var active = q != null && q.QuestStatus is EQuestStatus.Started or EQuestStatus.AvailableForFinish or EQuestStatus.Success;
            var ticket = VisitFlow.Given(q, NarrativePolicy.Ticket); var radio = VisitFlow.Given(q, NarrativePolicy.Radio);
            var success = q?.QuestStatus == EQuestStatus.Success;
            var signature = string.Join("-", facts.OrderBy(f => f)) + (active ? "-accepted" : "") + (ticket ? "-ticket" : "") + (radio ? "-radio" : "") + (success ? "-success" : "");
            var rows = new List<(string, string, Quest, List<(ChapterModel.ELink, string, string)>)>();
            var conversation = NarrativePolicy.Conversation(Has, active);
            var debrief = NarrativePolicy.Debrief(Has, ticket, radio, success);
            if (conversation.Length > 0) rows.Add(("e17-conversation-" + signature, conversation, __instance.Quest, new()));
            if (debrief.Length > 0) rows.Add(("e17-debrief-" + signature, debrief, q, new()));
            var delivery = Q2Flow.Journal(qc);
            if (delivery.Length > 0) rows.Add(("e17-q2-journal", delivery, Q2Flow.Quest(qc), new()));
            __result = __result.Concat(rows);
        }
    }

    [HarmonyPatch(typeof(MainQuestTabView), "FillTasks")]
    static class CurrentObjective
    {
        static void Postfix(MainQuestTabView __instance, ChapterModel ch)
        {
            var list = __instance._tasksView?._mainTasksList;
            var parent = list?._conditionsContainer;
            if (parent == null) return;
            var existing = parent.Find("E17CurrentObjective");
            var qc = QuestOps.Resolve(); var q = VisitFlow.First(qc);
            var active = ch.Quest.Id == Plugin.ChapterId && q != null && q.QuestStatus is EQuestStatus.Started or EQuestStatus.AvailableForFinish;
            var q2Text = ch.Quest.Id == Plugin.ChapterId ? Q2Flow.Objective(qc) : "";
            if (!active && q2Text.Length == 0) { if (existing != null) existing.gameObject.SetActive(false); return; }
            var text = active ? NarrativePolicy.Objective(true, Plugin.Found(q, NarrativePolicy.Ticket, qc.Profile), Plugin.Found(q, NarrativePolicy.Radio, qc.Profile),
                VisitFlow.Held(qc, NarrativePolicy.Ticket), VisitFlow.Held(qc, NarrativePolicy.Radio),
                VisitFlow.Given(q, NarrativePolicy.Ticket), VisitFlow.Given(q, NarrativePolicy.Radio)) : q2Text;
            var label = existing != null ? existing.GetComponent<TextMeshProUGUI>() : null;
            if (label == null)
            {
                var go = new GameObject("E17CurrentObjective", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
                go.transform.SetParent(parent, false);
                label = go.GetComponent<TextMeshProUGUI>();
                label.font = list._conditionsViewTemplate._titleField.font;
                label.fontSize = 19; label.enableWordWrapping = true; label.raycastTarget = false;
                label.color = new Color(0.85f, 0.82f, 0.69f); label.margin = new Vector4(12, 6, 12, 12);
            }
            label.gameObject.SetActive(true); label.transform.SetAsFirstSibling(); label.text = text;
            var width = Math.Max(250f, ((RectTransform)parent).rect.width - 24f);
            label.GetComponent<LayoutElement>().preferredHeight = label.GetPreferredValues(text, width, float.PositiveInfinity).y + 24f;
        }
    }
}
