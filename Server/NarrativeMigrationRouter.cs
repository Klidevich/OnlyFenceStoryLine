using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;

namespace CustomQuests.E17.Server;

// Migrate removed templates before the native profile response is cloned.
[Injectable(TypePriority = OnLoadOrder.Routers - 4)]
public sealed class NarrativeMigrationRouter(JsonUtil json, ProfileHelper profiles,
    SaveServer saves, ICloner cloner, ISptLogger<NarrativeMigrationRouter> log)
    : StaticRouter(json, [new RouteAction("/client/game/profile/list",
        async (_, _, sessionId, output, ct) =>
        {
            var result = await ParcelRequestGate.Run(sessionId, ct, () =>
                NarrativeMigration.Migrate(profiles.GetFullProfile(sessionId), cloner,
                    async () => { await saves.SaveProfileAsync(sessionId, ct); }));
            if (result.Warning != null) log.Warning("[E17 narrative migration] " + result.Warning);
            if (result.Changed) log.Info("[E17 narrative migration] v0.27 progress migrated and saved for the current profile.");
            return output ?? string.Empty;
        }, typeof(EmptyRequestData))]) { }
