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

namespace CustomQuests.E17.Server;

[Injectable]
public sealed class Q4Router(JsonUtil json, ProfileHelper profiles, SaveServer saves,
    TemplateTable templates, MailSendService mail, HttpResponseUtil response)
    : StaticRouter(json, [new RouteAction("/customquests/e17/q4/status", async (_, info, session, _, ct) =>
    {
        var snapshot = await ParcelRequestGate.Run(session, ct, () =>
        {
            var profile = profiles.GetFullProfile(session);
            var valid = profile?.CharacterData?.PmcData?.Id.ToString() == ((ParcelRequest)info).ProfileId;
            return Task.FromResult(new { profileId = ((ParcelRequest)info).ProfileId,
                status = (valid ? Q4Policy.Evaluate(profile, DateTimeOffset.UtcNow.ToUnixTimeSeconds()) : Q4Policy.Decision.MissingProfile).ToString(),
                repliesReady = valid && Q4Policy.HasReplyProof(profile) });
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
            if (decision == Q4Policy.Decision.Issue)
                mail.SendLocalisedNpcMessageToPlayer(session, new MongoId("579dc571d53a0658a154fbec"), MessageType.NpcTraderMessage,
                    Q4Policy.MailText, [new Item { Id = new MongoId(), Template = new MongoId(Q4Policy.TemplateId), Upd = new Upd { StackObjectsCount = 1 } }]);
            profile!.ExtensionData!["customquestsE17Q4Revision"] = Guid.NewGuid().ToString("N");
            await saves.SaveProfileAsync(session, ct);
            return decision;
        });
        return response.GetBody(new { profileId = ((ParcelRequest)info).ProfileId, status = status.ToString() });
    }, typeof(ParcelRequest))]) { }

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
            if (!Q4Policy.RecordEndedRaid(profile, request.ServerId, request.Results?.Profile?.Id.ToString())) return false;
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
