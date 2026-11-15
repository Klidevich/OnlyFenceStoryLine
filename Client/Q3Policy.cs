using System;
using System.Collections.Generic;

namespace CustomQuests.E17;

// Native quest state records actions; narrative facts record only displayed answers.
internal static class Q3Policy
{
    internal const string QuestId = "b700d4655d89afabc28d305e";
    internal const string Parts = "0c3a4c5190db312c73d4fbba";
    internal const string Books = "e0e3f1bcb6345d8e8434e53c";
    internal static readonly Dictionary<string, string> Facts = new()
    {
        ["e17_q3_ready"] = "q3Rudin",
        ["e17_q3_instructions"] = "q3Brief", ["e17_q3_condition"] = "q3Condition",
        ["e17_q3_parts_first"] = "q3PartsAcknowledged", ["e17_q3_parts_second"] = "q3PartsAcknowledged",
        ["e17_q3_books_first"] = "q3BooksAcknowledged", ["e17_q3_books_second"] = "q3BooksAcknowledged",
        ["e17_q3_paid"] = "q3PaidShown", ["e17_q3_payment"] = "q3Payment",
        ["e17_q3_ransom"] = "q3Ransom", ["e17_q3_expenses"] = "q3Expenses"
    };
    internal static bool Active(int status) => status is 2 or 3;
    internal static bool Available(int previousStatus, Func<string, bool> has) =>
        previousStatus == 4 && has("q2Result");
    internal static bool ContextAllows(string target, bool booksFound, bool booksGiven, int status, bool begun, bool available)
    {
        // Previously unanswered topics can become obsolete through actual quest progress.
        if (target is "e17_books" or "e17_q3_condition") return !booksFound && !booksGiven && status != 4;
        if (target is "e17_q2_entry" or "e17_q2_continue") return !begun || !available;
        return true;
    }
    internal static string HandoverTarget(bool parts, bool otherGiven) =>
        "e17_q3_" + (parts ? "parts_" : "books_") + (otherGiven ? "second" : "first");
    internal static string ActiveEntry(bool partsGiven, bool booksGiven) =>
        "e17_q3_active_" + (partsGiven ? booksGiven ? "both" : "parts" : booksGiven ? "books" : "none");

    internal static string Entry(int status, bool partsGiven, bool booksGiven, Func<string, bool> has)
    {
        if (status == 4)
            return !has("q3PaidShown") ? "e17_q3_paid" : !has("q3Ransom") && !has("q3Payment") ? "e17_q3_paid_resume" :
                !has("q3Ransom") ? "e17_q3_payment_resume" : !has("q3Expenses") ? "e17_q3_ransom_resume" : "e17_q3_after";
        if (Active(status))
        {
            if (partsGiven && !has("q3PartsAcknowledged")) return HandoverTarget(true, booksGiven);
            if (booksGiven && !has("q3BooksAcknowledged")) return HandoverTarget(false, partsGiven);
            return ActiveEntry(partsGiven, booksGiven);
        }
        return has("q3Brief") ? "e17_q3_offer" : has("q3Rudin") ? "e17_q3_resume" : "e17_q3_ready";
    }

    internal static bool Allowed(string target, int status, bool partsGiven, bool booksGiven,
        bool partsHeld, bool booksHeld, Func<string, bool> has)
    {
        if (target == null) return true;
        if (Facts.TryGetValue(target, out var fact) && has(fact)) return false;
        switch (target)
        {
            case "e17_q3_ready": return status is 0 or 1;
            case "e17_q3_instructions": return status is 0 or 1 && has("q3Rudin");
            case "e17_q3_accepted": return status == 1 && has("q3Brief");
            // Legacy joke facts remain stored, but never count as the new condition answer.
            case "e17_q3_name":
            case "e17_q3_weight": return false;
            case "e17_q3_condition": return status is 0 or 1 or 2 && !booksGiven && has("q3Brief");
            case "e17_q3_paid": return status == 3 && partsGiven && booksGiven;
            // The old acknowledgement remains a stored fact; the new dialogue goes straight to the ransom answer.
            case "e17_q3_payment": return false;
            case "e17_q3_ransom": return status == 4 && has("q3PaidShown");
            case "e17_q3_expenses": return status == 4 && has("q3Ransom");
        }
        if (target.StartsWith("e17_q3_active_", StringComparison.Ordinal))
            return Active(status) && target == ActiveEntry(partsGiven, booksGiven);
        if (target.StartsWith("e17_q3_parts_", StringComparison.Ordinal))
            return status == 2 && !partsGiven && partsHeld && target == HandoverTarget(true, booksGiven);
        if (target.StartsWith("e17_q3_books_", StringComparison.Ordinal))
            return status == 2 && !booksGiven && booksHeld && target == HandoverTarget(false, partsGiven);
        return true;
    }

    internal static bool CanRecord(string node, int status, bool partsGiven, bool booksGiven, Func<string, bool> has)
    {
        if (!Facts.TryGetValue(node, out var fact) || has(fact)) return false;
        if (fact == "q3PartsAcknowledged") return partsGiven;
        if (fact == "q3BooksAcknowledged") return booksGiven;
        if (fact == "q3PaidShown") return status == 4;
        if (fact == "q3Payment") return false;
        if (fact == "q3Ransom") return status == 4 && has("q3PaidShown");
        if (fact == "q3Expenses") return status == 4 && has("q3Ransom");
        return true;
    }

    internal static string Journal(int status, bool partsGiven, bool booksGiven, Func<string, bool> has, bool partsFound = false)
    {
        var p = new List<string>();
        if (has("q3Rudin")) p.Add(E17Text.Get("t0038ac873734c899"));
        if (has("q3Brief")) p.Add(E17Text.Get("t99608e57497999cf") + "\n\n" +
            (Active(status) || status == 4 ? E17Text.Get("tcbcbfbd3809b1c0c") : E17Text.Get("t38d9758932b13476")));
        if (has("q3Condition")) p.Add(E17Text.Get("t6bcce1a05ba04cdc"));
        if (partsFound && !partsGiven) p.Add(E17Text.Get("td44bcd4bbd9eb317"));
        if (partsGiven && booksGiven) p.Add(status == 4 ? E17Text.Get("tdaecade9c9229275") : E17Text.Get("t16e8e4aa2c4a0b8d"));
        else if (partsGiven) p.Add(E17Text.Get("t6b3aeea1bd47c0b5"));
        else if (booksGiven) p.Add(E17Text.Get("tadd47f0ede26c2f4"));
        if (status == 4)
        {
            p.Add(E17Text.Get("ted0fe3b82e61ccf8"));
            if (has("q3Payment")) p.Add(E17Text.Get("t6e373120cb216714"));
            if (has("q3Expenses")) p.Add(E17Text.Get("tfc0dbd52a42a5dd6"));
            else if (has("q3Ransom")) p.Add(E17Text.Get("tcdf1692302fcdfb1"));
        }
        return p.Count == 0 ? "" : E17Text.Get("t41a517bb24bce4ad") + string.Join("\n\n", p);
    }

    internal static string Objective(int status, bool partsFound, bool booksFound, bool partsHeld, bool booksHeld, bool partsGiven, bool booksGiven)
    {
        if (!Active(status)) return "";
        if (partsGiven && booksGiven) return E17Text.Get("t12eeff4e8e022021");
        if (!partsFound && !booksFound && !partsGiven && !booksGiven) return E17Text.Get("td9ab239329ff7499");
        var p = new List<string>();
        if (!partsGiven) p.Add(partsHeld ? E17Text.Get("t40dabf48ebff4d4f") : partsFound ? E17Text.Get("t42fa689231d33230") : E17Text.Get("te9235938df63bf1b"));
        if (!booksGiven) p.Add(booksHeld ? E17Text.Get("t97c09d2481150c54") : booksFound ? E17Text.Get("t511029703d0e24d5") : E17Text.Get("t1056d367ee280344"));
        return string.Join("\n", p);
    }
}
