using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using System.Text.Json;
using SPTarkov.Server.Core.Models.Eft.Match;

namespace CustomQuests.E17.Server;

public sealed class Q4Ledger
{
    public bool RequestRead { get; set; }
    public bool PersonalRead { get; set; }
    public bool CopyRead { get; set; }
    public bool PersonalIssued { get; set; }
    public string? AttachedRequest { get; set; }
    public string? AttachedLetter { get; set; }
    public bool Delivery { get; set; }
    public bool BusinessExtracted { get; set; }
    public bool PersonalExtracted { get; set; }
    public bool FailedAttempt { get; set; }
    public bool IncompleteAttempt { get; set; }
    public HashSet<string> EndedRaids { get; set; } = [];
    public Q4PendingRaid? PendingRaid { get; set; }
}

public sealed class Q4PendingRaid
{
    public string ServerId { get; set; } = "";
    public string ProfileId { get; set; } = "";
    public string Result { get; set; } = "";
    public string CommitFact { get; set; } = "";
    public string[] Carried { get; set; } = [];
}

public static class Q4Policy
{
    public const string QuestId = "8e3f7ba9a8877778bd0f9035";
    public const string TemplateId = "258aa01fe0c068cfd26744d1";
    public static readonly string PersonalRequest = LateIds.Id("personalRequest");
    public const string BusinessReply = "d0be16e02b1411636c168b7f";
    public const string PersonalCopy = "a47ffa9c56f5e8e37b76f82b";
    public const string ClaimGiveId = "2f7372f31d44cb8145b1bb94";
    public const string BusinessFindId = "b64d9234b8a9220a71a33221";
    public const string BusinessGiveId = "b1657b03b883293b2a617c2a";
    public const string PersonalFindId = "c3f9ee5012ae7371a962a3d8";
    public const string PlacementId = "59f3ea45693024d45ed3f928";
    public const string ReplyReadyFact = "q4ReplyReady";
    public const string MailText = "e17 request issue";
    public const string PersonalMailText = "e17 personal request issue";
    const string LedgerKey = "customquestsE17Q4V022";
    const string DurableReplyKey = "customquestsE17Q4ReplyReady";
    public enum Decision { MissingProfile, NotActive, AwaitingClaim, Placed, PendingMail, Owned, Issue }
    public static bool ConditionDone(SptProfile? profile, string condition) =>
        profile?.CharacterData?.PmcData is { } pmc &&
        (pmc.Quests?.Any(q => q.QId.ToString() == QuestId && q.CompletedConditions?.Contains(condition) == true) == true ||
         pmc.TaskConditionCounters?.Any(p => p.Key.ToString() == condition && p.Value.SourceId?.ToString() == QuestId && p.Value.Value >= 1) == true);
    public static bool Active(SptProfile? p) => p?.CharacterData?.PmcData?.Quests?.Any(q => q.QId.ToString() == QuestId && q.Status is QuestStatusEnum.Started or QuestStatusEnum.AvailableForFinish) == true;
    public static bool Success(SptProfile? p) => p?.CharacterData?.PmcData?.Quests?.Any(q => q.QId.ToString() == QuestId && q.Status == QuestStatusEnum.Success) == true;
    static bool Flag(SptProfile? p, string key) => p?.CharacterData?.PmcData?.Variables?.GetValueOrDefault(ProgressPolicy.Id(key)) == 1;
    public static Q4Ledger Read(SptProfile? p)
    {
        if (p?.ExtensionData?.TryGetValue(LedgerKey, out var raw) == true)
            return raw as Q4Ledger ?? JsonSerializer.Deserialize<Q4Ledger>(JsonSerializer.Serialize(raw)) ?? new();
        // Only legacy evidence is migrated, before native raid merge (pre-end hook).
        var delivery = Success(p) || LegacyProof(p) || ConditionDone(p, PlacementId);
        return new Q4Ledger { Delivery = delivery, RequestRead = delivery, PersonalRead = delivery,
            PersonalIssued = delivery, CopyRead = Success(p) || Flag(p, "q4CopyRead"),
            PersonalExtracted = Success(p) || delivery && ConditionDone(p, PersonalFindId),
            BusinessExtracted = Success(p) || delivery && (ConditionDone(p, BusinessGiveId) ||
                ConditionDone(p, BusinessFindId) && Held(p, BusinessReply)) };
    }
    static bool LegacyProof(SptProfile? p) => p?.ExtensionData?.TryGetValue(DurableReplyKey, out var v) == true &&
        (v is true || v is JsonElement { ValueKind: JsonValueKind.True });
    public static bool Initialize(SptProfile? p)
    {
        if (p?.CharacterData?.PmcData == null) return false;
        var fresh = p.ExtensionData?.ContainsKey(LedgerKey) != true;
        var state = Read(p);
        // Retain the old ledger and completed saves; the obsolete receipt is never a new gate.
        if (Success(p)) { state.Delivery = true; state.PersonalExtracted = true; state.CopyRead = true; }
        Write(p, state); return fresh;
    }
    public static void Write(SptProfile p, Q4Ledger state)
    {
        p.ExtensionData ??= new(); p.ExtensionData[LedgerKey] = state;
        var pmc = p.CharacterData!.PmcData!; pmc.Variables ??= new();
        foreach (var (fact, value) in Facts(state)) pmc.Variables[ProgressPolicy.Id(fact)] = value ? 1 : 0;
    }
    public static IEnumerable<(string fact, bool value)> Facts(Q4Ledger s)
    {
        yield return (ReplyReadyFact, s.Delivery); yield return ("q4RequestRead", s.RequestRead);
        yield return ("q4PersonalRead", s.PersonalRead); yield return ("q4CopyRead", s.CopyRead);
        yield return ("q4Attached", s.AttachedRequest != null); yield return ("q4BusinessExtracted", s.BusinessExtracted);
        yield return ("q4PersonalExtracted", s.PersonalExtracted); yield return ("q4FailedAttempt", s.FailedAttempt);
        yield return ("q4IncompleteAttempt", s.IncompleteAttempt);
    }
    public static bool Held(SptProfile? p, string tpl) => p?.CharacterData?.PmcData?.Inventory?.Items?.Any(i => i.Template.ToString() == tpl) == true;
    public static bool Pending(SptProfile p, string tpl, long now) => p.DialogueRecords?.Values.Any(d => d.Messages?.Any(m =>
        m.RewardCollected != true && m.Items?.Data?.Any(i => i.Template.ToString() == tpl) == true &&
        (m.DateTime == null || m.MaxStorageTime == null || m.MaxStorageTime <= 0 || now - m.DateTime < m.MaxStorageTime)) == true) == true;
    public static Decision Evaluate(SptProfile? p, long now)
    {
        if (p?.CharacterData?.PmcData?.Inventory?.Items == null) return Decision.MissingProfile;
        if (!Active(p)) return Decision.NotActive;
        if (Read(p).Delivery || ConditionDone(p, PlacementId)) return Decision.Placed;
        if (!ConditionDone(p, ClaimGiveId)) return Decision.AwaitingClaim;
        if (Pending(p, TemplateId, now)) return Decision.PendingMail;
        if (Held(p, TemplateId) || p.CharacterData.ScavData?.Inventory?.Items?.Any(i => i.Template.ToString() == TemplateId) == true) return Decision.Owned;
        return Decision.Issue;
    }
    public static bool ShouldIssuePersonal(SptProfile p, long now)
    {
        var s = Read(p);
        return Active(p) && s.RequestRead && !s.Delivery && s.AttachedRequest == null &&
            !Held(p, PersonalRequest) && !Pending(p, PersonalRequest, now) &&
            p.CharacterData?.ScavData?.Inventory?.Items?.Any(i => i.Template.ToString() == PersonalRequest) != true;
    }
    public static bool MarkRead(SptProfile p, string tpl)
    {
        if (!Active(p) && !Success(p)) return false;
        var s = Read(p);
        if (tpl == TemplateId && (Held(p, tpl) || Flag(p, "q4Received") || s.RequestRead)) s.RequestRead = true;
        else if (tpl == PersonalRequest && (Held(p, tpl) || s.PersonalRead || s.PersonalIssued)) s.PersonalRead = true;
        else if (tpl == PersonalCopy && (s.Delivery || Held(p, tpl) || s.PersonalExtracted || s.CopyRead)) s.CopyRead = true;
        else return false;
        Write(p, s); return true;
    }
    public static bool Attach(SptProfile p)
    {
        var s = Read(p); if (!Active(p) || s.Delivery || !s.RequestRead || !s.PersonalRead || ConditionDone(p, PlacementId)) return false;
        var items = p.CharacterData!.PmcData!.Inventory!.Items!;
        var packet = items.FirstOrDefault(i => i.Template.ToString() == TemplateId);
        if (packet == null) return false;
        if (s.AttachedRequest == packet.Id.ToString()) return true;
        var letter = items.FirstOrDefault(i => i.Template.ToString() == PersonalRequest);
        if (letter == null) return false;
        items.Remove(letter); s.AttachedRequest = packet.Id.ToString(); s.AttachedLetter = letter.Id.ToString(); Write(p, s); return true;
    }
    public static bool CanGiveBusiness(SptProfile p) => false;
    public static bool CanComplete(SptProfile p)
    {
        var s = Read(p); return Success(p) || s.Delivery && s.PersonalExtracted && s.CopyRead;
    }
    public static bool HasReplyProof(SptProfile? p) => Read(p).Delivery;
    public static bool RestoreReplyProof(SptProfile? p) => RecoverSavedPlacement(p);
    public static bool RecoverSavedPlacement(SptProfile? p)
    {
        if (p?.CharacterData?.PmcData == null) return false;
        var s = Read(p); var changed = false;
        // The marker is written only by native raid merge, after the pending intent was saved.
        // This distinguishes a crash after the native save from a failed/unprocessed request.
        if (s.PendingRaid is { } pending && Flag(p, pending.CommitFact))
        {
            changed = RecordEndedRaid(p, pending.ServerId, pending.ProfileId, pending.Result,
                pending.Carried.Select(t => new Item { Id = new SPTarkov.Server.Core.Models.Common.MongoId(), Template = t }));
            s = Read(p);
        }
        changed |= Facts(s).Any(x => Flag(p, x.fact) != x.value);
        changed |= Initialize(p);
        if (s.Delivery) changed |= SetCondition(p, PlacementId, true);
        if (s.PersonalExtracted) changed |= SetCondition(p, PersonalFindId, true);
        Write(p, s); return changed;
    }
    public static bool PrepareEndedRaid(SptProfile? p, EndLocalRaidRequestData request)
    {
        if (p?.CharacterData?.PmcData == null || request.Results?.Profile is not { } submitted ||
            submitted.Id != p.CharacterData.PmcData.Id) return false;
        var initialized = Initialize(p); var parts = request.ServerId?.Split('.');
        if (parts is not { Length: >= 2 } || !parts[0].Equals("woods", StringComparison.OrdinalIgnoreCase) ||
            !parts[1].Contains("pmc", StringComparison.OrdinalIgnoreCase)) return initialized;
        var s = Read(p);
        var marker = "q4NativeCommit_" + request.ServerId;
        s.PendingRaid = new Q4PendingRaid { ServerId = request.ServerId!, ProfileId = submitted.Id.ToString()!,
            Result = request.Results.Result.ToString()!, CommitFact = marker,
            Carried = (submitted.Inventory?.Items ?? []).Select(i => i.Template.ToString()).ToArray() };
        submitted.Variables ??= new(); submitted.Variables[ProgressPolicy.Id(marker)] = 1;
        Write(p, s); return true;
    }
    static bool SetCondition(SptProfile p, string id, bool done)
    {
        var pmc = p.CharacterData!.PmcData!;
        var q = pmc.Quests?.FirstOrDefault(x => x.QId.ToString() == QuestId);
        if (q == null || q.Status == QuestStatusEnum.Success) return false;
        q.CompletedConditions ??= [];
        if (done)
        {
            if (q.CompletedConditions.Contains(id)) return false;
            q.CompletedConditions.Add(id); return true;
        }
        var changed = q.CompletedConditions.Remove(id);
        changed |= pmc.TaskConditionCounters?.Remove(id) == true;
        if (q.Status == QuestStatusEnum.AvailableForFinish) { q.Status = QuestStatusEnum.Started; changed = true; }
        return changed;
    }
    // Called only after the native end-local save. A disconnect without a result never reaches here.
    public static bool RecordEndedRaid(SptProfile? p, string? serverId, string? submittedProfileId, string? result = null,
        IEnumerable<Item>? submittedItems = null)
    {
        var pmc = p?.CharacterData?.PmcData; var parts = serverId?.Split('.');
        if (pmc == null || pmc.Id.ToString() != submittedProfileId || parts is not { Length: >= 2 } ||
            !parts[0].Equals("woods", StringComparison.OrdinalIgnoreCase) || !parts[1].Contains("pmc", StringComparison.OrdinalIgnoreCase) || result == null) return false;
        var s = Read(p);
        if (s.PendingRaid is { } pending && pending.ServerId == serverId)
        {
            pmc.Variables?.Remove(ProgressPolicy.Id(pending.CommitFact)); s.PendingRaid = null;
        }
        if (!s.EndedRaids.Add(serverId!))
        {
            // Native replay can restore old counters. Reapply the already committed outcome.
            SetCondition(p!, PlacementId, s.Delivery);
            SetCondition(p!, PersonalFindId, s.PersonalExtracted);
            if (!s.BusinessExtracted && !ConditionDone(p, BusinessGiveId)) SetCondition(p!, BusinessFindId, false);
            Write(p!, s); return true;
        }
        var alive = result.Equals("Survived", StringComparison.OrdinalIgnoreCase) || result.Equals("Runner", StringComparison.OrdinalIgnoreCase) || result.Equals("RunThrough", StringComparison.OrdinalIgnoreCase);
        var wasDelivered = s.Delivery;
        if (!s.Delivery && ConditionDone(p, PlacementId))
        {
            var full = s.AttachedRequest != null && s.RequestRead && s.PersonalRead;
            s.Delivery = alive && full; s.FailedAttempt = !s.Delivery; s.IncompleteAttempt = !full;
            s.AttachedRequest = null;
            if (!s.Delivery) SetCondition(p!, PlacementId, false);
        }
        if (s.AttachedRequest != null && !Held(p, TemplateId)) s.AttachedRequest = null;
        // An answer can only originate in a raid entered after confirmed delivery.
        if (wasDelivered && alive)
        {
            var carried = (submittedItems ?? pmc.Inventory?.Items ?? []).Select(i => i.Template.ToString()).ToHashSet();
            s.BusinessExtracted |= carried.Contains(BusinessReply);
            s.PersonalExtracted |= carried.Contains(PersonalCopy);
        }
        if (!s.PersonalExtracted) SetCondition(p!, PersonalFindId, false);
        if (!s.BusinessExtracted && !ConditionDone(p, BusinessGiveId)) SetCondition(p!, BusinessFindId, false);
        Write(p!, s); if (s.PersonalExtracted) SetCondition(p!, PersonalFindId, true);
        return true;
    }
}
