using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.Quests;
using Newtonsoft.Json.Linq;
using UnityEngine;
using VisitAPI.ChapterUI;
using VisitAPI.Native;
namespace CustomQuests.E17;

internal static class LateSync
{
    static QuestController owner;
    static JObject snapshot;
    static bool busy;
    static int generation;
    static float refreshAt, expiresAt;
    static readonly HashSet<string> applied = new(StringComparer.Ordinal);
    static readonly HashSet<string> mirrored = new(StringComparer.Ordinal);
    internal static JObject State(QuestController qc) => owner?.Profile?.Id == qc?.Profile?.Id && qc?.Profile != null &&
        (Time.unscaledTime < expiresAt || !QuestOps.IsLobby(qc) && QuestOps.IsLive(qc)) ? snapshot : null;
    internal static void Clear()
    {
        generation++; owner = null; snapshot = null; busy = false; refreshAt = expiresAt = 0;
    }
    internal static void Tick(QuestController qc)
    {
        if (!QuestOps.IsLobby(qc) || !QuestOps.IsLive(qc)) return;
        if (VisitFlow.First(qc)?.QuestStatus != EQuestStatus.Success) return;
        if (!ReferenceEquals(owner, qc)) { Clear(); owner = qc; }
        if (!busy && Time.unscaledTime >= refreshAt)
            Plugin.Instance.StartCoroutine(Request(qc, generation, null, null));
    }
    internal static Task<bool> Act(QuestController qc, JToken action)
    {
        if (busy || !QuestOps.IsLobby(qc) || !QuestOps.IsLive(qc)) return Task.FromResult(false);
        if (!ReferenceEquals(owner, qc)) { Clear(); owner = qc; }
        var request = action is JObject obj ? (JObject)obj.DeepClone() : new JObject { ["action"] = (string)action };
        request["operationId"] = Guid.NewGuid().ToString("N");
        var subject = (string)request["subject"];
        var kind = (string)request["action"];
        request["expectedGeneration"] = kind is "RequestRecall" or "ReceiveRecalledCopy" or "DeliverReserveCopy"
            ? snapshot?["custody"]?[subject ?? "reserve"]?["generation"] ?? 0
            : snapshot?["reservation"]?["authorizationGeneration"] ?? 0;
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Plugin.Instance.StartCoroutine(Request(qc, generation, request, result));
        return result.Task;
    }
    static IEnumerator Request(QuestController captured, int version, JObject action, TaskCompletionSource<bool> done)
    {
        busy = true;
        var ok = false;
        try
        {
            yield return null;
            if (version != generation || !QuestOps.IsLobby(captured) || !QuestOps.IsLive(captured)) yield break;
            var request = action ?? new JObject();
            request["profileId"] = captured.Profile.Id;
            yield return VisitHttp.Call("/customquests/e17/late/" + (action == null ? "status" : "action"),
                request.ToString(), raw =>
                {
                    if (version != generation || !ReferenceEquals(owner, captured)) return false;
                    var data = JObject.Parse(raw)["data"] as JObject;
                    if ((string)data?["profileId"] != captured.Profile.Id) return false;
                    var operation = (string)data["operationId"];
                    var appliedKey = captured.Profile.Id + ":" + operation;
                    if (data["inventoryEffect"] is JObject effect && !applied.Contains(appliedKey))
                    {
                        if (captured is not QuestControllerClientBackend backend || backend.iQuestSession is not ClientBackendSession session)
                            throw new InvalidOperationException("E17 native profile session is unavailable");
                        LateInventoryApply.Apply(session, captured.Profile.Id, effect);
                        applied.Add(appliedKey);
                    }
                    snapshot = data["snapshot"] as JObject ?? data;
                    Mirror(captured);
                    expiresAt = Time.unscaledTime + 8f;
                    if (action != null && data["ok"]?.Value<bool>() != true)
                    {
                        Plugin.Warn("E17 action refused: " + (string)data["code"]);
                        return false;
                    }
                    return true;
                }, "E17 story state", accepted => ok = accepted, attempts: action == null ? 1 : 2, delay: 0.5f);
        }
        finally
        {
            if (version == generation)
            {
                busy = false; refreshAt = Time.unscaledTime + (ok ? 2f : 4f);
                ChapterEvents.Raise(); VisitFlow.RefreshParcelChoices();
            }
            done?.TrySetResult(ok && version == generation && ReferenceEquals(owner, captured));
        }
    }
    static void Mirror(QuestController qc)
    {
        var vars = qc.Profile.ProfileVariables;
        if (vars == null || snapshot == null) return;
        var current = new HashSet<string>((snapshot["serverFacts"] as JArray ?? new JArray()).Values<string>(), StringComparer.Ordinal);
        var choice = (string)snapshot["choice7"]; var keeper = (string)snapshot["keeper"];
        if (!string.IsNullOrEmpty(choice)) current.Add("choice7_" + choice);
        if (!string.IsNullOrEmpty(keeper)) current.Add("keeper_" + keeper);
        foreach (var key in mirrored.Union(current).ToArray())
            vars.SetVariableValue(Vars.Id("cq_e17_v1_server_" + key), current.Contains(key) ? 1 : 0);
        mirrored.Clear(); mirrored.UnionWith(current);
        vars.SetVariableValue(Vars.Id("cq_e17_v1_server_q18_familyGeneration"), (int?)snapshot["custody"]?["family"]?["generation"] ?? 0);
        vars.SetVariableValue(Vars.Id("cq_e17_v1_server_q20_authorizationGeneration"), (int?)snapshot["reservation"]?["authorizationGeneration"] ?? 0);
    }
}
