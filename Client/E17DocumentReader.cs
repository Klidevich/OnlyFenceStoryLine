using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Comfort.Common;
using EFT;
using EFT.Dialogs;
using EFT.InventoryLogic;
using EFT.Quests;
using EFT.UI;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using VisitAPI.ChapterUI;
using VisitAPI.Dialog;
using VisitAPI.Native;

namespace CustomQuests.E17;

// Native item inspection is also VisitAPI's chapter-material viewer. A receipt is
// recorded only after its complete description has reached a visible rendered panel.
// Chapter unread markers, inventory acquisition and journal enumeration are unrelated.
internal static class E17DocumentReader
{
    static readonly FieldInfo PanelItem = AccessTools.Field(typeof(ItemSpecificationPanel), "_item");
    static readonly FieldInfo WindowController = AccessTools.Field(typeof(TraderDialogWindow), "_dialogController");
    static readonly ConditionalWeakTable<ItemSpecificationPanel, PanelVersion> Versions = new();
    static readonly HashSet<string> OpenedInSession = new();
    sealed class PanelVersion { internal int Value; }

    static QuestController Controller()
    {
        var qc = QuestOps.Resolve();
        if (QuestOps.IsLive(qc)) return qc;
        // Inventory shown in the hideout can use a presentation copy. Persist only
        // against the matching live backend profile, never a different character.
        var lobby = QuestOps.Lobby;
        return qc?.Profile?.Id == lobby?.Profile?.Id && QuestOps.IsLive(lobby) ? lobby : null;
    }

    static bool Has(QuestController qc, string fact) => VisitFlow.Has(qc, fact);
    static bool Found(QuestController qc, Quest quest, string template) => quest != null && qc?.Profile != null &&
        (Plugin.Found(quest, template, qc.Profile) || VisitFlow.Held(qc, template));
    static bool Available(QuestController qc, string template)
    {
        if (qc?.Profile == null) return false;
        if (template == NarrativePolicy.Ticket) return Found(qc, VisitFlow.First(qc), template) ||
            VisitFlow.Given(VisitFlow.First(qc), template) || VisitFlow.First(qc)?.QuestStatus == EQuestStatus.Success;
        if (template == Q2Policy.Note) return Found(qc, Q2Flow.Quest(qc), template) || Has(qc, Q2Policy.NoteReadFact);
        if (template == Q3Policy.Books) return Found(qc, Q3Flow.Quest(qc), template) ||
            VisitFlow.Given(Q3Flow.Quest(qc), template) || Q3Flow.Quest(qc)?.QuestStatus == EQuestStatus.Success;
        if (template == Q4Policy.Request) return VisitFlow.Held(qc, template) || Has(qc, "q4Received") || Has(qc, "q4RequestRead");
        if (template == Q4Policy.PersonalRequest) return VisitFlow.Held(qc, template) || Has(qc, "q4PersonalRead");
        if (template == Q4Policy.PersonalCopy) return Found(qc, Q4Flow.Quest(qc), template) ||
            Has(qc, "q4CopyRead") || Has(qc, "q4PersonalExtracted");
        return false;
    }

    static void Record(QuestController qc, string template)
    {
        if (template == Q2Policy.Note) Q2Flow.ReadNote(qc);
        else if (template == Q3Policy.Books) Q3Flow.ReadBooks(qc);
        else
        {
            var fact = template == Q4Policy.Request ? "q4RequestRead" :
                template == Q4Policy.PersonalRequest ? "q4PersonalRead" :
                template == Q4Policy.PersonalCopy ? "q4CopyRead" : null;
            if (fact != null && !Has(qc, fact)) Q4Flow.Read(qc, template);
        }
    }

    static void ObservePanel(ItemSpecificationPanel panel)
    {
        if (panel == null || Plugin.Instance == null || PanelItem.GetValue(panel) is not Item item) return;
        var qc = Controller();
        if (!Available(qc, item.StringTemplateId)) return;
        var version = Versions.GetValue(panel, _ => new PanelVersion());
        var generation = ++version.Value;
        Plugin.Instance.StartCoroutine(AfterPaint(panel, item, qc, version, generation));
    }

    static IEnumerator AfterPaint(ItemSpecificationPanel panel, Item item, QuestController qc, PanelVersion version, int generation)
    {
        // Rendering boundary, not a reading timer or scroll/confirmation test.
        yield return new WaitForEndOfFrame();
        if (version.Value != generation || panel == null || !panel.gameObject.activeInHierarchy ||
            !ReferenceEquals(PanelItem.GetValue(panel), item) || !ReferenceEquals(Controller(), qc) || !panel.Examined) yield break;
        var description = panel._itemLabels?._description;
        var expected = item.Description.Localized();
        if (description == null || !description.isActiveAndEnabled || string.IsNullOrWhiteSpace(expected) ||
            description.text != expected || !Available(qc, item.StringTemplateId)) yield break;
        Record(qc, item.StringTemplateId);
        VisitFlow.RefreshParcelChoices();
    }

    [HarmonyPatch(typeof(ItemSpecificationPanel), nameof(ItemSpecificationPanel.Show))]
    static class InspectDocument
    {
        static void Postfix(ItemSpecificationPanel __instance) => ObservePanel(__instance);
    }

    // An initially unidentified item can reveal its description while the same
    // native window remains open. method_1 is the verified description renderer.
    [HarmonyPatch(typeof(ItemSpecificationPanel), nameof(ItemSpecificationPanel.method_1))]
    static class DescriptionRevealed
    {
        static void Postfix(ItemSpecificationPanel __instance) => ObservePanel(__instance);
    }

    static bool Open(string template)
    {
        if (ItemUiContext.Instance == null || !Singleton<ItemFactory>.Instantiated) return false;
        try
        {
            var item = Singleton<ItemFactory>.Instance.CreateItem(MongoID.Generate().ToString(), template, null);
            ItemUiContext.Instance.Inspect(new DefaultItemContext(item, EItemViewType.Handbook), null);
            return true;
        }
        catch (Exception error) { Plugin.Warn("E17 document inspection failed: " + error.Message); return false; }
    }

    [HarmonyPatch(typeof(DialogSession), nameof(DialogSession.Begin))]
    static class NewConversation
    {
        static void Postfix() => OpenedInSession.Clear();
    }

    [HarmonyPatch(typeof(TraderDialogWindow), nameof(TraderDialogWindow.Redraw))]
    static class A04Document
    {
        static void Postfix(TraderDialogWindow __instance, BaseTraderDialog dialog, bool __runOriginal)
        {
            var qc = Controller();
            var dc = VisitFlow.CurrentDialog;
            if (!__runOriginal || qc == null || !QuestOps.IsLobby(qc) || dc == null ||
                !ReferenceEquals(WindowController.GetValue(__instance), dc) || !__instance.gameObject.activeInHierarchy ||
                dialog?.DialogSide != EDialogSide.Player || !ReferenceEquals(dc.CurrentDialog, dialog) ||
                !DialogTemplateBuilder.NodeByDialog.TryGetValue(dialog.Id, out var node) || !Q3Policy.ShowsBook(node) ||
                !Available(qc, Q3Policy.Books) || OpenedInSession.Contains(node)) return;
            // The document stays in the native inspector, without Fence's speaker
            // name. Its normal description renderer owns the reading receipt.
            if (Open(Q3Policy.Books)) OpenedInSession.Add(node);
        }
    }

    sealed class BookWasDisplayed : DialogCondition
    {
        readonly DialogMainConditionGroup original;
        readonly QuestController qc;
        internal BookWasDisplayed(DialogMainConditionGroup original, QuestController qc) { this.original = original; this.qc = qc; }
        public override EDialogConditionType Type => EDialogConditionType.QuestStatus;
        public override bool Test(IDialogContext context) => (original == null || original.Test(context)) && Has(qc, Q3Policy.BooksReadFact);
    }

    [HarmonyPatch(typeof(QuestGates), nameof(QuestGates.Trigger))]
    static class ExplanationAfterDocument
    {
        static void Postfix(DialogOption o, QuestController quests, ref DialogMainConditionGroup __result)
        {
            // This orders the A04 presentation only; completion and its reward have
            // already happened and never require a separate prior reading action.
            if (o.Target != "e17_q3_ransom") return;
            __result = new DialogMainConditionGroup(new[] { new DialogConditionSubGroup(new List<DialogCondition>
                { new BookWasDisplayed(__result, quests) }) });
        }
    }

    [HarmonyPatch(typeof(ChapterModel), nameof(ChapterModel.ActiveItems))]
    static class SavedMaterials
    {
        static void Postfix(ChapterModel __instance, ref IEnumerable<(ChapterModel.ELink type, string tpl, string raw)> __result)
        {
            if (__instance.Quest?.Id != Plugin.ChapterId) return;
            var qc = Controller();
            var documents = new[] { NarrativePolicy.Ticket, Q2Policy.Note, Q3Policy.Books, Q4Policy.Request, Q4Policy.PersonalRequest, Q4Policy.PersonalCopy }
                .Where(template => Available(qc, template))
                .Select(template => (ChapterModel.ELink.Item, template, "e17-document:" + template));
            // Merely adding a saved icon does not mark the document as read.
            var original = __result.ToList();
            var listed = new HashSet<string>(original.Select(link => link.tpl));
            __result = original.Concat(documents.Where(link => !listed.Contains(link.Item2)));
        }
    }

    static bool CanRecover(QuestController qc)
    {
        var q = Q4Flow.Quest(qc);
        return qc != null && QuestOps.IsLobby(qc) && q?.QuestStatus == EQuestStatus.Started &&
            Has(qc, "q4RequestRead") && !Has(qc, "q4Attached") && !Q4Flow.Placed(q) && !Q4Flow.RepliesReady(qc) &&
            !VisitFlow.Held(qc, Q4Policy.PersonalRequest) && !Q4ActionSync.Busy;
    }

    [HarmonyPatch(typeof(MainQuestTabView), "FillTasks")]
    static class PreparePersonalRequest
    {
        static void Postfix(MainQuestTabView __instance, ChapterModel ch)
        {
            var list = __instance._tasksView?._mainTasksList;
            var parent = list?._conditionsContainer;
            if (parent == null) return;
            var template = list._conditionsViewTemplate?._dialogButtonsContainer?._visitTraderButton;
            var qc = Controller(); var chapter = ch?.Quest?.Id == Plugin.ChapterId;
            Bind(parent, template, "E17AttachPersonalRequest", chapter && Q4Flow.CanAttach(qc),
                LatePolicy.SystemText(/* release-text:Client.E17DocumentReader.5 */ "", /* release-text:Client.E17DocumentReader.4 */ "", /* release-text:Client.E17DocumentReader.3 */ ""), qc, () => Q4Flow.Attach(qc));
            Bind(parent, template, "E17RecoverPersonalRequest", chapter && CanRecover(qc),
                LatePolicy.SystemText(/* release-text:Client.E17DocumentReader.2 */ "", /* release-text:Client.E17DocumentReader.1 */ "", /* release-text:Client.E17DocumentReader.0 */ ""), qc, () => Q4Flow.RecoverPersonal(qc));
        }
    }

    static void Bind(Transform parent, DefaultUIButton template, string name, bool available, string text, QuestController qc, Action action)
    {
        var old = parent.Find(name);
        if (!available || template == null) { if (old != null) old.gameObject.SetActive(false); return; }
        var button = old != null ? old.GetComponent<DefaultUIButton>() : UnityEngine.Object.Instantiate(template, parent);
        button.name = name; button.gameObject.SetActive(true); button.Interactable = true;
        var layout = button.GetComponent<LayoutElement>() ?? button.gameObject.AddComponent<LayoutElement>();
        layout.minHeight = layout.preferredHeight = 44;
        button.SetRawText(text, 16); button.OnClick.RemoveAllListeners();
        button.OnClick.AddListener(() =>
        {
            if (!ReferenceEquals(Controller(), qc) || Q4ActionSync.Busy) return;
            button.Interactable = false; action();
            Plugin.Instance.StartCoroutine(RefreshAfterAction(qc));
        });
        button.transform.SetAsFirstSibling();
    }

    static IEnumerator RefreshAfterAction(QuestController qc)
    {
        yield return null;
        while (Q4ActionSync.Busy && ReferenceEquals(Controller(), qc)) yield return null;
        if (ReferenceEquals(Controller(), qc)) ChapterEvents.Raise();
    }
}
