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

internal static class Q4Flow
{
    static QuestController watched;
    static bool wasHeld, wasPlaced;
    internal static Quest Quest(QuestController qc) => qc?.Quests?.GetConditional(Q4Policy.QuestId);
    internal static bool RepliesReady(QuestController qc) => VisitFlow.Has(qc, "q4ReplyReady");
    internal static bool Placed(Quest q) => q != null && (q.QuestStatus == EQuestStatus.Success ||
        q.GetConditions<ConditionLeaveItemAtLocation>(EQuestStatus.AvailableForFinish).Any(q.IsConditionDone));
    static Q4State State(QuestController qc)
    {
        var q = Quest(qc);
        var stash = qc?.InventoryController?.Inventory?.QuestStashItems;
        bool found(string tpl) => q != null && Plugin.Found(q, tpl, qc.Profile);
        return new Q4State
        {
            Status = (int?)q?.QuestStatus ?? 0, ClaimFound = found(Q4Policy.Claim),
            ClaimGiven = VisitFlow.Given(q, Q4Policy.Claim), ClaimHeld = VisitFlow.Held(qc, Q4Policy.Claim),
            RequestHeld = VisitFlow.Held(qc, Q4Policy.Request), Placed = Placed(q), RepliesReady = RepliesReady(qc),
            InQuestStash = stash != null && new[] { stash }.GetAllItemsFromCollections().Any(i => i.StringTemplateId == Q4Policy.Request),
            BusinessFound = found(Q4Policy.BusinessReply), BusinessGiven = VisitFlow.Given(q, Q4Policy.BusinessReply),
            BusinessHeld = VisitFlow.Held(qc, Q4Policy.BusinessReply), PersonalFound = found(Q4Policy.PersonalCopy),
            Parcel = Q4RequestSync.State(qc)
        };
    }
    internal static bool Available(QuestController qc) => Q4Policy.Available((int?)Q3Flow.Quest(qc)?.QuestStatus ?? 0, f => VisitFlow.Has(qc, f));
    internal static bool Begun(QuestController qc) => VisitFlow.Has(qc, "q4Ready") || Q4Policy.Briefed(f => VisitFlow.Has(qc, f)) || (int?)Quest(qc)?.QuestStatus is 2 or 3 or 4;
    internal static string Entry(QuestController qc) => Available(qc) ? Q4Policy.Entry(State(qc), f => VisitFlow.Has(qc, f)) : Q3Flow.Entry(qc);
    internal static bool Question(string target) => target is "e17_q4_wait" or "e17_q4_debt";
    internal static bool Allowed(DialogOption option, QuestController qc)
    {
        if (option.Target is "e17_q3_entry" or "e17_q3_continue") return !Available(qc) || !Begun(qc);
        if (option.Target?.StartsWith("e17_q4_", StringComparison.Ordinal) != true &&
            !option.AcceptIds.Contains(Q4Policy.QuestId) && !option.CompleteIds.Contains(Q4Policy.QuestId) && option.HandoverId != Q4Policy.QuestId) return true;
        if (!Available(qc) || Quest(qc) == null) return false;
        var s = State(qc);
        if (option.AcceptIds.Contains(Q4Policy.QuestId) && !Q4Policy.Allowed("e17_q4_accepted", s, f => VisitFlow.Has(qc, f))) return false;
        if (option.CompleteIds.Contains(Q4Policy.QuestId) && !Q4Policy.Allowed("e17_q4_paid", s, f => VisitFlow.Has(qc, f))) return false;
        if (option.Target is "e17_q4_entry" or "e17_q4_continue") return option.Target == "e17_q4_continue" ? Begun(qc) : !Begun(qc);
        return Q4Policy.Allowed(option.Target, s, f => VisitFlow.Has(qc, f));
    }
    internal static bool Shown(string node, QuestController qc) => Available(qc) &&
        Q4Policy.CanRecord(node, State(qc), f => VisitFlow.Has(qc, f)) && VisitFlow.Mark(qc, Q4Policy.Facts[node]);
    internal static bool Chosen(DialogOption option, QuestController qc)
    {
        if (option.Target is not ("e17_q4_issue" or "e17_q4_replace")) return true;
        if (!QuestOps.IsLobby(qc) || !QuestOps.IsLive(qc) || !Allowed(option, qc)) return false;
        Q4RequestSync.Replace(qc);
        return true;
    }
    internal static void Observe(QuestController qc)
    {
        var s = State(qc);
        var controllerChanged = !ReferenceEquals(watched, qc);
        if (controllerChanged) { watched = qc; wasHeld = s.RequestHeld; wasPlaced = s.Placed; }
        if (Q4Policy.Active(s.Status))
        {
            if (s.ClaimGiven && s.RequestHeld && !s.Placed && !VisitFlow.Has(qc, "q4Received"))
            {
                VisitFlow.Mark(qc, "q4Received");
                NotificationManager.DisplayMessageNotification(E17Text.Get("t402481adb9c9f17f"));
            }
            if (s.Placed && !wasPlaced && !controllerChanged)
                NotificationManager.DisplayMessageNotification(E17Text.Get("t0d9e0d52f57c11d1"));
        }
        if (!controllerChanged && (wasHeld != s.RequestHeld || wasPlaced != s.Placed)) ChapterEvents.Raise();
        wasHeld = s.RequestHeld; wasPlaced = s.Placed;
        Q4RequestSync.Tick(qc, Q4Policy.Active(s.Status) && s.ClaimGiven &&
            (!s.Placed && !s.RequestHeld || s.Placed && !s.RepliesReady));
    }
    internal static string Journal(QuestController qc) => Q4Policy.Journal(State(qc), f => VisitFlow.Has(qc, f));
    internal static string Objective(QuestController qc) => Q4Policy.Objective(State(qc), f => VisitFlow.Has(qc, f));
    internal static void Dispose() { Q4RequestSync.Dispose(); watched = null; wasHeld = wasPlaced = false; }
}
