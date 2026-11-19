using System;
using System.Collections.Generic;

namespace CustomQuests.E17;

internal enum ParcelAvailability { Unknown, Owned, PendingMail, Missing, Unavailable }

// Language-independent progress. Presentation facts are recorded only after an answer was painted.
internal static class Q2Policy
{
    internal const string QuestId = "b1e561de6727b0871dba91e9";
    internal const string Note = "90eb63a906ed4dff2fbbf0d8";
    internal const string Parcel = "96a7ba6b4bc23cb1d7fc6717";
    internal const string Placement = "de0d30cb048f4fe1178b5bcd";
    internal const string NoteReadFact = "q2NoteReadV022";
    internal static readonly Dictionary<string, string> Facts = new()
    {
        ["e17_q2_ready"] = "q2ReadyV018", ["e17_q2_instructions"] = "q2BriefV018",
        ["e17_q2_wait"] = "q2Wait", ["e17_q2_loss"] = "q2Replacement",
        ["e17_q2_report"] = "q2Reported", ["e17_q2_result"] = "q2ResultV018",
        ["e17_q2_cargo"] = "q2CargoV018",
        ["e17_q2_workshop"] = "q2ArrivalV022",
        ["e17_q2_remind"] = "q2RouteReminder", ["e17_q2_remind_reported"] = "q2ReportedReminder"
    };

    internal static bool Ready(Func<string, bool> has) => has("q2ReadyV018") && has("q2BriefV018");
    internal static bool Active(int status) => status is 2 or 3;
    internal static bool CanReadNote(bool found, Func<string, bool> has) => found && !has(NoteReadFact);
    internal static bool ContextAllows(string target, int status, bool found, Func<string, bool> has) => target switch
    {
        "e17_workshop" => status == 0 && !found && !has("workshop") && !has("q2ArrivalV022"),
        "e17_q2_workshop" => status is 1 or 2 or 3 && !found && !has("workshop") && !has("q2ArrivalV022"),
        "e17_cargo" => !has("cargo") && !has("q2CargoV018"),
        "e17_q2_archive_workshop" => has("workshop"),
        "e17_q2_archive_arrival" => has("q2ArrivalV022"),
        "e17_q2_archive_cargo" => has("cargo"),
        _ => true
    };
    internal static bool ReadyReport(int status, bool found, bool placed, Func<string, bool> has) =>
        Active(status) && found && !placed && !has("q2Reported");
    internal static string Entry(int status, bool placed, Func<string, bool> has)
    {
        if (status == 4)
            return !has("q2ResultV018") ? "e17_q2_result" : !has("q2CargoV018") ? "e17_q2_result_resume" : "e17_q2_after";
        if (Active(status)) return placed ? "e17_q2_placed" : "e17_q2_active";
        return Ready(has) ? "e17_q2_offer" : has("q2ReadyV018") ? "e17_q2_instructions" : "e17_q2_ready";
    }

    internal static bool Allowed(string target, int status, bool found, bool placed, bool held, Func<string, bool> has,
        ParcelAvailability parcel = ParcelAvailability.Unknown)
    {
        if (!ContextAllows(target, status, found, has)) return false;
        switch (target)
        {
            case "e17_q2_ready": return status is 0 or 1 && !has("q2ReadyV018");
            case "e17_q2_instructions": return status is 0 or 1 && has("q2ReadyV018") && !has("q2BriefV018");
            case "e17_q2_accepted": return status == 1 && Ready(has);
            case "e17_q2_report": return ReadyReport(status, found, placed, has);
            case "e17_q2_friend": return false;
            case "e17_q2_remind": return Active(status) && !placed && !has("q2Reported");
            case "e17_q2_remind_reported": return Active(status) && !placed && has("q2Reported");
            case "e17_q2_replace": return status == 2 && !placed && !held && has("q2Received") && parcel == ParcelAvailability.Missing;
            case "e17_q2_replace_confirm": return status == 2 && !placed && !held && has("q2Received") && parcel == ParcelAvailability.Missing;
            case "e17_q2_recover_confirm": return status == 2 && !placed && !held && !has("q2Received") && parcel == ParcelAvailability.Missing;
            case "e17_q2_missing": return status == 2 && !placed && !held && !has("q2Received") && parcel == ParcelAvailability.Missing;
            case "e17_q2_pending": return Active(status) && !placed;
            case "e17_q2_wait": return false;
            case "e17_q2_loss": return status != 4 && !placed && (has("q2ReadyV018") || Ready(has));
            case "e17_q2_result": return status is 3 or 4 && placed;
            case "e17_q2_cargo": case "e17_q2_result_resume": return status == 4 && has("q2ResultV018") && !has("q2CargoV018");
            default: return true;
        }
    }

    internal static string Objective(int status, bool found, bool placed, bool held,
        bool received = false, ParcelAvailability parcel = ParcelAvailability.Unknown, bool inQuestStash = false)
    {
        if (!Active(status)) return "";
        if (placed) return E17Text.Get("td2f1286a353709e8");
        var receipt = held ? (inQuestStash ? E17Text.Get("t75e19a94dacd4aaf") + "\n" : "")
            : parcel == ParcelAvailability.PendingMail || !received && parcel != ParcelAvailability.Missing
            ? E17Text.Get("td1c233b18e43774e") + "\n"
            : parcel == ParcelAvailability.Missing && !received
                ? E17Text.Get("tb5bb200262290b86") + "\n"
                : E17Text.Get("ta57d3a6d7a9b4438") + "\n";
        if (found) return receipt +
            E17Text.Get("t4795bd6d5f8296b6") + "\n" +
            E17Text.Get("tc3c80380342a80a7");
        return receipt + E17Text.Get("td8fc3f24510c323b") + "\n\n" + E17Text.Get("t0270000000000202");
    }

    internal static string Journal(int status, bool placed, bool held, Func<string, bool> has)
    {
        var p = new List<string>();
        if (Active(status) || status == 4) p.Add(E17Text.Get("t434a01b428dafef5"));
        else if (has("q2BriefV018")) p.Add(E17Text.Get("t460177262ee2aa55"));
        else if (has("q2ReadyV018")) p.Add(E17Text.Get("t4d02e4f97e01c7b5"));
        if (has("q2ArrivalV022")) p.Add(E17Text.Get("t0220000000000202"));
        if (has("q2Received")) p.Add(E17Text.Get(has("q2ReplacementReceived") ? "t5673e0516e3e60d4" :
            held && !placed ? "t0eafa3245edae52d" : "t0220000000000203"));
        if (has("q2Replacement")) p.Add(E17Text.Get("t5be10f7f5778fd55"));
        if (status == 4)
        {
            p.Add(E17Text.Get("t9e10d708827473ec"));
            if (has("q2ResultV018")) p.Add(E17Text.Get("tc3ad27792471f4b7"));
        }
        else if (placed) p.Add(E17Text.Get("tfb4038b71f6eba41"));
        return p.Count == 0 ? "" : E17Text.Get("tb34d5a3647c1eb05") + string.Join("\n\n", p);
    }
}
