using System.Reflection;
using System.Text.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Callbacks;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.DI.Routing;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Helpers.Quest;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Eft.Quests;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;

namespace CustomQuests.E17.Server;

[Injectable]
public sealed class LateRouter(JsonUtil json, LateService late, HttpResponseUtil response)
    : StaticRouter(json, [
        new RouteAction("/customquests/e17/late/status", async (_, info, session, _, ct) =>
            response.GetBody(await late.Status(session, (LateRequest)info, ct)), typeof(LateRequest)),
        new RouteAction("/customquests/e17/late/action", async (_, info, session, _, ct) =>
            response.GetBody(await late.Action(session, (LateRequest)info, ct)), typeof(LateRequest))]) { }

[Injectable(TypePriority = OnLoadOrder.Routers + 2)]
public sealed class LateRaidRouter(JsonUtil json, LateService late)
    : StaticRouter(json, [new RouteAction("/client/match/local/start", async (_, _, session, output, ct) =>
    {
        await late.RaidStarted(session, output ?? throw new InvalidOperationException("Native raid start output absent"), ct);
        return output;
    }, typeof(StartLocalRaidRequestData)), new RouteAction("/client/match/local/end", async (_, info, session, output, ct) =>
    {
        var raid = (EndLocalRaidRequestData)info;
        await late.RaidEnded(session, raid.ServerId, raid.Results?.Profile?.Id.ToString(), ct);
        return output ?? string.Empty;
    }, typeof(EndLocalRaidRequestData))]) { }

[Injectable(TypePriority = OnLoadOrder.Routers - 2)]
public sealed class LateProfileRouter(JsonUtil json, LateService late, ProfileHelper profiles)
    : StaticRouter(json, [new RouteAction("/client/game/profile/list", async (_, _, session, output, ct) =>
    {
        var profile = profiles.GetFullProfile(session);
        if (profile?.CharacterData?.PmcData != null)
            await late.Status(session, new LateRequest { ProfileId = profile.CharacterData.PmcData.Id.ToString() }, ct, returningToLobby: true);
        return output ?? string.Empty;
    }, typeof(EmptyRequestData))]) { }

public static class LateSettlement
{
    static readonly MethodInfo Bonus = typeof(QuestRewardHelper).GetMethod("GetQuestMoneyRewardBonusMultiplier", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("SPT 4.1.6 QuestRewardHelper.GetQuestMoneyRewardBonusMultiplier");
    public static long BaseCash()
    {
        var assembly = typeof(LateSettlement).Assembly;
        var name = assembly.GetManifestResourceNames().SingleOrDefault(x => x.EndsWith("rewards-v018.json", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("E17 rewards-v018.json resource missing");
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var doc = JsonDocument.Parse(stream);
        return doc.RootElement.GetProperty("quests").GetProperty("q7").GetProperty("choice").GetProperty("options").GetProperty("cash").GetProperty("roubles").GetInt64();
    }
    public static long CashAmount(PmcData pmc, QuestRewardHelper rewards)
    {
        var percent = (double)(Bonus.Invoke(rewards, [pmc]) ?? 0d);
        return checked((long)Math.Floor(BaseCash() * (1d + Math.Max(0, percent) / 100d)));
    }
}

[Injectable(TypePriority = OnLoadOrder.Routers - 2)]
public sealed class LateQuestRouter : ItemEventRouter
{
    public LateQuestRouter(QuestCallbacks native, ProfileHelper profiles, SaveServer saves, LateService late, QuestRewardHelper rewards, ICloner cloner, TemplateTable templates, InventoryHelper inventory)
        : base([
            new ItemRouteAction<CompleteQuestRequestData>("QuestComplete", async (_, pmc, body, session, output, ct) =>
            {
                var key = Enumerable.Range(5, 18).Select(i => "q" + i).FirstOrDefault(q => LateIds.Id(q) == body.QuestId.ToString());
                if (key == null) return await ParcelRequestGate.Run(session, ct, () =>
                {
                    var livePmc = profiles.GetPmcProfile(session) ?? pmc;
                    if (body.QuestId.ToString() == Q4Policy.QuestId)
                    {
                        var profile = profiles.GetFullProfile(session) ?? throw new InvalidOperationException("E17 profile missing");
                        if (Q4Policy.Success(profile)) return Task.FromResult(output);
                        if (!Q4Policy.CanComplete(profile)) throw new InvalidOperationException("E17 q4 delivery, replies and reading are not complete");
                    }
                    return Q2NoteCleanup.Complete(livePmc, body.QuestId, session, output,
                        () => native.CompleteQuest(livePmc, body, session), inventory, ct);
                });
                return await ParcelRequestGate.Run(session, ct, async () =>
                {
                    var profile = profiles.GetFullProfile(session) ?? throw new InvalidOperationException("E17 profile missing");
                    var livePmc = profile.CharacterData?.PmcData ?? throw new InvalidOperationException("E17 PMC missing");
                    var state = LateService.Read(profile);
                    var view = new LateNativeState(profile, state, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                    if (view.Success(key))
                    {
                        if (key == "q7" && !state.SettlementPaid && state.SettlementIntent is "cash" or "tools")
                            await LateTransaction.Run(profile, cloner, () =>
                            {
                                if (state.SettlementIntent == "cash") late.SendMoney(session, LateSettlement.CashAmount(livePmc, rewards), "e17 late q7 settlement");
                                state.Choice7 = state.SettlementIntent; state.SettlementPaid = true; state.Revision++;
                                LateService.Write(profile, state);
                                return Task.FromResult(true);
                            }, async () => { await saves.SaveProfileAsync(session, ct); });
                        return output;
                    }
                    if (!view.Active(key)) throw new InvalidOperationException("E17 late quest is not active");
                    if (!LateRules.CanFinish(key, view, state)) throw new InvalidOperationException("E17 authored completion guard is not satisfied");
                    if (key == "q7" && (state.SettlementIntent is not ("cash" or "tools") ||
                        !view.Done("q7/give/transportReturnLog") || !view.Done("q7/give/rudinTools") || !view.Done("q7/find/transportRefusal")))
                        throw new InvalidOperationException("E17 q7 settlement is not ready");
                    if (key == "q7" && templates.Quests.TryGetValue(body.QuestId, out var q7) &&
                        q7.Rewards?.GetValueOrDefault("Success")?.Any(r => r.Items?.Any(i => i.Template.ToString() == LateIds.Rouble) == true) == true)
                        throw new InvalidOperationException("E17 q7 native rewards contain cash; conditional settlement would duplicate it");
                    var characters = cloner.Clone(profile.CharacterData);
                    var dialogue = cloner.Clone(profile.DialogueRecords);
                    var extension = cloner.Clone(profile.ExtensionData);
                    try
                    {
                        if (key == "q7") state.Choice7 = state.SettlementIntent;
                        var result = await native.CompleteQuest(livePmc, body, session);
                        if (!new LateNativeState(profile, state, 0).Success(key)) throw new InvalidOperationException("Native late completion was not committed");
                        if (key == "q7" && !state.SettlementPaid)
                        {
                            if (state.Choice7 == "cash")
                            {
                                late.SendMoney(session, LateSettlement.CashAmount(livePmc, rewards), "e17 late q7 settlement");
                            }
                            state.SettlementPaid = true;
                        }
                        LatePolicy.Reconcile(state, new LateNativeState(profile, state, 0));
                        state.Revision++;
                        LateService.MirrorFlags(profile, state, new LateNativeState(profile, state, 0));
                        LateService.Write(profile, state);
                        LateService.MirrorAccess(profile, state);
                        await saves.SaveProfileAsync(session, ct);
                        return result;
                    }
                    catch
                    {
                        profile.CharacterData = characters;
                        profile.DialogueRecords = dialogue;
                        profile.ExtensionData = extension;
                        profile.ExtensionData ??= new();
                        profile.ExtensionData["customquestsE17LateRevision"] = Guid.NewGuid().ToString("N");
                        throw;
                    }
                });
            }),
            new ItemRouteAction<HandoverQuestRequestData>("QuestHandover", async (_, pmc, body, session, output, ct) =>
                await ParcelRequestGate.Run(session, ct, async () =>
                {
                    if (body.QuestId.ToString() == Q4Policy.QuestId && body.ConditionId.ToString() == Q4Policy.BusinessGiveId)
                    {
                        var profile = profiles.GetFullProfile(session) ?? throw new InvalidOperationException("E17 profile missing");
                        if (!Q4Policy.CanGiveBusiness(profile)) throw new InvalidOperationException("E17 q4 business reply has not been extracted or is missing");
                    }
                    return await native.HandoverQuest(profiles.GetPmcProfile(session) ?? pmc, body, session);
                })),
            new ItemRouteAction<AcceptQuestRequestData>("QuestAccept", async (_, pmc, body, session, output, ct) =>
                await ParcelRequestGate.Run(session, ct, async () => await native.AcceptQuest(profiles.GetPmcProfile(session) ?? pmc, body, session))),
            new ItemRouteAction<RepeatableQuestChangeRequest>("RepeatableQuestChange", async (_, pmc, body, session, output, ct) =>
                await ParcelRequestGate.Run(session, ct, async () => await native.ChangeRepeatableQuest(profiles.GetPmcProfile(session) ?? pmc, body, session)))
        ]) { }
}
