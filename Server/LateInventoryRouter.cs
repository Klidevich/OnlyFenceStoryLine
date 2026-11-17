using System.Reflection;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.DI.Routing;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Request;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Routers.ItemEvents;

namespace CustomQuests.E17.Server;

/// <summary>Keep native wallet/inventory mutations out of an in-flight E17 debit/save/rollback.</summary>
[Injectable(TypePriority = OnLoadOrder.Routers - 3)]
public sealed class LateInventoryRouter(InventoryItemEventRouter inventory, TradeItemEventRouter trade,
    RagfairItemEventRouter ragfair, HideoutItemEventRouter hideout, RepairItemEventRouter repair,
    InsuranceItemEventRouter insurance, HealthItemEventRouter health, CustomizationItemEventRouter customization, ProfileHelper profiles)
    : ItemEventRouter(Build([inventory, trade, ragfair, hideout, repair, insurance, health, customization], session => profiles.GetPmcProfile(session)))
{
    static readonly MethodInfo Routes = typeof(ItemEventRouter).GetMethod("GetHandledRoutes", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("SPT 4.1.6 ItemEventRouter.GetHandledRoutes");
    public static IEnumerable<ItemRouteAction> Build(IEnumerable<ItemEventRouter> routers, Func<MongoId, PmcData?>? resolve = null)
    {
        foreach (var router in routers)
        {
            var routes = (IEnumerable<HandledRoute>)(Routes.Invoke(router, null) ?? throw new InvalidOperationException("Native item routes unavailable"));
            foreach (var route in routes)
            {
                var url = route.route;
                yield return new Forward(url, async (name, pmc, body, session, output, ct) =>
                    await ParcelRequestGate.Run(session, ct, async () => await router.HandleItemEvent(name, resolve?.Invoke(session) ?? pmc, body, session, output, ct)),
                    router.GetBodyType(url) ?? throw new InvalidOperationException("Native body type missing: " + url));
            }
        }
    }
    sealed record Forward(string Name,
        Func<string, PmcData, BaseInteractionRequestData, MongoId, ItemEventRouterResponse, CancellationToken, ValueTask<ItemEventRouterResponse>> Handler,
        Type RequestType) : ItemRouteAction(Name, Handler, RequestType);
}
