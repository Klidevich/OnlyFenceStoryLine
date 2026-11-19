using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Helpers.Traders;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;

namespace CustomQuests.E17.Server;

// Execute before native ProfileStaticRouter clones the profile for the client.
// Session identity comes from SPT; no profile identifier is accepted in the body.
[Injectable(TypePriority = OnLoadOrder.Routers - 3)]
public sealed class JaegerAccessRouter(JsonUtil json, ProfileHelper profiles, TraderHelper traders,
    SaveServer saves, ISptLogger<JaegerAccessRouter> log)
    : StaticRouter(json, [new RouteAction("/client/game/profile/list",
        async (_, _, sessionId, output, ct) =>
        {
            var result = await ParcelRequestGate.Run(sessionId, ct, () =>
                JaegerAccess.Migrate(profiles.GetFullProfile(sessionId),
                    () => traders.SetTraderUnlockedState(Traders.JAEGER, true, sessionId),
                    async () => { await saves.SaveProfileAsync(sessionId, ct); }));
            if (result.Warning != null) log.Warning("[E17 trader access] " + result.Warning);
            if (result.Changed) log.Info("[E17 trader access] Jaeger access restored and saved for the current profile.");
            return output ?? string.Empty;
        }, typeof(EmptyRequestData))]) { }
