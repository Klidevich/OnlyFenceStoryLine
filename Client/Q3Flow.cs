using System;
using EFT;
using EFT.Quests;
using VisitAPI.Dialog;

namespace CustomQuests.E17;

internal static class Q3Flow
{
    internal static Quest Quest(QuestController qc) => qc?.Quests?.GetConditional(Q3Policy.QuestId);
    static int Status(QuestController qc) => (int?)Quest(qc)?.QuestStatus ?? 0;
    internal static bool Available(QuestController qc) => Q3Policy.Available((int?)Q2Flow.Quest(qc)?.QuestStatus ?? 0, f => VisitFlow.Has(qc, f));
    internal static bool Begun(QuestController qc) => VisitFlow.Has(qc, "q3Rudin") || Q3Policy.Active(Status(qc)) || Status(qc) == 4;
    internal static string Entry(QuestController qc)
    {
        if (!Available(qc)) return Q2Flow.Entry(qc);
        var q = Quest(qc);
        return Q3Policy.Entry(Status(qc), VisitFlow.Given(q, Q3Policy.Parts), VisitFlow.Given(q, Q3Policy.Books), f => VisitFlow.Has(qc, f));
    }
    internal static bool Question(string target) => target == "e17_q3_condition";
    internal static bool Allowed(DialogOption option, QuestController qc)
    {
        if (option.Target is "e17_books" or "e17_q2_entry" or "e17_q2_continue")
        {
            var cargo = Quest(qc);
            return Q3Policy.ContextAllows(option.Target, cargo != null && Plugin.Found(cargo, Q3Policy.Books, qc.Profile),
                VisitFlow.Given(cargo, Q3Policy.Books), Status(qc), Begun(qc), Available(qc));
        }
        if (option.Target?.StartsWith("e17_q3_", StringComparison.Ordinal) != true &&
            !option.AcceptIds.Contains(Q3Policy.QuestId) && !option.CompleteIds.Contains(Q3Policy.QuestId) && option.HandoverId != Q3Policy.QuestId) return true;
        if (!Available(qc) || Quest(qc) == null) return false;
        if (option.AcceptIds.Contains(Q3Policy.QuestId) && (Status(qc) != 1 || !VisitFlow.Has(qc, "q3Brief"))) return false;
        var q = Quest(qc); var parts = VisitFlow.Given(q, Q3Policy.Parts); var books = VisitFlow.Given(q, Q3Policy.Books);
        if (!Q3Policy.ContextAllows(option.Target, Plugin.Found(q, Q3Policy.Books, qc.Profile), books, Status(qc), Begun(qc), Available(qc))) return false;
        if (option.CompleteIds.Contains(Q3Policy.QuestId) && (Status(qc) != 3 || !parts || !books)) return false;
        if (option.Target is "e17_q3_entry" or "e17_q3_continue")
            return option.Target == "e17_q3_continue" ? Begun(qc) : !Begun(qc);
        return Q3Policy.Allowed(option.Target, Status(qc), parts, books, VisitFlow.Held(qc, Q3Policy.Parts),
            VisitFlow.Held(qc, Q3Policy.Books), f => VisitFlow.Has(qc, f));
    }
    internal static bool Shown(string node, QuestController qc)
    {
        var q = Quest(qc);
        return Available(qc) && Q3Policy.CanRecord(node, Status(qc), VisitFlow.Given(q, Q3Policy.Parts), VisitFlow.Given(q, Q3Policy.Books),
            f => VisitFlow.Has(qc, f)) && VisitFlow.Mark(qc, Q3Policy.Facts[node]);
    }
    // The caller must have displayed the full document (item, chapter materials, or A04 document panel).
    internal static bool ReadBooks(QuestController qc)
    {
        var q = Quest(qc);
        var available = q != null && (Plugin.Found(q, Q3Policy.Books, qc.Profile) ||
            VisitFlow.Given(q, Q3Policy.Books) || q.QuestStatus == EQuestStatus.Success);
        return Q3Policy.CanReadBooks(available, f => VisitFlow.Has(qc, f)) && VisitFlow.Mark(qc, Q3Policy.BooksReadFact);
    }
    internal static string Journal(QuestController qc)
    {
        var q = Quest(qc);
        if (q == null) return "";
        return Q3Policy.Journal(Status(qc), VisitFlow.Given(q, Q3Policy.Parts), VisitFlow.Given(q, Q3Policy.Books),
            f => VisitFlow.Has(qc, f), Plugin.Found(q, Q3Policy.Parts, qc.Profile));
    }
    internal static string Objective(QuestController qc)
    {
        var q = Quest(qc);
        if (q == null) return "";
        return Q3Policy.Objective(Status(qc), Plugin.Found(q, Q3Policy.Parts, qc.Profile), Plugin.Found(q, Q3Policy.Books, qc.Profile),
            VisitFlow.Held(qc, Q3Policy.Parts), VisitFlow.Held(qc, Q3Policy.Books), VisitFlow.Given(q, Q3Policy.Parts), VisitFlow.Given(q, Q3Policy.Books));
    }
}
