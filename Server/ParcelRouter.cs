using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.Commerce;
using SPTarkov.Server.Core.Utils;

namespace CustomQuests.E17.Server;

public sealed class ParcelRequest : IRequestData
{
    [JsonPropertyName("profileId")] public string? ProfileId { get; set; }
}

public static class ParcelRequestGate
{
    static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new();
    public static async Task<T> Run<T>(string session, CancellationToken ct, Func<Task<T>> body)
    {
        var gate = Gates.GetOrAdd(session, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try { return await body(); }
        finally { gate.Release(); }
    }
}

[Injectable]
public class ParcelRouter(JsonUtil json, ProfileHelper profiles, SaveServer saves,
    TemplateTable templates, MailSendService mail, HttpResponseUtil response)
    : StaticRouter(json, [new RouteAction("/customquests/e17/parcel/status",
        async (_, info, sessionId, _, ct) =>
        {
            var decision = await ParcelRequestGate.Run(sessionId, ct, () =>
            {
                var profile = profiles.GetFullProfile(sessionId);
                return Task.FromResult(profile?.CharacterData?.PmcData?.Id.ToString() != ((ParcelRequest)info).ProfileId
                    ? ParcelPolicy.Decision.MissingProfile
                    : ParcelPolicy.Evaluate(profile, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
            });
            return response.GetBody(new { profileId = ((ParcelRequest)info).ProfileId, status = decision.ToString() });
        }, typeof(ParcelRequest)), new RouteAction("/customquests/e17/parcel/replace",
        async (url, info, sessionId, output, ct) =>
        {
            var decision = await ParcelRequestGate.Run(sessionId, ct, async () =>
            {
                // No client-supplied profile, item ID, count, quest or placement state.
                var profile = profiles.GetFullProfile(sessionId);
                if (profile?.CharacterData?.PmcData?.Id.ToString() != ((ParcelRequest)info).ProfileId)
                    return ParcelPolicy.Decision.MissingProfile;
                var result = ParcelPolicy.Evaluate(profile, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                if (result != ParcelPolicy.Decision.Replace && result != ParcelPolicy.Decision.PendingMail) return result;
                if (!templates.Items.ContainsKey(ParcelPolicy.TemplateId) || !templates.Quests.ContainsKey(ParcelPolicy.QuestId))
                    throw new InvalidOperationException("E17 content pack is missing; parcel not issued");
                if (result == ParcelPolicy.Decision.Replace)
                    mail.SendLocalisedNpcMessageToPlayer(sessionId, new MongoId("579dc571d53a0658a154fbec"),
                        MessageType.NpcTraderMessage, ParcelPolicy.ReplacementText,
                        [new Item { Id = new MongoId(), Template = new MongoId(ParcelPolicy.TemplateId), Upd = new Upd { StackObjectsCount = 1 } }]);
                // Persist an already pending letter too: its first disk write may have failed.
                profile!.ExtensionData["customquestsE17ParcelRevision"] = Guid.NewGuid().ToString("N");
                await saves.SaveProfileAsync(sessionId, ct);
                return result;
            });
            return response.GetBody(new { profileId = ((ParcelRequest)info).ProfileId, status = decision.ToString() });
        }, typeof(ParcelRequest))]) { }
