using System.Text.Json.Serialization;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;

namespace CustomQuests.E17.Server;

public sealed class IntroRequest : IRequestData
{
    [JsonPropertyName("profileId")] public string? ProfileId { get; set; }
    [JsonPropertyName("skipped")] public bool Skipped { get; set; }
}

[Injectable]
public sealed class IntroStore(SaveServer saves)
{
    public Task<bool> Finish(MongoId id, IntroRequest request, CancellationToken ct) =>
        ParcelRequestGate.Run(id, ct, async () =>
        {
            if (saves.IsProfileInvalidOrUnloadable(id)) return false;
            return await IntroState.Complete(saves.GetProfile(id), request.ProfileId, request.Skipped,
                async () => { await saves.SaveProfileAsync(id, ct); });
        });
}

[Injectable]
public sealed class IntroRouter : StaticRouter
{
    public IntroRouter(JsonUtil json, SaveServer saves, IntroStore store, HttpResponseUtil response)
        : base(json, [
            new RouteAction<IntroRequest>("/customquests/e17/intro/status", async (_, _, session, _, _) =>
            {
                var profile = saves.GetProfile(session);
                return response.GetBody(new { profileId = profile?.CharacterData?.PmcData?.Id.ToString(), eligible =
                    !saves.IsProfileInvalidOrUnloadable(session) &&
                    IntroState.Eligible(profile) });
            }),
            new RouteAction<IntroRequest>("/customquests/e17/intro/finish", async (_, request, session, _, ct) =>
            {
                var saved = await store.Finish(session, request, ct);
                var profile = saves.GetProfile(session);
                return response.GetBody(new { profileId = profile?.CharacterData?.PmcData?.Id.ToString(), saved });
            })]) { }
}
