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
    internal static bool ReadyReport(QuestController qc) =>
        VisitFlow.First(qc)?.QuestStatus == EQuestStatus.Success && VisitFlow.Has(qc, "q1RadioV018") &&
        Q2Policy.ReadyReport(Status(qc), Found(qc), Placed(Quest(qc)), f => VisitFlow.Has(qc, f));
    internal static string Entry(QuestController qc) => Q2Policy.Entry(Status(qc), Placed(Quest(qc)), f => VisitFlow.Has(qc, f));
    internal static bool Question(string target) => target is "e17_q2_wait" or "e17_q2_loss" or "e17_q2_friend" or "e17_q2_remind" or "e17_q2_remind_reported" or "e17_q2_workshop" or "e17_q2_archive_workshop" or "e17_q2_archive_arrival" or "e17_q2_archive_cargo";
    internal static bool Allowed(DialogOption option, QuestController qc)
    {
        var status = Status(qc); var placed = Placed(Quest(qc));
        if (!Q2Policy.ContextAllows(option.Target, status, Found(qc), f => VisitFlow.Has(qc, f))) return false;
        if (option.Target is "e17_workshop" or "e17_cargo") return true;
        if (option.Target?.StartsWith("e17_q2_", StringComparison.Ordinal) != true) return true;
        if (VisitFlow.First(qc)?.QuestStatus != EQuestStatus.Success || Quest(qc) == null || !VisitFlow.Has(qc, "q1RadioV018")) return false;
        return Q2Policy.Allowed(option.Target, status, Found(qc), placed, VisitFlow.Held(qc, Q2Policy.Parcel),
            f => VisitFlow.Has(qc, f), ParcelStatusSync.State(qc));
    }
    internal static bool Shown(string node, QuestController qc)
    {
        if (!Q2Policy.Facts.TryGetValue(node, out var fact)) return false;
        if ((fact == "q2ResultV018" || fact == "q2CargoV018") && Status(qc) != 4) return false;
        if (fact == "q2Reported" && !Found(qc)) return false;
        return VisitFlow.Mark(qc, fact);
    }
    // Called only after the full note text has actually been painted, never on pickup.
    internal static bool ReadNote(QuestController qc) => Q2Policy.CanReadNote(Found(qc), f => VisitFlow.Has(qc, f)) &&
        VisitFlow.Mark(qc, Q2Policy.NoteReadFact);
    internal static bool Chosen(DialogOption option, QuestController qc)
    {
        if (option.Target is not ("e17_q2_replace" or "e17_q2_missing")) return true;
        if (!QuestOps.IsLobby(qc) || !QuestOps.IsLive(qc) || !Allowed(option, qc)) return false;
        if (option.Target == "e17_q2_replace") VisitFlow.Mark(qc, "q2ReplacementRequested");
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
            if (held && !placed && VisitFlow.Has(qc, "q2ReplacementRequested"))
                VisitFlow.Mark(qc, "q2ReplacementReceived");
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
