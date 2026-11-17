using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.Commerce;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;
using SPTarkov.Server.Core.Models.Utils;
using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;

namespace CustomQuests.E17.Server;

public sealed class Q4ActionRequest : IRequestData
{
    [JsonPropertyName("profileId")] public string? ProfileId { get; set; }
    [JsonPropertyName("action")] public string? Action { get; set; }
    [JsonPropertyName("templateId")] public string? TemplateId { get; set; }
}

[Injectable]
public sealed class Q4Router(JsonUtil json, ProfileHelper profiles, SaveServer saves,
    TemplateTable templates, MailSendService mail, HttpResponseUtil response, ICloner cloner)
    : StaticRouter(json, [new RouteAction("/customquests/e17/q4/status", async (_, info, session, _, ct) =>
    {
        var snapshot = await ParcelRequestGate.Run(session, ct, () =>
        {
            var profile = profiles.GetFullProfile(session);
            var valid = profile?.CharacterData?.PmcData?.Id.ToString() == ((ParcelRequest)info).ProfileId;
            return Task.FromResult(new { profileId = ((ParcelRequest)info).ProfileId,
                status = (valid ? Q4Policy.Evaluate(profile, DateTimeOffset.UtcNow.ToUnixTimeSeconds()) : Q4Policy.Decision.MissingProfile).ToString(),
                repliesReady = valid && Q4Policy.HasReplyProof(profile),
                facts = valid ? Q4Policy.Facts(Q4Policy.Read(profile)).Where(x => x.value).Select(x => x.fact).ToArray() : [] });
        });
        return response.GetBody(snapshot);
    }, typeof(ParcelRequest)), new RouteAction("/customquests/e17/q4/issue", async (_, info, session, _, ct) =>
    {
        var status = await ParcelRequestGate.Run(session, ct, async () =>
        {
            var profile = profiles.GetFullProfile(session);
            if (profile?.CharacterData?.PmcData?.Id.ToString() != ((ParcelRequest)info).ProfileId) return Q4Policy.Decision.MissingProfile;
            var decision = Q4Policy.Evaluate(profile, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            if (decision is not (Q4Policy.Decision.Issue or Q4Policy.Decision.PendingMail)) return decision;
            if (!templates.Items.ContainsKey(Q4Policy.TemplateId) || !templates.Quests.ContainsKey(Q4Policy.QuestId))
                throw new InvalidOperationException("E17 q4 pack missing; request not issued");
            await LateTransaction.Run(profile!, cloner, () =>
            {
            var state = Q4Policy.Read(profile);
            if (decision == Q4Policy.Decision.Issue)
            {
                state.AttachedRequest = null;
                mail.SendLocalisedNpcMessageToPlayer(session, new MongoId("579dc571d53a0658a154fbec"), MessageType.NpcTraderMessage,
                    Q4Policy.MailText, [new Item { Id = new MongoId(), Template = new MongoId(Q4Policy.TemplateId), Upd = new Upd { StackObjectsCount = 1 } }]);
            }
            Q4Policy.Write(profile!, state);
            profile!.ExtensionData!["customquestsE17Q4Revision"] = Guid.NewGuid().ToString("N");
            return Task.FromResult(true);
            }, async () => { await saves.SaveProfileAsync(session, ct); });
            return decision;
        });
        return response.GetBody(new { profileId = ((ParcelRequest)info).ProfileId, status = status.ToString() });
    }, typeof(ParcelRequest)), new RouteAction("/customquests/e17/q4/action", async (_, info, session, _, ct) =>
    {
        var request = (Q4ActionRequest)info;
        var result = await ParcelRequestGate.Run(session, ct, async () =>
        {
            var profile = profiles.GetFullProfile(session);
            if (profile?.CharacterData?.PmcData is not { } pmc || pmc.Id.ToString() != request.ProfileId) throw new InvalidOperationException("Stale Q4 profile");
            var before = cloner.Clone(pmc.Inventory!.Items)!;
            var ok = await LateTransaction.Run(profile, cloner, () =>
            {
                Q4Policy.Initialize(profile);
                var accepted = request.Action switch
                {
                    "read" => request.TemplateId != null && Q4Policy.MarkRead(profile, request.TemplateId),
                    "attach" => Q4Policy.Attach(profile),
                    "recoverPersonal" => Q4Policy.Active(profile) && Q4Policy.Read(profile).RequestRead,
                    _ => false
                };
                if (!accepted) return Task.FromResult(false);
                if (Q4Policy.ShouldIssuePersonal(profile, DateTimeOffset.UtcNow.ToUnixTimeSeconds()))
                {
                    if (!templates.Items.ContainsKey(Q4Policy.PersonalRequest)) throw new InvalidOperationException("Q4 personal letter template missing");
                    mail.SendLocalisedSystemMessageToPlayer(session, Q4Policy.PersonalMailText,
                        [new Item { Id = new MongoId(), Template = new MongoId(Q4Policy.PersonalRequest), Upd = new Upd { StackObjectsCount = 1 } }], null);
                    var state = Q4Policy.Read(profile); state.PersonalIssued = true; Q4Policy.Write(profile, state);
                }
                profile.ExtensionData!["customquestsE17Q4Revision"] = Guid.NewGuid().ToString("N");
                return Task.FromResult(true);
            }, async () => { await saves.SaveProfileAsync(session, ct); });
            // A retry of attach also returns the deletion, so a lost response cannot leave a ghost letter.
            var effect = LateInventoryDelta.Create(session, pmc, before, cloner);
            if (ok && request.Action == "attach" && Q4Policy.Read(profile).AttachedLetter is { } removed)
            {
                var deleted = effect.ProfileChanges[session].Items!.DeletedItems!;
                if (!deleted.Any(i => i.Id.ToString() == removed)) deleted.Add(new DeletedItem { Id = new MongoId(removed) });
            }
            return new { profileId = request.ProfileId, ok,
                facts = Q4Policy.Facts(Q4Policy.Read(profile)).Where(x => x.value).Select(x => x.fact).ToArray(), inventoryEffect = effect,
                personalMailAvailable = Q4Policy.Pending(profile, Q4Policy.PersonalRequest, DateTimeOffset.UtcNow.ToUnixTimeSeconds()) };
        });
        return response.GetBody(result);
    }, typeof(Q4ActionRequest))]) { }

// Native MatchStaticRouter is 400000. Preserve its response; only a successful native
// completion reaches this router. The second save is retryable and serialized with issue.
[Injectable(TypePriority = OnLoadOrder.Routers + 1)]
public sealed class Q4RaidRouter(JsonUtil json, ProfileHelper profiles, SaveServer saves)
    : StaticRouter(json, [new RouteAction("/client/match/local/end", async (_, info, session, output, ct) =>
    {
        var request = (EndLocalRaidRequestData)info;
        await ParcelRequestGate.Run(session, ct, async () =>
        {
            var profile = profiles.GetFullProfile(session);
            if (!Q4Policy.RecordEndedRaid(profile, request.ServerId, request.Results?.Profile?.Id.ToString(),
                request.Results?.Result.ToString(), request.Results?.Profile?.Inventory?.Items)) return false;
            profile!.ExtensionData!["customquestsE17Q4Revision"] = Guid.NewGuid().ToString("N");
            await saves.SaveProfileAsync(session, ct);
            return true;
        });
        return output ?? string.Empty;
    }, typeof(EndLocalRaidRequestData))]) { }

// A fresh native profile snapshot must contain the proof even after an older raid
// snapshot overwrote PMC Variables. A plant committed by native end-local also
// repairs a crash between its save and the following proof save. No items issue here.
[Injectable(TypePriority = OnLoadOrder.Routers - 1)]
public sealed class Q4ProfileRouter(JsonUtil json, ProfileHelper profiles, SaveServer saves)
    : StaticRouter(json, [new RouteAction("/client/game/profile/list", async (_, _, session, output, ct) =>
    {
        await ParcelRequestGate.Run(session, ct, async () =>
        {
            var profile = profiles.GetFullProfile(session);
            if (!Q4Policy.RecoverSavedPlacement(profile)) return false;
            profile!.ExtensionData!["customquestsE17Q4Revision"] = Guid.NewGuid().ToString("N");
            await saves.SaveProfileAsync(session, ct);
            return true;
        });
        return output ?? string.Empty;
    }, typeof(EmptyRequestData))]) { }

// Freeze legacy migration before a new native raid snapshot is merged. New deaths cannot become legacy success.
[Injectable(TypePriority = OnLoadOrder.Routers - 1)]
public sealed class Q4RaidPrepareRouter(JsonUtil json, ProfileHelper profiles, SaveServer saves)
    : StaticRouter(json, [new RouteAction("/client/match/local/end", async (_, info, session, output, ct) =>
    {
        await ParcelRequestGate.Run(session, ct, async () =>
        {
            var profile = profiles.GetFullProfile(session);
            if (!Q4Policy.PrepareEndedRaid(profile, (EndLocalRaidRequestData)info)) return false;
            await saves.SaveProfileAsync(session, ct); return true;
        });
        return output ?? string.Empty;
    }, typeof(EndLocalRaidRequestData))]) { }
