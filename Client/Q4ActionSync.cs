using System;
using System.Collections;
using System.Collections.Generic;
using EFT;
using EFT.Communications;
using EFT.Quests;
using Newtonsoft.Json.Linq;
using VisitAPI.ChapterUI;
using VisitAPI.Native;

namespace CustomQuests.E17;

internal static class Q4ActionSync
{
    static readonly Queue<(QuestController qc, string action, string template)> queue = new();
    static readonly HashSet<string> deleted = new();
    internal static bool Busy { get; private set; }
    internal static readonly string[] ServerFacts = { "q4ReplyReady", "q4RequestRead", "q4PersonalRead", "q4CopyRead",
        "q4Attached", "q4BusinessExtracted", "q4PersonalExtracted", "q4FailedAttempt", "q4IncompleteAttempt" };
    internal static void Mirror(QuestController qc, JToken data)
    {
        if (data?["facts"] is not JArray facts) return;
        var enabled = new HashSet<string>(facts.Values<string>());
        foreach (var fact in ServerFacts)
            qc.Profile.ProfileVariables.SetVariableValue(Vars.Id("cq_e17_v1_" + fact), enabled.Contains(fact) ? 1 : 0);
    }
    internal static void Action(QuestController qc, string action, string template = null)
    {
        if (qc?.Profile == null || !QuestOps.IsLive(qc)) return;
        if (action != "read" && !QuestOps.IsLobby(qc)) return;
        queue.Enqueue((qc, action, template));
        if (!Busy) { Busy = true; Plugin.Instance.StartCoroutine(Run()); }
    }
    static IEnumerator Run()
    {
        try
        {
            yield return null;
            while (queue.Count != 0)
            {
                var (qc, action, template) = queue.Dequeue();
                if (!QuestOps.IsLive(qc)) continue;
                var body = new JObject { ["profileId"] = qc.Profile.Id, ["action"] = action, ["templateId"] = template };
                var wasRead = VisitFlow.Has(qc, "q4RequestRead");
                yield return VisitHttp.Call("/customquests/e17/q4/action", body.ToString(), raw =>
                {
                    var data = JObject.Parse(raw)["data"];
                    if ((string)data?["profileId"] != qc.Profile.Id || data?["ok"]?.Value<bool>() != true) return false;
                    if (action == "attach" && data["inventoryEffect"] is JObject effect)
                    {
                        var key = qc.Profile.Id + ":" + effect.ToString();
                        if (!deleted.Contains(key))
                        {
                            if (qc is not QuestControllerClientBackend backend || backend.iQuestSession is not ClientBackendSession session)
                                throw new InvalidOperationException("Native Q4 inventory session missing");
                            LateInventoryApply.Apply(session, qc.Profile.Id, effect); deleted.Add(key);
                        }
                    }
                    Mirror(qc, data);
                    if (data["personalMailAvailable"]?.Value<bool>() == true &&
                        (action == "recoverPersonal" || !wasRead && VisitFlow.Has(qc, "q4RequestRead")))
                        NotificationManager.DisplayMessageNotification(E17Text.Get("t9f9ea4ae7a70949c"));
                    ChapterEvents.Raise(); VisitFlow.RefreshParcelChoices(); return true;
                }, "E17 Q4 document action", _ => { }, attempts: 2, delay: 0.5f);
            }
        }
        finally { Busy = false; }
    }
}
