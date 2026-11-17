using System.Text.Json;
using System.Text.Json.Serialization;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Helpers.Quest;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.Commerce;
using SPTarkov.Server.Core.Utils.Cloners;
using SPTarkov.Server.Core.Utils;

namespace CustomQuests.E17.Server;

public sealed class LateRequest : IRequestData
{
    [JsonPropertyName("profileId")] public string? ProfileId { get; set; }
    [JsonPropertyName("operationId")] public string? OperationId { get; set; }
    [JsonPropertyName("action")] public string? Action { get; set; }
    [JsonPropertyName("subject")] public string? Subject { get; set; }
    [JsonPropertyName("choice")] public string? Choice { get; set; }
    [JsonPropertyName("expectedGeneration")] public int? ExpectedGeneration { get; set; }
}

public sealed class LateNativeState(SptProfile profile, LateLedger ledger, long now) : ILateNativeState
{
    public int Status(string q) => (int?)profile.CharacterData?.PmcData?.Quests?.FirstOrDefault(x => x.QId.ToString() == LateIds.Id(q))?.Status ?? 0;
    public bool Active(string q) => profile.CharacterData?.PmcData?.Quests?.Any(x => x.QId.ToString() == LateIds.Id(q) && x.Status is QuestStatusEnum.Started or QuestStatusEnum.AvailableForFinish) == true;
    public bool Success(string q) => profile.CharacterData?.PmcData?.Quests?.Any(x => x.QId.ToString() == LateIds.Id(q) && x.Status == QuestStatusEnum.Success) == true;
    public bool Done(string condition)
    {
        var pmc = profile.CharacterData?.PmcData;
        var q = LateIds.Id(condition.Split('/')[0]); var id = LateIds.Id(condition);
        return pmc?.Quests?.Any(x => x.QId.ToString() == q && x.CompletedConditions?.Contains(id) == true) == true ||
            pmc?.TaskConditionCounters?.Any(x => x.Key.ToString() == id && x.Value.SourceId?.ToString() == q && x.Value.Value >= 1) == true;
    }
    public bool Fact(string fact) => profile.CharacterData?.PmcData?.Variables?.GetValueOrDefault(ProgressPolicy.Id(fact)) > 0;
    public bool Held(string key) => profile.CharacterData?.PmcData?.Inventory?.Items?.Any(x => x.Template.ToString() == LateIds.Id(key) && !ledger.RevokedItems.Contains(x.Id.ToString())) == true;
    public long Roubles => profile.CharacterData?.PmcData?.Inventory?.Items?.Where(x => x.Template.ToString() == LateIds.Rouble)
        .Sum(x => checked((long)Math.Floor(x.Upd?.StackObjectsCount ?? 0))) ?? 0;
    public string Parcel(string key)
    {
        var tpl = LateIds.Id(key);
        if (profile.DialogueRecords?.Values.Any(d => d.Messages?.Any(m => m.RewardCollected != true &&
            m.Items?.Data?.Any(i => i.Template.ToString() == tpl && !ledger.RevokedItems.Contains(i.Id.ToString())) == true &&
            (m.DateTime == null || m.MaxStorageTime == null || m.MaxStorageTime <= 0 || now - m.DateTime < m.MaxStorageTime)) == true) == true) return "PendingMail";
        if (Held(key) || profile.CharacterData?.ScavData?.Inventory?.Items?.Any(x => x.Template.ToString() == tpl && !ledger.RevokedItems.Contains(x.Id.ToString())) == true) return "Owned";
        return "Missing";
    }
}

[Injectable]
public sealed class LateService(ProfileHelper profiles, SaveServer saves, TemplateTable templates, MailSendService mail, ICloner cloner, QuestRewardHelper rewards, JsonUtil json)
{
    public const string LedgerKey = "customquestsE17LateLedger";
    const string Fence = "579dc571d53a0658a154fbec";
    public static LateLedger Read(SptProfile profile)
    {
        if (profile.ExtensionData?.TryGetValue(LedgerKey, out var value) != true || value == null) return new();
        if (value is LateLedger ledger) return LatePolicy.Copy(ledger);
        return JsonSerializer.Deserialize<LateLedger>(value is JsonElement element ? element.GetRawText() : JsonSerializer.Serialize(value))
            ?? throw new InvalidOperationException("E17 late ledger is null");
    }
    public static void Write(SptProfile profile, LateLedger ledger)
    {
        profile.ExtensionData ??= new();
        profile.ExtensionData[LedgerKey] = JsonSerializer.SerializeToElement(ledger);
        profile.ExtensionData["customquestsE17LateRevision"] = Guid.NewGuid().ToString("N");
    }
    public static object Response(string? profileId, LateLedger ledger, ILateNativeState? native, bool ok, string code, string? operationId = null, bool inventoryRefreshRequired = false)
    {
        var inventoryEffect = operationId != null && ledger.Operations.TryGetValue(operationId, out var operation) ? operation.InventoryEffect : null;
        var flags = native == null ? new HashSet<string>() : LatePolicy.SnapshotFlags(ledger, native);
        var parcels = LatePolicy.Parcels.ToDictionary(x => x, x => native == null ? "Unavailable" :
            LatePolicy.CanIssue(ledger, native, x) ? native.Parcel(x) : "Unavailable");
        return new
        {
            profileId, ok, code, operationId, revision = ledger.Revision, inventoryRefreshRequired = inventoryEffect.HasValue, inventoryEffect,
            snapshot = new
            {
                choice7 = ledger.Choice7, keeper = ledger.Keeper, serverFacts = flags.Order().ToArray(),
                custody = ledger.Custody.ToDictionary(x => x.Key, x => new { generation = x.Value.Generation, status = x.Value.Status, returnReady = x.Value.ReturnReady,
                    placementCondition = LateIds.Id(LatePolicy.CopyProof(x.Key, x.Value.Generation)) }),
                reservation = new { state = ledger.Reservation.Status, amount = ledger.Reservation.Amount, authorizationGeneration = ledger.Reservation.Generation,
                    cancelAllowed = ledger.Reservation.Status == "Reserved" && !ledger.Reservation.FinalDeposited,
                    finalDeposited = ledger.Reservation.FinalDeposited, placementProof = LateIds.Id(LatePolicy.AuthorizationProof(ledger.Reservation.Generation)) },
                parcels
            }
        };
    }
    public Task<object> Status(string session, LateRequest request, CancellationToken ct, bool returningToLobby = false) => ParcelRequestGate.Run(session, ct, async () =>
    {
        var profile = profiles.GetFullProfile(session);
        if (profile?.CharacterData?.PmcData == null || profile.CharacterData.PmcData.Id.ToString() != request.ProfileId) return Response(request.ProfileId, new(), null, false, "InvalidProfile");
        var ledger = Read(profile); var native = new LateNativeState(profile, ledger, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        await LateTransaction.Run(profile, cloner, () =>
        {
            var changed = false;
            if (returningToLobby && !string.IsNullOrEmpty(ledger.ActiveRaidId)) { ledger.ActiveRaidId = ""; changed = true; }
            if (native.Success("q7") && !ledger.SettlementPaid && ledger.SettlementIntent is "cash" or "tools")
            {
                if (ledger.SettlementIntent == "cash") SendMoney(session, LateSettlement.CashAmount(profile.CharacterData.PmcData, rewards), "e17 late q7 settlement");
                ledger.Choice7 = ledger.SettlementIntent; ledger.SettlementPaid = true; changed = true;
            }
            changed |= LatePolicy.Reconcile(ledger, native, visit: true);
            changed |= MirrorFlags(profile, ledger, native);
            if (changed) { ledger.Revision++; Write(profile, ledger); MirrorAccess(profile, ledger); }
            return Task.FromResult(changed);
        }, async () => { await saves.SaveProfileAsync(session, ct); });
        return Response(request.ProfileId, ledger, native, true, "Snapshot");
    });

    public Task<object> Action(string session, LateRequest request, CancellationToken ct) => ParcelRequestGate.Run(session, ct, async () =>
    {
        var profile = profiles.GetFullProfile(session);
        if (profile?.CharacterData?.PmcData == null || profile.CharacterData.PmcData.Id.ToString() != request.ProfileId) return Response(request.ProfileId, new(), null, false, "InvalidProfile");
        var ledger = Read(profile); var native = new LateNativeState(profile, ledger, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var next = LatePolicy.Copy(ledger);
        var command = new LateCommand(request.OperationId ?? "", request.Action ?? "", request.Subject ?? "", request.Choice, request.ExpectedGeneration);
        if (!ledger.Operations.ContainsKey(command.OperationId) && !LateRules.CanAction(command, native, ledger)) return Response(request.ProfileId, ledger, native, false, "AuthoredGuardFailed", request.OperationId);
        var decision = LatePolicy.Apply(next, native, command);
        if (!decision.Ok || decision.Code == "Replayed") return Response(request.ProfileId, ledger, native, decision.Ok, decision.Code, request.OperationId);
        var beforeItems = cloner.Clone(profile.CharacterData.PmcData.Inventory?.Items) ?? [];
        await LateTransaction.Run(profile, cloner, () =>
        {
            ApplyEffects(session, profile, next, decision.Effects);
            var delta = LateInventoryDelta.Create(session, profile.CharacterData.PmcData, beforeItems, cloner);
            if (LateInventoryDelta.HasChanges(delta))
            {
                using var parsed = JsonDocument.Parse(json.Serialize(delta) ?? throw new InvalidOperationException("Native inventory delta serialization failed"));
                next.Operations[command.OperationId] = next.Operations[command.OperationId] with { InventoryEffect = parsed.RootElement.Clone() };
            }
            MirrorFlags(profile, next, new LateNativeState(profile, next, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
            Write(profile, next);
            return Task.FromResult(true);
        }, async () => { await saves.SaveProfileAsync(session, ct); });
        return Response(request.ProfileId, next, new LateNativeState(profile, next, DateTimeOffset.UtcNow.ToUnixTimeSeconds()), true, decision.Code, request.OperationId,
            decision.Effects.Debit != 0 || decision.Effects.Refund != 0 || decision.Effects.Consume != null || decision.Effects.RevokeAuthorization);
    });

    void ApplyEffects(string session, SptProfile profile, LateLedger ledger, LateEffects fx)
    {
        if (fx.Debit > 0) Debit(profile, fx.Debit);
        if (fx.RevokeAuthorization) Revoke(profile, ledger, "q20/item/finalAuthorization");
        foreach (var key in fx.RevokeParcels) Revoke(profile, ledger, key);
        if (fx.Consume != null)
        {
            var item = profile.CharacterData!.PmcData!.Inventory!.Items!.FirstOrDefault(x => x.Template.ToString() == LateIds.Id(fx.Consume) && !ledger.RevokedItems.Contains(x.Id.ToString()))
                ?? throw new InvalidOperationException("E17 late physical handover item missing");
            profile.CharacterData!.PmcData!.Inventory!.Items!.Remove(item);
        }
        if (fx.Refund > 0) SendMoney(session, fx.Refund, "e17 late reserve returned");
        if (fx.Issue != null)
        {
            var tpl = LateIds.Id(fx.Issue);
            if (!templates.Items.TryGetValue(tpl, out var template) || template.Properties?.QuestItem != true)
                throw new InvalidOperationException("E17 late quest parcel template missing or not a quest item: " + fx.Issue);
            var item = new Item { Id = new MongoId(), Template = new MongoId(tpl), Upd = new Upd { StackObjectsCount = 1 } };
            mail.SendLocalisedNpcMessageToPlayer(session, new MongoId(Fence), MessageType.NpcTraderMessage, "e17 late parcel issue", [item]);
            if (!ledger.IssuedItems.TryGetValue(fx.Issue, out var ids)) ledger.IssuedItems[fx.Issue] = ids = new(StringComparer.Ordinal);
            ids.Add(item.Id.ToString());
        }
    }
    public static void Debit(SptProfile profile, long amount)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        var items = profile.CharacterData?.PmcData?.Inventory?.Items ?? throw new InvalidOperationException("Inventory unavailable");
        var money = items.Where(x => x.Template.ToString() == LateIds.Rouble).ToList();
        if (money.Sum(x => checked((long)(x.Upd?.StackObjectsCount ?? 0))) < amount) throw new InvalidOperationException("InsufficientFunds");
        foreach (var item in money)
        {
            var count = checked((long)(item.Upd?.StackObjectsCount ?? 0)); var take = Math.Min(count, amount);
            if (take == count) items.Remove(item); else item.Upd!.StackObjectsCount = count - take;
            amount -= take; if (amount == 0) break;
        }
    }
    public void SendMoney(string session, long amount, string locale)
    {
        if (amount <= 0) return;
        // Same physical rouble template, split before native mail delivery; no stash-space dependent refund.
        var stacks = new List<Item>();
        while (amount > 0)
        {
            var count = Math.Min(500_000L, amount);
            stacks.Add(new Item { Id = new MongoId(), Template = new MongoId(LateIds.Rouble), Upd = new Upd { StackObjectsCount = count } });
            amount -= count;
        }
        mail.SendLocalisedNpcMessageToPlayer(session, new MongoId(Fence), MessageType.NpcTraderMessage, locale, stacks);
    }
    public static void Revoke(SptProfile profile, LateLedger ledger, string key)
    {
        var tpl = LateIds.Id(key);
        var all = new List<Item>();
        all.AddRange(profile.CharacterData?.PmcData?.Inventory?.Items?.Where(x => x.Template.ToString() == tpl) ?? []);
        all.AddRange(profile.CharacterData?.ScavData?.Inventory?.Items?.Where(x => x.Template.ToString() == tpl) ?? []);
        foreach (var d in profile.DialogueRecords?.Values.AsEnumerable() ?? [])
            foreach (var m in d.Messages ?? [])
                if (m.Items?.Data != null)
                {
                    var found = m.Items.Data.Where(x => x.Template.ToString() == tpl).ToList(); all.AddRange(found);
                    foreach (var item in found) m.Items.Data.Remove(item);
                }
        foreach (var item in all)
        {
            ledger.RevokedItems.Add(item.Id.ToString());
            profile.CharacterData?.PmcData?.Inventory?.Items?.Remove(item);
            profile.CharacterData?.ScavData?.Inventory?.Items?.Remove(item);
        }
    }
    public static void MirrorAccess(SptProfile profile, LateLedger ledger)
    {
        var pmc = profile.CharacterData?.PmcData; if (pmc == null) return;
        pmc.Variables ??= new();
        foreach (var (fact, trader) in new[] { ("unlockTherapist", "54cb57776803fa99248b456e"), ("unlockPrapor", "54cb50c76803fa8b248b4571") })
            if (ledger.Flags.Contains(fact))
            {
                pmc.Variables[ProgressPolicy.Id(fact)] = 1;
                // OnlyFence exceptions are a transient integration, never a permanent profile trader unlock.
            }
    }
    public static bool MirrorFlags(SptProfile profile, LateLedger ledger, ILateNativeState native)
    {
        var pmc = profile.CharacterData?.PmcData; if (pmc == null) return false;
        pmc.Variables ??= new();
        var current = LatePolicy.SnapshotFlags(ledger, native);
        if (ledger.Choice7 is "cash" or "tools") current.Add("choice7_" + ledger.Choice7);
        if (ledger.Keeper is "Fence" or "Ragman") current.Add("keeper_" + ledger.Keeper);
        var changed = false;
        foreach (var key in ledger.MirroredFlags.Union(current))
        {
            var id = ProgressPolicy.Id("server_" + key); var value = current.Contains(key) ? 1 : 0;
            if (pmc.Variables.GetValueOrDefault(id) != value) { pmc.Variables[id] = value; changed = true; }
        }
        if (!ledger.MirroredFlags.SetEquals(current)) changed = true;
        ledger.MirroredFlags = current;
        foreach (var pair in new[] {
            ("server_q18_familyGeneration", ledger.Custody["family"].Generation),
            ("server_q20_authorizationGeneration", ledger.Reservation.Status == "Reserved" ? ledger.Reservation.Generation : 0)
        })
        {
            var id = ProgressPolicy.Id(pair.Item1);
            if (pmc.Variables.GetValueOrDefault(id) != pair.Item2) { pmc.Variables[id] = pair.Item2; changed = true; }
        }
        return changed;
    }
    public Task RaidEnded(string session, string? raidId, string? submittedProfile, CancellationToken ct) => ParcelRequestGate.Run(session, ct, async () =>
    {
        var profile = profiles.GetFullProfile(session);
        if (profile?.CharacterData?.PmcData == null || string.IsNullOrWhiteSpace(raidId) ||
            (profile.CharacterData.PmcData.Id.ToString() != submittedProfile && profile.CharacterData.ScavData?.Id.ToString() != submittedProfile)) return false;
        var ledger = Read(profile); var native = new LateNativeState(profile, ledger, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        if (!LatePolicy.RecordRaid(ledger, native, raidId)) return false;
        await LateTransaction.Run(profile, cloner, () =>
        {
            MirrorFlags(profile, ledger, native); Write(profile, ledger); MirrorAccess(profile, ledger);
            return Task.FromResult(true);
        }, async () => { await saves.SaveProfileAsync(session, ct); });
        return true;
    });
    public Task RaidStarted(string session, string output, CancellationToken ct) => ParcelRequestGate.Run(session, ct, async () =>
    {
        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;
        if (root.TryGetProperty("data", out var data)) root = data;
        if (!root.TryGetProperty("serverId", out var id) || id.ValueKind != JsonValueKind.String) throw new InvalidOperationException("Native raid start has no serverId");
        var profile = profiles.GetFullProfile(session) ?? throw new InvalidOperationException("E17 raid profile missing");
        var ledger = Read(profile);
        ledger.ActiveRaidId = id.GetString()!;
        ledger.Revision++;
        await LateTransaction.Run(profile, cloner, () => { Write(profile, ledger); return Task.FromResult(true); }, async () => { await saves.SaveProfileAsync(session, ct); });
        return true;
    });
}
