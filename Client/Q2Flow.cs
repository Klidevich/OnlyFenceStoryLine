using System;
using System.Linq;
using EFT;
using EFT.Communications;
using EFT.InventoryLogic;
using EFT.Quests;
using VisitAPI.Dialog;
using VisitAPI.Native;
using VisitAPI.ChapterUI;

namespace CustomQuests.E17;

internal static class Q2Flow
{
    static QuestController watched;
    static bool wasHeld;
    static bool wasPlaced;
    static bool wasInQuestStash;
    static bool InQuestStash(QuestController qc)
    {
        var stash = qc?.InventoryController?.Inventory?.QuestStashItems;
        return stash != null && new[] { stash }.GetAllItemsFromCollections().Any(i => i.StringTemplateId == Q2Policy.Parcel);
    }
    internal static Quest Quest(QuestController qc) => qc?.Quests?.GetConditional(Q2Policy.QuestId);
    internal static bool Placed(Quest q) => q != null && (q.QuestStatus == EQuestStatus.Success ||
        q.GetConditions<ConditionLeaveItemAtLocation>(EQuestStatus.AvailableForFinish).Any(q.IsConditionDone));
    internal static bool Found(QuestController qc) => Quest(qc) is Quest q && qc.Profile != null && Plugin.Found(q, Q2Policy.Note, qc.Profile);
    static int Status(QuestController qc) => (int?)Quest(qc)?.QuestStatus ?? 0;
    internal static string Entry(QuestController qc) => Q2Policy.Entry(Status(qc), Placed(Quest(qc)), f => VisitFlow.Has(qc, f));
    internal static bool Question(string target) => target is "e17_q2_wait" or "e17_q2_loss" or "e17_q2_remind" or "e17_q2_remind_reported";
    internal static bool Allowed(DialogOption option, QuestController qc)
    {
        var status = Status(qc); var placed = Placed(Quest(qc));
        if (option.Target is "e17_workshop") return !Found(qc) && status != 4;
        if (option.Target is "e17_cargo") return !VisitFlow.Has(qc, "q2Cargo");
        if (option.Target?.StartsWith("e17_q2_", StringComparison.Ordinal) != true) return true;
        if (VisitFlow.First(qc)?.QuestStatus != EQuestStatus.Success || Quest(qc) == null) return false;
        return Q2Policy.Allowed(option.Target, status, Found(qc), placed, VisitFlow.Held(qc, Q2Policy.Parcel),
            f => VisitFlow.Has(qc, f), ParcelStatusSync.State(qc));
    }
    internal static bool Shown(string node, QuestController qc)
    {
        if (!Q2Policy.Facts.TryGetValue(node, out var fact)) return false;
        if ((fact == "q2Result" || fact == "q2Cargo") && Status(qc) != 4) return false;
        if (fact == "q2Reported" && !Found(qc)) return false;
        return VisitFlow.Mark(qc, fact);
    }
    internal static bool Chosen(DialogOption option, QuestController qc)
    {
        if (option.Target is not ("e17_q2_replace" or "e17_q2_missing")) return true;
        if (!QuestOps.IsLobby(qc) || !QuestOps.IsLive(qc) || !Allowed(option, qc)) return false;
        ParcelStatusSync.Replace(qc);
        return true;
    }
    internal static void Observe(QuestController qc)
    {
        var q = Quest(qc); var held = VisitFlow.Held(qc, Q2Policy.Parcel); var placed = Placed(q);
        var inQuestStash = InQuestStash(qc);
        var changedController = !ReferenceEquals(watched, qc);
        if (changedController) { watched = qc; wasHeld = held; wasPlaced = placed; }
        if (q != null && Q2Policy.Active((int)q.QuestStatus))
        {
            // Existing owned items are legitimate receipt evidence; acceptance and mail are not.
            if (held && !placed && (!wasHeld || !VisitFlow.Has(qc, "q2Received")))
            {
                VisitFlow.Mark(qc, "q2Received");
                NotificationManager.DisplayMessageNotification(E17Text.Get("t6c9e9672f769dd0b"));
            }
            if (placed && !wasPlaced && !changedController)
                NotificationManager.DisplayMessageNotification(E17Text.Get("t7d1f98302af571f5"));
        }
        if (!changedController && (wasHeld != held || wasPlaced != placed || wasInQuestStash != inQuestStash)) ChapterEvents.Raise();
        wasHeld = held; wasPlaced = placed; wasInQuestStash = inQuestStash;
        ParcelStatusSync.Tick(qc, (int?)q?.QuestStatus == (int)EQuestStatus.Started && !placed && !held);
    }
    internal static string Journal(QuestController qc) => Q2Policy.Journal(Status(qc), Placed(Quest(qc)), VisitFlow.Held(qc, Q2Policy.Parcel), f => VisitFlow.Has(qc, f));
    internal static string Objective(QuestController qc) => Q2Policy.Objective(Status(qc), Found(qc), Placed(Quest(qc)),
        VisitFlow.Held(qc, Q2Policy.Parcel), VisitFlow.Has(qc, "q2Received"), ParcelStatusSync.State(qc), InQuestStash(qc));
}
