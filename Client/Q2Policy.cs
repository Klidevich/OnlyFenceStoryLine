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
    internal static readonly Dictionary<string, string> Facts = new()
    {
        ["e17_q2_ready"] = "q2Ready", ["e17_q2_instructions"] = "q2Brief",
        ["e17_q2_wait"] = "q2Wait", ["e17_q2_loss"] = "q2Replacement",
        ["e17_q2_report"] = "q2Reported", ["e17_q2_result"] = "q2Result",
        ["e17_q2_cargo"] = "q2Cargo", ["e17_q2_friend"] = "q2Friend",
        ["e17_q2_remind"] = "q2RouteReminder", ["e17_q2_remind_reported"] = "q2ReportedReminder"
    };

    internal static bool Active(int status) => status is 2 or 3;
    internal static string Entry(int status, bool placed, Func<string, bool> has)
    {
        if (status == 4)
            return !has("q2Result") ? "e17_q2_result" : !has("q2Cargo") ? "e17_q2_result_resume" : "e17_q2_after";
        if (Active(status)) return placed ? "e17_q2_placed" : "e17_q2_active";
        return has("q2Brief") ? "e17_q2_offer" : has("q2Ready") ? "e17_q2_resume" : "e17_q2_ready";
    }

    internal static bool Allowed(string target, int status, bool found, bool placed, bool held, Func<string, bool> has,
        ParcelAvailability parcel = ParcelAvailability.Unknown)
    {
        switch (target)
        {
            case "e17_q2_ready": return status is 0 or 1 && !has("q2Ready");
            case "e17_q2_instructions": return status is 0 or 1 && !has("q2Brief");
            case "e17_q2_accepted": return status == 1 && has("q2Brief");
            case "e17_q2_report": return Active(status) && found && !placed && !has("q2Reported");
            case "e17_q2_friend": return status != 4 && !found && !placed && has("q2Ready") && !has("q2Friend");
            case "e17_q2_remind": return Active(status) && !placed && !has("q2Reported");
            case "e17_q2_remind_reported": return Active(status) && !placed && has("q2Reported");
            case "e17_q2_replace": return status == 2 && !placed && !held && has("q2Received") && parcel == ParcelAvailability.Missing;
            case "e17_q2_missing": return status == 2 && !placed && !held && !has("q2Received") && parcel == ParcelAvailability.Missing;
            case "e17_q2_pending": return Active(status) && !placed;
            case "e17_q2_wait": case "e17_q2_loss": return status != 4 && !placed && has("q2Brief");
            case "e17_q2_result": return status is 3 or 4 && placed;
            case "e17_q2_cargo": return status == 4 && has("q2Result");
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
        return receipt + E17Text.Get("td8fc3f24510c323b");
    }

    internal static string Journal(int status, bool placed, bool held, Func<string, bool> has)
    {
        var p = new List<string>();
        if (Active(status) || status == 4)
            p.Add(E17Text.Get("t434a01b428dafef5"));
        else if (has("q2Ready"))
            p.Add(E17Text.Get("t4d02e4f97e01c7b5"));
        if (has("q2Brief"))
            p.Add(E17Text.Get("t460177262ee2aa55"));
        if (!placed && held && has("q2Received")) p.Add(E17Text.Get(has("q2ReplacementReceived") ? "t5673e0516e3e60d4" : "t0eafa3245edae52d"));
        if (has("q2Friend")) p.Add(E17Text.Get("t4764de4522598daf"));
        if (has("q2Wait")) p.Add(E17Text.Get("t0ab1529fe74e1dec"));
        if (has("q2Replacement")) p.Add(E17Text.Get("t5be10f7f5778fd55"));
        if (placed) p.Add(E17Text.Get("tfb4038b71f6eba41"));
        if (status == 4)
        {
            if (has("q2Result"))
            {
                // The former uncertainty is superseded only by the actually shown confirmation.
                if (placed) p.RemoveAt(p.Count - 1);
                p.Add(E17Text.Get("t7eeb6008ac61a6f0"));
            }
            if (has("q2Cargo")) p.Add(E17Text.Get("tc3ad27792471f4b7"));
            p.Add(E17Text.Get("t9e10d708827473ec"));
        }
        return p.Count == 0 ? "" : E17Text.Get("tb34d5a3647c1eb05") + string.Join("\n\n", p);
    }
}
