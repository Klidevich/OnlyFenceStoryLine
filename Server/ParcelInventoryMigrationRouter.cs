using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;

namespace CustomQuests.E17.Server;

// HttpRouter executes all matching routers in priority order. Native ProfileStaticRouter (400000)
// runs next and clones the normalized profile into its StreamedJsonBody. Never rewrite that stream.
[Injectable(TypePriority = OnLoadOrder.Routers - 1)]
public sealed class ParcelInventoryMigrationRouter(JsonUtil json, ProfileHelper profiles, SaveServer saves,
    TemplateTable templates, InventoryHelper inventory, ISptLogger<ParcelInventoryMigrationRouter> log)
    : StaticRouter(json, [new RouteAction("/client/game/profile/list",
        async (_, _, sessionId, output, ct) =>
        {
            var result = await ParcelRequestGate.Run(sessionId, ct, () =>
                ParcelInventoryMigrationPolicy.Migrate(profiles.GetFullProfile(sessionId), templates, inventory,
                    async () => { await saves.SaveProfileAsync(sessionId, ct); }));
            if (result.Warning != null) log.Warning("[E17 parcel migration] " + result.Warning);
            if (result.Moved > 0) log.Info($"[E17 parcel migration] Moved {result.Moved} existing parcel(s) into quest stash.");
            return output ?? string.Empty;
        }, typeof(EmptyRequestData))]) { }
