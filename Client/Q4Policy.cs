using System;
using System.Collections.Generic;

namespace CustomQuests.E17;

internal sealed class Q4State
{
    internal int Status;
    internal bool ClaimFound, ClaimGiven, ClaimHeld, RequestHeld, InQuestStash, Placed, RepliesReady;
    internal bool BusinessFound, BusinessGiven, BusinessHeld, PersonalFound;
    internal ParcelAvailability Parcel;
}

internal static class Q4Policy
{
    internal const string QuestId = "8e3f7ba9a8877778bd0f9035";
    internal const string Claim = "47e6870020092ae0b8de45da";
    internal const string Request = "258aa01fe0c068cfd26744d1";
    internal const string BusinessReply = "d0be16e02b1411636c168b7f";
    internal const string PersonalCopy = "a47ffa9c56f5e8e37b76f82b";
    internal const string Placement = "59f3ea45693024d45ed3f928";
    internal static readonly Dictionary<string, string> Facts = new()
    {
        ["e17_q4_ready"] = "q4ReadyVoice", ["e17_q4_instructions"] = "q4Brief",
        ["e17_q4_wait"] = "q4Wait", ["e17_q4_claim"] = "q4ClaimAcknowledged",
        ["e17_q4_debt"] = "q4Debt", ["e17_q4_business"] = "q4BusinessAcknowledged",
        ["e17_q4_paid"] = "q4PaidShown"
    };
    internal static bool Active(int status) => status is 2 or 3;
    internal static bool Briefed(Func<string, bool> has) => has("q4ReadyVoice") || has("q4Brief");
    internal static bool Available(int previousStatus, Func<string, bool> has) => previousStatus == 4 && has("q3Expenses");
    internal static string Entry(Q4State s, Func<string, bool> has)
    {
        if (s.Status == 4) return has("q4PaidShown") ? "e17_q4_after" : "e17_q4_paid";
        if (Active(s.Status))
        {
            if (s.ClaimGiven && !has("q4ClaimAcknowledged")) return "e17_q4_claim";
            if (s.BusinessGiven && !has("q4BusinessAcknowledged")) return "e17_q4_business";
            return "e17_q4_active";
        }
        return Briefed(has) ? "e17_q4_offer" : has("q4Ready") ? "e17_q4_instructions" : "e17_q4_ready";
    }
    internal static bool Allowed(string target, Q4State s, Func<string, bool> has)
    {
        if (target == null) return true;
        if (Facts.TryGetValue(target, out var fact) && has(fact)) return false;
        switch (target)
        {
            case "e17_q4_ready": return s.Status is 0 or 1;
            case "e17_q4_instructions": return s.Status is 0 or 1 && has("q4Ready") && !Briefed(has);
            case "e17_q4_accepted": return s.Status == 1 && Briefed(has);
            case "e17_q4_wait": return s.Status != 4 && (has("q4Ready") || Briefed(has));
            case "e17_q4_claim": return s.Status == 2 && !s.ClaimGiven && s.ClaimHeld;
            case "e17_q4_debt": return Active(s.Status) && s.ClaimGiven;
            case "e17_q4_issue": return s.Status == 2 && s.ClaimGiven && !s.Placed && !s.RequestHeld &&
                !has("q4Received") && s.Parcel == ParcelAvailability.Missing;
            case "e17_q4_replace": return s.Status == 2 && s.ClaimGiven && !s.Placed && !s.RequestHeld &&
                has("q4Received") && s.Parcel == ParcelAvailability.Missing;
            case "e17_q4_business": return s.Status == 2 && s.RepliesReady && !s.BusinessGiven && s.BusinessHeld;
            case "e17_q4_paid": return s.Status == 3 && s.BusinessGiven && s.PersonalFound && s.Placed && s.RepliesReady;
            case "e17_q4_active": return Active(s.Status);
            default: return true;
        }
    }
    internal static bool CanRecord(string node, Q4State s, Func<string, bool> has)
    {
        if (!Facts.TryGetValue(node, out var fact) || has(fact)) return false;
        if (fact == "q4ClaimAcknowledged") return s.ClaimGiven;
        if (fact == "q4BusinessAcknowledged") return s.BusinessGiven;
        if (fact == "q4PaidShown") return s.Status == 4;
        return true;
    }
    internal static string Objective(Q4State s, Func<string, bool> has)
    {
        if (!Active(s.Status)) return "";
        if (!s.ClaimGiven) return s.ClaimHeld ? E17Text.Get("t77b3b8265a8bff73") : s.ClaimFound ? E17Text.Get("te4d4843c442e7c0b") : E17Text.Get("td3175bcf54ea27b9");
        if (!s.Placed)
        {
            if (s.RequestHeld) return (s.InQuestStash ? E17Text.Get("t996c44324ebde818") + "\n" : "") + E17Text.Get("t5f2dfe2a40ba9dcb");
            if (s.Parcel == ParcelAvailability.PendingMail) return E17Text.Get("t91e399a594fcb8a7");
            if (s.Parcel == ParcelAvailability.Missing) return has("q4Received") ? E17Text.Get("t8f33b1b2b412de14") : E17Text.Get("t9108227460a0c5b8");
            return E17Text.Get("td4ff757bacdc199b");
        }
        if (!s.RepliesReady) return E17Text.Get("t0d9e0d52f57c11d1");
        if (s.BusinessGiven && s.PersonalFound) return E17Text.Get("tc4921eb7909d2fad");
        var p = new List<string>();
        if (!s.BusinessGiven) p.Add(s.BusinessHeld ? E17Text.Get("t344364d38d0a2537") : s.BusinessFound ? E17Text.Get("t1afe6b9a0f7e4576") : s.PersonalFound ? E17Text.Get("ta774863680675e2e") : E17Text.Get("td42196451d5a9aae"));
        if (!s.PersonalFound && (s.BusinessFound || s.BusinessGiven)) p.Add(E17Text.Get("t68c5ea12c3181c74"));
        return string.Join("\n", p);
    }
    internal static string Journal(Q4State s, Func<string, bool> has)
    {
        var p = new List<string>();
        if (has("q4Ready") || Briefed(has)) p.Add(E17Text.Get("t3aa47d681cefaf4f"));
        if (has("q4Wait")) p.Add(E17Text.Get("t9221f4a8e3387980"));
        if (Active(s.Status) || s.Status == 4) p.Add(E17Text.Get("taa387c4a110f7918"));
        if (s.ClaimFound && !s.ClaimGiven) p.Add(E17Text.Get("t8608f0c18890ab4f"));
        if (s.ClaimGiven) p.Add(E17Text.Get("t2da20b03d904b874"));
        if (has("q4ClaimAcknowledged")) p.Add(E17Text.Get("t4c6742acb887f687"));
        if (has("q4Debt")) p.Add(E17Text.Get("t7331057ea863e678"));
        if (has("q4Received") || s.Placed) p.Add(E17Text.Get("t6825e8797d74aa8b") + "\n\n" + E17Text.Get("t9a2ead36b2b6d05b"));
        if (s.Placed) p.Add(E17Text.Get("t9795c5e2feb72b51"));
        if (s.BusinessFound && !s.BusinessGiven) p.Add(E17Text.Get("t7f525b9a9c24794d"));
        if ((s.BusinessFound || s.BusinessGiven) && !s.PersonalFound) p.Add(E17Text.Get("teee23fb91b5b214c"));
        if (s.PersonalFound) p.Add(E17Text.Get("tb3f6f0b202adf8c4"));
        if (s.BusinessGiven) p.Add(E17Text.Get("t2fe06fc1b41d6526"));
        if (s.Status == 4)
        {
            p.Add(E17Text.Get("t871ae1f875f42960"));
            if (has("q4PaidShown")) p.Add(E17Text.Get("ta4279fc6e2d075c1"));
        }
        return p.Count == 0 ? "" : E17Text.Get("t12bf508fc1ad29ee") + string.Join("\n\n", p);
    }
}
