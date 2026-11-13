using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.Quests;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using VisitAPI.Native;

namespace CustomQuests.E17;

// Monotonic narrative flags: local presentation is immediate, durable save is acknowledged.
// Keep failed writes queued for the same profile; never replay them into a different session.
internal static class ProgressSync
{
    static readonly Dictionary<string, HashSet<string>> Pending = new();
    static bool busy;
    static float retryAt;

    internal static void Queue(QuestController qc, string fact)
    {
        if (qc?.Profile == null) return;
        if (!Pending.TryGetValue(qc.Profile.Id, out var facts))
            Pending[qc.Profile.Id] = facts = new HashSet<string>();
        facts.Add(fact);
    }

    internal static void Tick(QuestController qc)
    {
        if (busy || Time.unscaledTime < retryAt || !QuestOps.IsLobby(qc) || !QuestOps.IsLive(qc) ||
            qc?.Profile == null || !Pending.TryGetValue(qc.Profile.Id, out var facts) || facts.Count == 0) return;
        busy = true;
        Plugin.Instance.StartCoroutine(Save(qc.Profile.Id, facts.ToArray()));
    }

    static IEnumerator Save(string profileId, string[] facts)
    {
        var saved = false;
        // Profile ID is an expected-session guard, never a server-side profile selector.
        var json = JsonConvert.SerializeObject(new { profileId, facts });
        try
        {
            yield return VisitHttp.Call("/customquests/e17/progress/mark", json, body =>
            {
                try { return JObject.Parse(body).SelectToken("data.saved")?.Value<bool>() == true; }
                catch { return false; }
            }, "E17 conversation save", ok => saved = ok, attempts: 2, delay: 1f);
            if (saved && Pending.TryGetValue(profileId, out var pending))
                foreach (var fact in facts) pending.Remove(fact);
        }
        finally
        {
            busy = false;
            retryAt = Time.unscaledTime + (saved ? 0.25f : 5f);
        }
    }
}
