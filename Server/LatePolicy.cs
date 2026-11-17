using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CustomQuests.E17.Server;

/// <summary>Only E17's durable decisions. Native quests remain authoritative for physical progress.</summary>
public sealed class LateLedger
{
    public int Version { get; set; } = 1;
    public long Revision { get; set; }
    public string Choice7 { get; set; } = "unknown";
    public string Keeper { get; set; } = "unknown";
    public bool SettlementPaid { get; set; }
    public string SettlementIntent { get; set; } = "unknown";
    public long RaidSequence { get; set; }
    public string ActiveRaidId { get; set; } = "";
    public HashSet<string> EndedRaids { get; set; } = new(StringComparer.Ordinal);
    public HashSet<string> Flags { get; set; } = new(StringComparer.Ordinal);
    public HashSet<string> MirroredFlags { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, long> Dispatches { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Receipts { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, LateOperation> Operations { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, LateCustody> Custody { get; set; } = new(StringComparer.Ordinal)
    { ["family"] = new(), ["reserve"] = new() };
    public LateReservation Reservation { get; set; } = new();
    public Dictionary<string, HashSet<string>> IssuedItems { get; set; } = new(StringComparer.Ordinal);
    public HashSet<string> RevokedItems { get; set; } = new(StringComparer.Ordinal);
}

public sealed class LateCustody
{
    public int Generation { get; set; } = 1;
    public string Status { get; set; } = "Missing";
    public long SentRaid { get; set; } = -1;
    public bool ReturnReady { get; set; }
}

public sealed class LateReservation
{
    public int Generation { get; set; }
    public string Status { get; set; } = "None";
    public long Amount { get; set; }
    public long IssuedAfterRaid { get; set; }
    public string OperationId { get; set; } = "";
    public string RefundOperationId { get; set; } = "";
    public bool FinalDeposited { get; set; }
}

public sealed record LateOperation(string Fingerprint, string Code, JsonElement? InventoryEffect = null);
public sealed record LateCommand(string OperationId, string Action, string Subject = "", string? Choice = null, int? ExpectedGeneration = null)
{
    public string Fingerprint => JsonSerializer.Serialize(new { Action, Subject, Choice, ExpectedGeneration });
}
public sealed class LateEffects
{
    public long Debit { get; set; }
    public long Refund { get; set; }
    public string? Issue { get; set; }
    public string? Consume { get; set; }
    public bool RevokeAuthorization { get; set; }
    public List<string> RevokeParcels { get; } = [];
}
public sealed record LateDecision(bool Ok, string Code, LateEffects Effects);

public interface ILateNativeState
{
    int Status(string quest);
    bool Active(string quest);
    bool Success(string quest);
    bool Done(string condition);
    bool Fact(string fact);
    bool Held(string item);
    string Parcel(string item);
    long Roubles { get; }
}

public static class LateIds
{
    public static string Id(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("CustomQuests/E17/v1/" + key)))[..24].ToLowerInvariant();
    public static string Item(string quest, string name) => quest + "/item/" + name;
    public const string Rouble = "5449016a4bdc2d6f028b456f";
    public const long Ransom = 8_000_000;
}

public static class LatePolicy
{
    public static readonly string[] Parcels = ["q11/item/severReplyEnvelope", "q18/item/familyClosedCopy", "q18/item/reserveClosedCopy", "q20/item/finalAuthorization", "q22/item/antonFamilyPacket"];
    static readonly Dictionary<string, (string Quest, string[] Conditions)> Dispatch = new(StringComparer.Ordinal)
    {
        ["q8.sazonovRequest"] = ("q8", ["q8/give/contractorApplications"]),
        ["q10.familyMessage"] = ("q10", ["q10/give/medicalCorrespondence"]),
        ["q14.therapistReply"] = ("q14", ["q14/give/reserveBook"]),
        ["q14.sazonovRequest"] = ("q14", ["q14/give/reserveBook"]),
        ["q16.familyQuestions"] = ("q16", []),
        ["q17.praporRecommendation"] = ("q17", ["q17/place/transportStretcher"]),
        ["q18.cityDriverInquiry"] = ("q18", []),
        ["q19.kitSpecification"] = ("q19", ["q19/give/ransomTerms", "q19/give/transportCheck"])
    };
    public static LateLedger Copy(LateLedger value) => JsonSerializer.Deserialize<LateLedger>(JsonSerializer.Serialize(value))!;
    public static string CopyItem(string recipient) => "q18/item/" + (recipient == "family" ? "familyClosedCopy" : "reserveClosedCopy");
    public static string CopyProof(string recipient, int generation) => "q18/redeposit/" + recipient + "/" + generation;
    public static string AuthorizationProof(int generation) => "q20/authorization/" + generation;
    public static bool BothStored(LateLedger s) => s.Custody.Values.All(x => x.Status == "Stored");
    public static bool Locked(LateLedger s) => s.Reservation.FinalDeposited;

    public static LateDecision Apply(LateLedger s, ILateNativeState n, LateCommand c)
    {
        var fx = new LateEffects();
        LateDecision Fail(string code) => new(false, code, fx);
        if (string.IsNullOrWhiteSpace(c.OperationId) || c.OperationId.Length > 100) return Fail("InvalidOperationId");
        if (s.Version != 1) return Fail("UnsupportedLedgerVersion");
        if (s.Operations.TryGetValue(c.OperationId, out var prior))
            return prior.Fingerprint == c.Fingerprint ? new(true, "Replayed", fx) : Fail("OperationConflict");
        if (!string.IsNullOrEmpty(s.ActiveRaidId)) return Fail("LobbyRequired");
        bool During(string q) => n.Active(q) || n.Success(q);
        bool Conditions(params string[] keys) => keys.All(n.Done);
        switch (c.Action)
        {
            case "CommitQ7Settlement":
                if (c.Subject != "q7" || c.Choice is not ("cash" or "tools")) return Fail("InvalidAction");
                if (!n.Active("q7") || !Conditions("q7/give/transportReturnLog", "q7/give/rudinTools", "q7/find/transportRefusal")) return Fail("NotReady");
                if (s.Choice7 != "unknown" && s.Choice7 != c.Choice) return Fail("AlreadyChosen");
                // This is only an intent. Final choice and cash entitlement are committed by native QuestComplete.
                s.SettlementIntent = c.Choice;
                break;
            case "ChooseKeeper":
                if (c.Subject != "q14" || c.Choice is not ("Fence" or "Ragman")) return Fail("InvalidAction");
                if (!During("q14") || !n.Done("q14/give/reserveBook") || !n.Fact("late_q14_boundaries")) return Fail("NotReady");
                if (s.Keeper != "unknown" && s.Keeper != c.Choice) return Fail("AlreadyChosen");
                s.Keeper = c.Choice;
                break;
            case "DispatchMessage":
                if (!Dispatch.TryGetValue(c.Subject, out var d)) return Fail("InvalidAction");
                if (!During(d.Quest) || !d.Conditions.All(n.Done)) return Fail("NotReady");
                if (d.Quest == "q14" && s.Keeper == "unknown") return Fail("NotReady");
                if (d.Quest == "q17" && !s.Flags.Contains("q17.deliveryConfirmed")) return Fail("NotReady");
                if (d.Quest == "q18" && !n.Success("q15")) return Fail("NotReady");
                if (d.Quest == "q19" && !n.Fact("late_q19_together")) return Fail("NotReady");
                s.Dispatches.TryAdd(c.Subject, s.RaidSequence);
                s.Flags.Add(c.Subject + ".sent");
                break;
            case "BeginClosedStorage":
                if (c.Subject != "q18" || !During("q18") || s.Keeper == "unknown" ||
                    !Conditions("q18/give/nutritionKit", "q18/give/threePersonReception") ||
                    !s.Flags.Contains("q18.suppliesReceived") || !s.Flags.Contains("q18.cityTransportConfirmed")) return Fail("NotReady");
                if (Locked(s)) return Fail("Irreversible");
                s.Flags.Add("q18.storageEnabled");
                break;
            case "IssueParcel":
                if (!Parcels.Contains(c.Subject)) return Fail("InvalidAction");
                if (!CanIssue(s, n, c.Subject)) return Fail("NotReady");
                if (n.Parcel(c.Subject) == "Missing") fx.Issue = c.Subject;
                if (c.Subject.StartsWith("q11/", StringComparison.Ordinal)) s.Flags.Add("q11.deliveryAccepted");
                break;
            case "RequestRecall":
                if (!s.Custody.TryGetValue(c.Subject, out var recall)) return Fail("InvalidAction");
                if (Locked(s)) return Fail("Irreversible");
                if (c.ExpectedGeneration != recall.Generation) return Fail("StaleGeneration");
                if (recall.Status is not ("Stored" or "Sent" or "RecallPending")) return Fail("NotReady");
                recall.Status = "RecallPending";
                recall.ReturnReady = false;
                Cancel(s, c.OperationId, fx);
                break;
            case "ReceiveRecalledCopy":
                if (!s.Custody.TryGetValue(c.Subject, out var returned)) return Fail("InvalidAction");
                if (Locked(s)) return Fail("Irreversible");
                if (c.ExpectedGeneration != returned.Generation) return Fail("StaleGeneration");
                if (returned.Status != "RecallPending" || !returned.ReturnReady) return Fail("NotReady");
                returned.Status = "Returned";
                returned.Generation++;
                returned.ReturnReady = false;
                fx.RevokeParcels.Add(CopyItem(c.Subject));
                break;
            case "DeliverReserveCopy":
                if (c.Subject != "reserve" || !s.Flags.Contains("q18.storageEnabled") || s.Keeper == "unknown") return Fail("NotReady");
                if (!n.Done("q18/give/reserveClosedCopy")) return Fail("NativeHandoverRequired");
                var custody = s.Custody["reserve"];
                if (Locked(s)) return Fail("Irreversible");
                if (c.ExpectedGeneration != custody.Generation) return Fail("StaleGeneration");
                if (custody.Status is "RecallPending" or "Stored") return Fail("NotReady");
                if (!n.Held(CopyItem("reserve"))) return Fail("MissingItem");
                fx.Consume = CopyItem("reserve");
                custody.Status = "Stored";
                break;
            case "PrepareAuthorization":
                if (c.Subject != "q20" || !n.Active("q20") || !s.Flags.Contains("q20.repairConfirmed")) return Fail("NotReady");
                if (Locked(s)) return Fail("Irreversible");
                if (!BothStored(s)) return Fail("RecallPending");
                if (!n.Fact("late_q20_moneyConfirmed") || !n.Fact("late_q20_disclosureConfirmed") || !n.Fact("late_q20_refusalConfirmed")) return Fail("ConsentRequired");
                if (s.Reservation.Status == "Reserved") break;
                if (n.Roubles < LateIds.Ransom) return Fail("InsufficientFunds");
                s.Reservation.Generation++;
                s.Reservation.Status = "Reserved";
                s.Reservation.Amount = LateIds.Ransom;
                s.Reservation.OperationId = c.OperationId;
                s.Reservation.IssuedAfterRaid = s.RaidSequence;
                s.Reservation.RefundOperationId = "";
                fx.Debit = LateIds.Ransom;
                // Issuance is recoverable and separate; a crash after reserve cannot cause a second debit.
                break;
            case "CancelAuthorization":
                if (c.Subject != "q20") return Fail("InvalidAction");
                if (Locked(s)) return Fail("Irreversible");
                if (c.ExpectedGeneration != s.Reservation.Generation) return Fail("StaleGeneration");
                Cancel(s, c.OperationId, fx);
                break;
            default: return Fail("InvalidAction");
        }
        s.Revision++;
        s.Operations[c.OperationId] = new(c.Fingerprint, "Applied");
        return new(true, "Applied", fx);
    }

    static void Cancel(LateLedger s, string operation, LateEffects fx)
    {
        if (s.Reservation.Status != "Reserved") return;
        fx.Refund = s.Reservation.Amount;
        fx.RevokeAuthorization = true;
        s.Reservation.Status = "Refunded";
        s.Reservation.RefundOperationId = operation;
        s.Reservation.Amount = 0;
    }

    public static bool CanIssue(LateLedger s, ILateNativeState n, string key) => key switch
    {
        "q11/item/severReplyEnvelope" => n.Active("q11") && n.Done("q11/find/severLetter") && !n.Done("q11/place/severReplyEnvelope"),
        "q18/item/familyClosedCopy" => s.Flags.Contains("q18.storageEnabled") && !Locked(s) && s.Custody["family"].Status is "Missing" or "Returned",
        "q18/item/reserveClosedCopy" => s.Flags.Contains("q18.storageEnabled") && !Locked(s) && s.Custody["reserve"].Status is "Missing" or "Returned",
        "q20/item/finalAuthorization" => n.Active("q20") && s.Reservation.Status == "Reserved" && !Locked(s) && BothStored(s),
        "q22/item/antonFamilyPacket" => n.Active("q22") && !n.Done("q22/place/antonFamilyPacket"),
        _ => false
    };

    public static bool Reconcile(LateLedger s, ILateNativeState n, bool visit = false)
    {
        var before = JsonSerializer.Serialize(s);
        if (n.Success("q14")) s.Flags.Add("unlockTherapist");
        if (n.Success("q17")) s.Flags.Add("unlockPrapor");
        if (n.Success("q7") && s.Choice7 is "cash" or "tools") s.Flags.Add("q7.rudinReplyReady");
        // The first reserve transfer uses native handover; later generations use the repeat adapter.
        if (s.Flags.Contains("q18.storageEnabled") && s.Custody["reserve"].Generation == 1 &&
            s.Custody["reserve"].Status == "Missing" && n.Done("q18/give/reserveClosedCopy")) s.Custody["reserve"].Status = "Stored";
        if (visit)
        {
            foreach (var c in s.Custody.Values.Where(x => x.Status == "RecallPending")) c.ReturnReady = true;
            Receipt("q16.answersVerified", n.Done("q16/give/threeNameReplies"));
            var supplies = n.Done("q18/give/nutritionKit") && n.Done("q18/give/threePersonReception");
            Receipt("q18.suppliesReceived", supplies);
            Receipt("q18.cityTransportConfirmed", supplies);
            Receipt("q19.kitReservationConfirmed", s.Dispatches.ContainsKey("q19.kitSpecification"));
            var final = n.Success("q20") && s.Reservation.FinalDeposited && s.Flags.Contains("q20.linkReceived");
            if (final) s.Flags.Add("q21.documentsReady");
            Receipt("q21.offerCancelled", final);
            Receipt("q21.reserveDelivered", final && s.Keeper is "Fence" or "Ragman");
            Receipt("q21.familyDisclosed", final);
            Receipt("q21.cityDriverReplyReady", final && s.Dispatches.ContainsKey("q18.cityDriverInquiry"));
            Receipt("q21.praporReplyReady", final && s.Keeper is "Fence" or "Ragman");
        }
        if (n.Done("q21/find/severAcceptance") && s.Reservation.FinalDeposited) s.Flags.Add("q20.severAcceptanceReceived");
        if (s.Flags.Contains("q20.severAcceptanceReceived")) s.Reservation.Status = "Released";
        return before != JsonSerializer.Serialize(s);
        void Receipt(string key, bool dispatched)
        {
            if (!dispatched) return;
            if (!s.Receipts.TryAdd(key, "Pending")) { s.Receipts[key] = "Ready"; s.Flags.Add(key); }
        }
    }

    public static bool RecordRaid(LateLedger s, ILateNativeState n, string raidId)
    {
        if (string.IsNullOrWhiteSpace(raidId) || !s.EndedRaids.Add(raidId)) return false;
        s.RaidSequence++;
        s.ActiveRaidId = "";
        if (s.Dispatches.TryGetValue("q16.familyQuestions", out var sent) && s.RaidSequence > sent) s.Flags.Add("q16.answersReady");
        if (n.Done("q11/place/severReplyEnvelope")) s.Flags.Add("q11.replyDelivered");
        if (n.Done("q17/place/transportStretcher")) s.Flags.Add("q17.deliveryConfirmed");
        var family = s.Custody["family"];
        if (family.Status is "Missing" or "Returned" &&
            n.Done(family.Generation == 1 ? "q18/place/familyClosedCopy" : CopyProof("family", family.Generation)))
        { family.Status = "Stored"; family.SentRaid = s.RaidSequence; }
        if (n.Done("q20/place/startingKit")) s.Flags.Add("q20.repairConfirmed");
        var r = s.Reservation;
        // The per-generation proof is required even for the first final plant: a stale package cannot authorize a new reserve.
        if (r.Status == "Reserved" && !r.FinalDeposited && BothStored(s) && s.RaidSequence > r.IssuedAfterRaid &&
            n.Done(AuthorizationProof(r.Generation)) && n.Done("q20/place/finalAuthorization"))
        { r.FinalDeposited = true; r.Status = "Dispatched"; s.Flags.Add("q20.linkReceived"); }
        if (n.Done("q22/place/antonFamilyPacket")) s.Flags.Add("q22.replyReady");
        Reconcile(s, n);
        s.Revision++;
        return true;
    }

    public static HashSet<string> SnapshotFlags(LateLedger s, ILateNativeState n)
    {
        var flags = new HashSet<string>(s.Flags, StringComparer.Ordinal);
        foreach (var pair in s.Custody)
        {
            var prefix = "q18." + pair.Key;
            if (pair.Value.Status == "Stored") flags.Add(prefix + "Stored");
            if (pair.Value.Status == "RecallPending") flags.Add(prefix + "RecallPending");
            if (pair.Value.ReturnReady) flags.Add(prefix + "ReturnReady");
            if (pair.Value.Status == "Returned") flags.Add(prefix + "Returned");
            if (CanIssue(s, n, CopyItem(pair.Key)) && n.Parcel(CopyItem(pair.Key)) == "Missing") flags.Add(prefix + "Missing");
        }
        if (s.Flags.Contains("q18.storageEnabled") && s.Custody["family"].Generation > 1 && s.Custody["family"].Status == "Returned" && !Locked(s)) flags.Add("q18.familyCanRedeliver");
        if (s.SettlementIntent is "cash" or "tools" && n.Active("q7")) flags.Add("q7.settlementReady");
        if (s.Reservation.Status == "Reserved") flags.Add("q20.reserved");
        if (s.Reservation.Status == "Reserved" && !s.Reservation.FinalDeposited && BothStored(s) && s.Flags.Contains("q20.repairConfirmed")) flags.Add("q20.finalDeliveryAvailable");
        if (s.Reservation.FinalDeposited) flags.Add("q20.finalDeposited");
        if (CanIssue(s, n, "q11/item/severReplyEnvelope") && n.Parcel("q11/item/severReplyEnvelope") == "Missing") flags.Add("q11.parcelMissing");
        if (CanIssue(s, n, "q20/item/finalAuthorization") && n.Parcel("q20/item/finalAuthorization") == "Missing") flags.Add("q20.authorizationMissing");
        return flags;
    }
}
