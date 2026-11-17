using System;
using System.Collections.Generic;
using System.Linq;

namespace CustomQuests.E17;

// Pure presentation rules shared by the runtime and the offline scenario checks.
internal static class NarrativePolicy
{
    internal const string Ticket = "582c4d5c43fdd55ee98b20bc";
    internal const string Radio = "e208363938a46fbc1507af21";
    internal const string Banner = "/files/quest/icon/customquests_e17_records.png";
    internal static readonly Dictionary<string, string> Facts = new()
    {
        ["e17_rescue"] = "rescue", ["e17_closed"] = "closed",
        ["e17_act"] = "booksKnown",
        ["e17_company"] = "company", ["e17_delivery"] = "dispatched",
        ["e17_van"] = "route", ["e17_ours"] = "introOursV018", ["e17_archive"] = "archive",
        ["e17_q1_separate"] = "q1Separate", ["e17_q1_person"] = "q1Person",
        ["e17_books"] = "uncertain", ["e17_ticket_unknown"] = "ticketFirst",
        ["e17_ticket_held"] = "ticketFirst", ["e17_ticket_lost"] = "ticketFirst",
        ["e17_ticket_second"] = "ticketSecond", ["e17_radio_first"] = "radioFirst",
        ["e17_radio_second"] = "radioSecond", ["e17_workshop"] = "workshop",
        ["e17_cargo"] = "cargo", ["e17_route"] = "q1RouteReminder", ["e17_done_new"] = "finished", ["e17_done_ready"] = "finished"
    };

    internal static bool Preparing(Func<string, bool> has) => has("radioFirst") || has("radioSecond") || has("finished");
    internal static IEnumerable<string> JournalFacts => Facts.Values.Concat(new[] { "books", "bill", "actExplained", "coordinatorClaim", "motive", "briefed", "intro2_motive", "intro3_rescue", "intro3_delivery", "intro3_van", "intro4_closed", "intro4_company", "q1RadioVoice", "q2Brief", "q1RadioV018" });
    internal static string HandoverTarget(bool ticket, bool otherGiven, bool radioFound, bool radioHeld) =>
        !ticket ? (otherGiven ? "e17_radio_second" : "e17_radio_first") : otherGiven ? "e17_ticket_second" :
        radioHeld ? "e17_ticket_held" : radioFound ? "e17_ticket_lost" : "e17_ticket_unknown";

    internal static string Conversation(Func<string, bool> has, bool accepted)
    {
        var paragraphs = new List<string>();
        var recovery = new List<string>();
        if (Q1FlowPolicy.Seen("rescue", has)) recovery.Add(E17Text.Get("t2346974f596e87b5"));
        else
        {
            if (has("rescue")) recovery.Add(E17Text.Get("t5409e5d0c466a1bf"));
            if (has("bill")) recovery.Add(E17Text.Get("t2ae1ed88e9483d6e"));
        }
        if (has("closed") || has("intro4_closed")) recovery.Add(E17Text.Get("t8e41f8627e603ced"));
        if (recovery.Count > 0) paragraphs.Add(string.Join(" ", recovery));
        if (has("company") || has("intro4_company")) paragraphs.Add(E17Text.Get("tc3482faa8609afed"));
        var books = Q1FlowPolicy.BooksKnown(has);
        var dispatched = has("intro4_company") || has("intro3_delivery") || has("dispatched");
        var route = has("route") || Q1FlowPolicy.Seen("van", has);
        if (accepted && books && dispatched && route)
            paragraphs.Add(E17Text.Get("t0100000000000001") + (has("uncertain") ? E17Text.Get("t966f08e9470ff929") : ""));
        else
        {
            if (books) paragraphs.Add(E17Text.Get("t605c83e11332f9c1") + (has("uncertain") ? E17Text.Get("t966f08e9470ff929") : ""));
            if (dispatched) paragraphs.Add(E17Text.Get("tf1c5f07f6e7f453b"));
            if (route) paragraphs.Add(E17Text.Get("t60cb3cd46158a876"));
            if (accepted) paragraphs.Add(E17Text.Get("t6991fee75f5cef9e"));
        }
        if (has("introOursV018")) paragraphs.Add(E17Text.Get("t71131ed4f5f2397b"));
        if (has("archive")) paragraphs.Add(E17Text.Get("te2305f3ddf21e6ed"));
        if (has("q1Separate")) paragraphs.Add(E17Text.Get("t0180000000000101"));
        if (has("q1Person")) paragraphs.Add(E17Text.Get("t0180000000000102"));
        return paragraphs.Count == 0 ? "" : E17Text.Get("t34a3854c443907c0") + string.Join("\n\n", paragraphs);
    }

    internal static string Debrief(Func<string, bool> has, bool ticketGiven, bool radioGiven, bool success)
    {
        var p = new List<string>();
        if (ticketGiven && !has("ticketSecond")) p.Add(E17Text.Get("t1c23b5e1c1fd9b5d"));
        if (radioGiven)
        {
            p.Add(E17Text.Get("t269800a86560bac7"));
            if (has("q1RadioV018")) p.Add(E17Text.Get("ta8a2bd7785b564f7"));
        }
        if (has("workshop")) p.Add(E17Text.Get("t25cf33128b49e86d"));
        if (has("cargo")) p.Add(E17Text.Get("tf49f1e8f9787b0fc"));
        if (success)
        {
            p.Add(E17Text.Get("t72a9e2bafe441072"));
            if (has("finished")) p.Add(E17Text.Get("ta93ad8d45b34c797"));
        }
        else if (ticketGiven && radioGiven) p.Add(E17Text.Get("tcab12f6ddbf52c17"));
        return p.Count == 0 ? "" : E17Text.Get("t2c16fa82a025bc2a") + string.Join("\n\n", p);
    }

    internal static string Objective(bool active, bool ticketFound, bool radioFound, bool ticketHeld, bool radioHeld, bool ticketGiven, bool radioGiven)
    {
        if (!active) return "";
        if (ticketGiven && radioGiven) return E17Text.Get("t410a42627b587d5a");
        var missing = new List<string>();
        if (ticketFound && !ticketHeld && !ticketGiven) missing.Add(E17Text.Get("t5362e47a115c99fc"));
        if (radioFound && !radioHeld && !radioGiven) missing.Add(E17Text.Get("t2d0ce96a2b8c412b"));
        var prefix = missing.Count == 0 ? "" : E17Text.Get("t37db995e19e327e0") + string.Join("; ", missing) + ".\n";
        if (!ticketFound && !ticketGiven) return prefix + E17Text.Get("t3a351674e94bde3c");
        if (!radioFound && !radioGiven) return prefix + E17Text.Get("t615f108daec2d3c7");
        if (missing.Count > 0) return prefix.TrimEnd();
        return ticketGiven ? E17Text.Get("tf90d8adb5d044e8f") : radioGiven ? E17Text.Get("t5c59e7dc5c6bb4f4") : E17Text.Get("t184a4001b925f4e1");
    }
}
