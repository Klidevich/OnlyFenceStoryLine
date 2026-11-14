using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using EFT;
using EFT.Dialogs;
using EFT.InventoryLogic;
using EFT.Quests;
using EFT.UI;
using HarmonyLib;
using VisitAPI.Dialog;
using VisitAPI.Native;
using VisitAPI.ChapterUI;

namespace CustomQuests.E17;

internal static class VisitFlow
{
    static ClientDialogController dialog;
    static QuestController quests;
    static DialogTree tree;
    static string returnNode = "e17_root";
    static BaseTraderDialogLine replay;
    static bool handoverBusy;
    static readonly Func<string, string, MongoID> LineId = (Func<string, string, MongoID>)AccessTools.Method(
        typeof(DialogTemplateBuilder), "Id", new[] { typeof(string), typeof(string) }).CreateDelegate(typeof(Func<string, string, MongoID>));
    static readonly FieldInfo WindowController = AccessTools.Field(typeof(TraderDialogWindow), "_dialogController");
    static readonly Dictionary<MongoID, (string node, DialogOption option)> options = new();
    static readonly HashSet<string> extra = new() { "e17_ours", "e17_archive", "e17_books", "e17_route", "e17_workshop", "e17_cargo" };

    internal static void Clear() { dialog = null; quests = null; tree = null; options.Clear(); returnNode = "e17_root"; }
    internal static void RefreshParcelChoices()
    {
        if (Session(dialog) && !dialog.CurrentDialog.IsBlocked) DialogPage.Redraw(dialog, true);
    }
    static bool OurTree(DialogTree t) => t?.TraderId == VisitEntryPolicy.FenceId && t.Nodes.ContainsKey("e17_intro") && t.Nodes.ContainsKey("e17_root");

    [HarmonyPatch(typeof(DialogTemplateBuilder), nameof(DialogTemplateBuilder.Register))]
    static class PrepareDialogueText
    {
        static void Prefix(DialogTree tree) => DialogueText.Prepare(tree);
    }

    [HarmonyPatch(typeof(OptionMap), nameof(OptionMap.Icon))]
    static class OtherQuestionsIcon
    {
        static void Postfix(DialogOption o, ref DialogLineTemplate.EDialogLineIconType __result)
        {
            // Return to E17's common topics, independent of the displayed RU/EN wording.
            if (o.Target == "e17_root" && __result == DialogLineTemplate.EDialogLineIconType.QuestionMark)
                __result = DialogLineTemplate.EDialogLineIconType.BackArrow;
        }
    }

    static bool Session(ClientDialogController dc) => dc != null && ReferenceEquals(dc, dialog) && dc.CurrentDialog != null &&
        QuestOps.IsLobby(quests) && QuestOps.IsLive(quests) && ReferenceEquals(QuestOps.Resolve(), quests);
    internal static bool Has(QuestController qc, string fact) => qc?.Profile?.ProfileVariables?.GetVariableValue(Vars.Id("cq_e17_v1_" + fact)) > 0;
    internal static bool Mark(QuestController qc, string fact)
    {
        if (qc?.Profile?.ProfileVariables == null || Has(qc, fact)) return false;
        qc.Profile.ProfileVariables.SetVariableValue(Vars.Id("cq_e17_v1_" + fact), 1);
        ProgressSync.Queue(qc, fact);
        ChapterEvents.Raise();
        return true;
    }
    internal static Quest First(QuestController qc) => qc?.Quests?.GetConditional(VisitEntryPolicy.FirstQuestId);
    internal static bool Given(Quest q, string tpl) => q != null && q.GetConditions<ConditionHandoverItem>(EQuestStatus.AvailableForFinish)
        .Any(c => c.target.Contains(tpl) && q.IsConditionDone(c));
    internal static bool Held(QuestController qc, string tpl) => qc?.InventoryController?.Inventory?
        .GetPlayerItems(EPlayerItems.AllExceptHideoutStashes).Any(i => i.StringTemplateId == tpl) == true;

    static ConditionHandoverItem ReadyHandover(Quest q, QuestController qc) =>
        q?.QuestStatus != EQuestStatus.Started || qc?.InventoryController?.Inventory == null ? null :
        q.ProgressCheckers.Keys.OfType<ConditionHandoverItem>()
            .Where(c => q.CheckVisibilityStatus(c) && !q.IsConditionDone(c))
            .FirstOrDefault(c => qc.GetItemsForCondition(c)?.Length > 0);

    static bool Allowed(DialogOption option, QuestController qc)
    {
        var q = First(qc); var target = option.Target;
        if (!ConversationPolicy.Allowed(target, option.AcceptIds.Count > 0 || option.CompleteIds.Count > 0 ||
            option.HandoverId != null, key => Has(qc, key))) return false;
        if (!Q2Flow.Allowed(option, qc)) return false;
        if (!Q3Flow.Allowed(option, qc)) return false;
        if (!Q4Flow.Allowed(option, qc)) return false;
        if (option.AcceptIds.Contains(VisitEntryPolicy.FirstQuestId) && !Q1FlowPolicy.CanAccept(key => Has(qc, key))) return false;
        if (target == "e17_resume") return !Q1FlowPolicy.CanAccept(key => Has(qc, key));
        if (target == "e17_offer") return q?.QuestStatus == EQuestStatus.AvailableForStart && Q1FlowPolicy.CanAccept(key => Has(qc, key));
        if (target?.StartsWith("e17_active_", StringComparison.Ordinal) == true)
            return q != null && q.QuestStatus is EQuestStatus.Started or EQuestStatus.AvailableForFinish &&
                target == Q1FlowPolicy.ActiveEntry(Given(q, NarrativePolicy.Ticket), Given(q, NarrativePolicy.Radio));
        if (target is "e17_report" or "e17_no_finds")
        {
            var active = q != null && q.QuestStatus is EQuestStatus.Started or EQuestStatus.AvailableForFinish;
            var ready = active && ((!Given(q, NarrativePolicy.Ticket) && Held(qc, NarrativePolicy.Ticket)) ||
                (!Given(q, NarrativePolicy.Radio) && Held(qc, NarrativePolicy.Radio)));
            return target == "e17_report" ? ready : active && !ready && !(Given(q, NarrativePolicy.Ticket) && Given(q, NarrativePolicy.Radio));
        }
        if (target is "e17_q2_entry" or "e17_q2_continue")
        {
            var q2 = Q2Flow.Quest(qc);
            var begun = Has(qc, "q2Ready") || q2?.QuestStatus is EQuestStatus.Started or EQuestStatus.AvailableForFinish or EQuestStatus.Success;
            return q?.QuestStatus == EQuestStatus.Success && Has(qc, "finished") && q2 != null &&
                (target == "e17_q2_continue" ? begun : !begun);
        }
        if (option.HandoverId == VisitEntryPolicy.FirstQuestId)
        {
            if (q?.QuestStatus != EQuestStatus.Started) return false;
            var ticket = target.StartsWith("e17_ticket_", StringComparison.Ordinal);
            var tpl = ticket ? NarrativePolicy.Ticket : NarrativePolicy.Radio;
            if (Given(q, tpl) || !Held(qc, tpl)) return false;
            return target == NarrativePolicy.HandoverTarget(ticket, Given(q, ticket ? NarrativePolicy.Radio : NarrativePolicy.Ticket),
                Plugin.Found(q, NarrativePolicy.Radio, qc.Profile), Held(qc, NarrativePolicy.Radio));
        }
        if (target is "e17_done_new" or "e17_done_ready")
        {
            if (q == null || !(q.QuestStatus is EQuestStatus.AvailableForFinish or EQuestStatus.Success)) return false;
            return (target == "e17_done_ready") == NarrativePolicy.Preparing(key => Has(qc, key));
        }
        if (target == "e17_ours") return Has(qc, "rescue");
        if (target is "e17_archive" or "e17_books") return Has(qc, "booksKnown") || Has(qc, "books");
        if (target == "e17_route") return Has(qc, "route");
        if (target is "e17_workshop" or "e17_cargo") return Has(qc, "radioFirst") || Has(qc, "radioSecond");
        return true;
    }
    sealed class PresentationGate : DialogCondition
    {
        readonly DialogOption option; readonly QuestController qc; readonly DialogMainConditionGroup original;
        internal PresentationGate(DialogOption o, QuestController q, DialogMainConditionGroup g) { option = o; qc = q; original = g; }
        public override EDialogConditionType Type => EDialogConditionType.QuestStatus;
        public override bool Test(IDialogContext c) => (original == null || original.Test(c)) && Allowed(option, qc);
    }
    [HarmonyPatch(typeof(QuestGates), nameof(QuestGates.Trigger))]
    static class OptionGates
    {
        static void Postfix(DialogOption o, QuestController quests, ref DialogMainConditionGroup __result)
        {
            if (o.Target?.StartsWith("e17_", StringComparison.Ordinal) != true) return;
            __result = new DialogMainConditionGroup(new[] { new DialogConditionSubGroup(new List<DialogCondition>
                { new PresentationGate(o, quests, __result) }) });
        }
    }
    [HarmonyPatch]
    static class EntryNode
    {
        static MethodBase TargetMethod() => AccessTools.DeclaredMethod(typeof(DialogOpener), "TryOpen", new[]
        {
            typeof(DialogTree), typeof(Profile), typeof(QuestController), typeof(InventoryController),
            typeof(TraderScreensGroup), typeof(string), typeof(bool), typeof(string).MakeByRefType(), typeof(bool)
        }) ?? throw new MissingMethodException("VisitAPI DialogOpener.TryOpen signature changed");
        static void Prefix(DialogTree tree, QuestController quests, bool scene, ref string forceNode)
        {
            if (!scene || forceNode != null || !OurTree(tree) || !QuestOps.IsLobby(quests)) return;
            var q = First(quests);
            if (VisitEntryPolicy.NeedsBriefing((int?)q?.QuestStatus ?? 0))
                forceNode = Q1FlowPolicy.BriefingEntry(key => Has(quests, key));
            else if (q.QuestStatus is EQuestStatus.Started or EQuestStatus.AvailableForFinish)
                forceNode = Q1FlowPolicy.ActiveEntry(Given(q, NarrativePolicy.Ticket), Given(q, NarrativePolicy.Radio));
            else if (q.QuestStatus == EQuestStatus.Success)
            {
                if (!Has(quests, "finished")) forceNode = NarrativePolicy.Preparing(key => Has(quests, key)) ? "e17_done_ready" : "e17_done_new";
                else if (Q4Flow.Available(quests) && Q4Flow.Begun(quests)) forceNode = Q4Flow.Entry(quests);
                else if (Q3Flow.Available(quests) && Q3Flow.Begun(quests)) forceNode = Q3Flow.Entry(quests);
                else if (Q2Flow.Quest(quests) != null && (Has(quests, "q2Ready") || Q2Flow.Quest(quests).QuestStatus is EQuestStatus.Started or EQuestStatus.AvailableForFinish or EQuestStatus.Success))
                    forceNode = Q2Flow.Entry(quests);
            }
        }
    }
    [HarmonyPatch(typeof(DialogSession), nameof(DialogSession.Begin))]
    static class RememberSession
    {
        static void Postfix(ClientDialogController dc, DialogTree tree, QuestController quests)
        {
            Clear();
            if (!OurTree(tree) || !QuestOps.IsLobby(quests)) return;
            VisitFlow.dialog = dc; VisitFlow.quests = quests; VisitFlow.tree = tree;
            foreach (var node in tree.Nodes.Values)
                for (var i = 0; i < node.Options.Count; i++)
                    options[LineId(tree.TraderId, node.Name + "#" + i)] = (node.Name, node.Options[i]);
        }
    }
    [HarmonyPatch(typeof(TraderDialogWindow), nameof(TraderDialogWindow.Redraw))]
    static class ShownAnswer
    {
        static void Postfix(TraderDialogWindow __instance, BaseTraderDialog dialog, bool __runOriginal)
        {
            // #npc is skipped by VisitAPI. #opt actually paints LastNpcLine and the choices.
            if (!__runOriginal || !Session(VisitFlow.dialog) || !ReferenceEquals(WindowController.GetValue(__instance), VisitFlow.dialog) ||
                !__instance.gameObject.activeInHierarchy || dialog?.DialogSide != EDialogSide.Player ||
                !DialogTemplateBuilder.NodeByDialog.TryGetValue(dialog.Id, out var node) ||
                VisitFlow.dialog.LastNpcLine?.Template.Id != LineId(VisitEntryPolicy.FenceId, node + "#say")) return;
            var stage = Q1FlowPolicy.StageFact(node);
            var marked = stage != null && Mark(quests, stage);
            marked |= Q2Flow.Shown(node, quests);
            marked |= Q3Flow.Shown(node, quests);
            marked |= Q4Flow.Shown(node, quests);
            if (!NarrativePolicy.Facts.TryGetValue(node, out var fact))
            {
                if (marked) Plugin.Instance.StartCoroutine(RefreshChoices(VisitFlow.dialog, dialog));
                return;
            }
            var q = First(quests);
            if (node.StartsWith("e17_ticket_") && !Given(q, NarrativePolicy.Ticket) ||
                node.StartsWith("e17_radio_") && !Given(q, NarrativePolicy.Radio) ||
                fact == "finished" && q?.QuestStatus != EQuestStatus.Success) return;
            marked |= Mark(quests, fact);
            if (marked) Plugin.Instance.StartCoroutine(RefreshChoices(VisitFlow.dialog, dialog));
        }
    }
    static IEnumerator RefreshChoices(ClientDialogController dc, BaseTraderDialog shown)
    {
        // Choices were evaluated before the newly displayed answer was recorded.
        yield return null;
        if (Session(dc) && ReferenceEquals(dc.CurrentDialog, shown) && !shown.IsBlocked) DialogPage.Redraw(dc, true);
    }
    static string ReturnEntry()
    {
        // Optional questions return to current progress, never to the answer just heard.
        if (returnNode.StartsWith("e17_q4_", StringComparison.Ordinal)) return Q4Flow.Entry(quests);
        if (returnNode.StartsWith("e17_q3_", StringComparison.Ordinal)) return Q3Flow.Entry(quests);
        if (returnNode.StartsWith("e17_q2_", StringComparison.Ordinal)) return Q2Flow.Entry(quests);
        var q = First(quests);
        if (VisitEntryPolicy.NeedsBriefing((int?)q?.QuestStatus ?? 0))
            return Q1FlowPolicy.BriefingEntry(key => Has(quests, key));
        if (q?.QuestStatus is EQuestStatus.Started or EQuestStatus.AvailableForFinish)
            return Q1FlowPolicy.ActiveEntry(Given(q, NarrativePolicy.Ticket), Given(q, NarrativePolicy.Radio));
        return "e17_root";
    }
    [HarmonyPatch(typeof(BaseTraderDialogController), nameof(BaseTraderDialogController.SetCurrentDialog))]
    static class ReturnToQuestion
    {
        static void Prefix(BaseTraderDialogController __instance, ref BaseTraderDialog dialog)
        {
            if (!ReferenceEquals(__instance, VisitFlow.dialog) || dialog == null || tree == null ||
                !DialogTemplateBuilder.NodeByDialog.TryGetValue(dialog.Id, out var node)) return;
            var destination = node == "e17_return" ? ReturnEntry() :
                node == "e17_resume" ? Q1FlowPolicy.BriefingEntry(key => Has(quests, key)) :
                node is "e17_q2_entry" or "e17_q2_continue" ? Q2Flow.Entry(quests) :
                node is "e17_q3_entry" or "e17_q3_continue" ? Q3Flow.Entry(quests) :
                node is "e17_q4_entry" or "e17_q4_continue" ? Q4Flow.Entry(quests) : null;
            if (destination != null) dialog = __instance.method_0(OptionMap.Entry(tree, destination));
        }
    }
    [HarmonyPatch(typeof(ClientDialogController), nameof(ClientDialogController.ExecuteLine))]
    static class ChooseLine
    {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(ClientDialogController __instance, BaseTraderDialogLine line, ref Task __result)
        {
            if (!Session(__instance) || line?.Template == null || !options.TryGetValue(line.Template.Id, out var entry)) return true;
            if (extra.Contains(entry.option.Target) || Q2Flow.Question(entry.option.Target) || Q3Flow.Question(entry.option.Target) || Q4Flow.Question(entry.option.Target)) returnNode = entry.node;
            if (!ReferenceEquals(replay, line) && (!Q3Flow.Allowed(entry.option, quests) || !Q4Flow.Allowed(entry.option, quests)))
            {
                __result = RefreshExpiredParcel(__instance);
                return false;
            }
            if (!Q2Flow.Chosen(entry.option, quests) || !Q4Flow.Chosen(entry.option, quests))
            {
                __result = RefreshExpiredParcel(__instance);
                return false;
            }
            if (!SpecificHandover(entry.option.HandoverId) || ReferenceEquals(replay, line)) return true;
            __result = handoverBusy || !Allowed(entry.option, quests) ? Task.CompletedTask : Handover(__instance, line, entry.option);
            return false;
        }
    }
    static bool SpecificHandover(string questId) => questId == VisitEntryPolicy.FirstQuestId || questId == Q3Policy.QuestId || questId == Q4Policy.QuestId;
    static async Task RefreshExpiredParcel(ClientDialogController dc)
    {
        // ExecuteLine has already blocked this page. Rebuild only after the click unwinds.
        await Task.Yield();
        if (Session(dc)) DialogPage.Reopen(dc, "E17 parcel availability changed");
    }
    [HarmonyPatch(typeof(EffectActions), nameof(EffectActions.Handover))]
    static class SuppressGenericHandover
    {
        // Our item-specific confirmation replaces the generic first-matching-item event.
        static bool Prefix(string questId, QuestController quests) => !SpecificHandover(questId) ||
            !ReferenceEquals(quests, VisitFlow.quests) || tree == null;
    }
    static async Task Handover(ClientDialogController dc, BaseTraderDialogLine line, DialogOption option)
    {
        var qc = quests; var q = qc.Quests.GetConditional(option.HandoverId);
        var tpl = option.HandoverId == Q4Policy.QuestId
            ? (option.Target == "e17_q4_claim" ? Q4Policy.Claim : Q4Policy.BusinessReply)
            : option.HandoverId == Q3Policy.QuestId
            ? (option.Target.StartsWith("e17_q3_parts_", StringComparison.Ordinal) ? Q3Policy.Parts : Q3Policy.Books)
            : (option.Target.StartsWith("e17_ticket_", StringComparison.Ordinal) ? NarrativePolicy.Ticket : NarrativePolicy.Radio);
        var condition = q.GetConditions<ConditionHandoverItem>(EQuestStatus.AvailableForFinish).Single(c => c.target.Contains(tpl));
        var items = qc.GetItemsForCondition(condition);
        if (items == null || items.Length == 0) return;
        var window = DialogScreenTracker.Live?._dialogWindow?.gameObject;
        var selection = new TaskCompletionSource<Item[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        handoverBusy = true;
        try
        {
            dc.CurrentDialog.IsBlocked = true;
            if (window != null) window.SetActive(false);
            var ctx = ItemUiContext.Instance.HandoverQuestItemsWindow.Show(condition, q.ProgressCheckers[condition].CurrentValue,
                items, qc.Profile, qc.InventoryController, chosen => selection.TrySetResult(chosen), true);
            ctx.OnClose += () => selection.TrySetResult(null);
            ctx.OnCloseSilent += () => selection.TrySetResult(null);
            var selected = await selection.Task;
            if (selected == null || selected.Length == 0 || !Session(dc)) return;
            await qc.HandoverItem(q, condition, selected, true);
            if (!q.IsConditionDone(condition) || !Session(dc)) return;
            RefreshQuestBadge.Invalidate();
            ChapterEvents.Raise();
            replay = line;
            if (window != null) window.SetActive(true);
            await dc.ExecuteLine(line); // Native history and response only after actual successful handover.
        }
        catch (Exception e) { Plugin.Warn("E17 handover failed: " + e); }
        finally
        {
            replay = null; handoverBusy = false;
            if (Session(dc))
            {
                if (window != null) window.SetActive(true);
                DialogPage.Reopen(dc, "E17 handover");
            }
        }
    }
    [HarmonyPatch(typeof(TraderBadge), "NeedsTalk")]
    static class StoryVisitBadge
    {
        static bool Prefix(Quest q, QuestController qc, ref bool __result)
        {
            if (q == null || !Plugin.Quests.Contains(q.Id)) return true;
            // New work or at least one actual, unsurrendered hand-in item warrants a visit.
            // TraderBadge still owns its config, trader unlock checks and other mods' quests.
            __result = q.Template != null && QuestOps.IsLobby(qc) && QuestOps.IsLive(qc) &&
                ChapterChain.Reachable(qc, q) && (q.Id != Q3Policy.QuestId || Q3Flow.Available(qc)) &&
                (q.Id != Q4Policy.QuestId || Q4Flow.Available(qc)) &&
                VisitEntryPolicy.NeedsVisit((int)q.QuestStatus, ReadyHandover(q, qc) != null);
            return false;
        }
    }
    [HarmonyPatch(typeof(TraderBadge), "Sweep")]
    static class RefreshQuestBadge
    {
        static readonly FieldInfo SweepUntil = AccessTools.Field(typeof(TraderBadge), "_sweepUntil");
        static QuestController lastController;
        static int lastState;
        internal static void Invalidate() => SweepUntil.SetValue(null, 0f);
        static void Prefix(QuestController qc)
        {
            var state = 0;
            foreach (var id in Plugin.Quests) state = state * 16 + ((int?)qc?.Quests?.GetConditional(id)?.QuestStatus ?? -1) + 1;
            if (ReferenceEquals(qc, lastController) && state == lastState) return;
            lastController = qc; lastState = state;
            Invalidate();
        }
    }
    [HarmonyPatch(typeof(QuestGates), nameof(QuestGates.PendingItems))]
    static class FindReadyHandover
    {
        static bool Prefix(Quest quest, ref ConditionItem __result)
        {
            if (quest == null || !Plugin.Quests.Contains(quest.Id)) return true;
            var qc = QuestOps.Resolve();
            __result = ReadyHandover(quest, qc);
            return false;
        }
    }
}
