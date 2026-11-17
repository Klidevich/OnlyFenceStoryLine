using System;
using System.Collections;
using EFT.Quests;
using Newtonsoft.Json.Linq;
using UnityEngine;
using VisitAPI.ChapterUI;
using VisitAPI.Native;

namespace CustomQuests.E17;

// The local chat cache may omit uncollected letters. Ask the same server policy that issues replacements.
internal static class Q4RequestSync
{
    static QuestController owner;
    static Coroutine pending;
    static ParcelAvailability value;
    static float retryAt, expiresAt;
    static int generation;

    internal static ParcelAvailability State(QuestController qc) => ReferenceEquals(qc, owner) &&
        Time.unscaledTime < expiresAt ? value : ParcelAvailability.Unknown;

    internal static void Tick(QuestController qc, bool needsParcel)
    {
        if (!QuestOps.IsLobby(qc) || !QuestOps.IsLive(qc) || !needsParcel) { Dispose(); return; }
        if (!ReferenceEquals(owner, qc)) { Dispose(); owner = qc; }
        if (value != ParcelAvailability.Unknown && Time.unscaledTime >= expiresAt)
        { value = ParcelAvailability.Unknown; Notify(); }
        if (pending == null && Time.unscaledTime >= retryAt) Start(qc, false);
    }

    internal static void Replace(QuestController qc)
    {
        if (State(qc) != ParcelAvailability.Missing) return;
        Start(qc, true);
    }

    static void Start(QuestController qc, bool replace)
    {
        generation++;
        if (pending != null) Plugin.Instance.StopCoroutine(pending);
        pending = null;
        if (replace) { value = ParcelAvailability.Unknown; expiresAt = 0f; }
        pending = Plugin.Instance.StartCoroutine(Request(qc, generation, replace));
    }

    static IEnumerator Request(QuestController captured, int version, bool replace)
    {
        var accepted = false;
        var next = ParcelAvailability.Unknown;
        IEnumerator call = null;
        try
        {
            // Do not rebuild a clicked option while ExecuteLine is still on the stack.
            yield return null;
            if (version != generation || !ReferenceEquals(owner, captured) ||
                !QuestOps.IsLobby(captured) || !QuestOps.IsLive(captured)) yield break;
            if (replace) Notify();
            var body = new JObject { ["profileId"] = captured.Profile.Id }.ToString();
            call = VisitHttp.Call("/customquests/e17/q4/" + (replace ? "issue" : "status"), body, raw =>
            {
                try
                {
                    var data = JObject.Parse(raw)["data"];
                    if ((string)data?["profileId"] != captured.Profile.Id) return false;
                    Q4ActionSync.Mirror(captured, data);
                    if (data?["repliesReady"]?.Value<bool>() == true && !VisitFlow.Has(captured, "q4ReplyReady"))
                    {
                        // Server-owned raid proof: never send this through ProgressSync.
                        captured.Profile.ProfileVariables.SetVariableValue(Vars.Id("cq_e17_v1_q4ReplyReady"), 1);
                        Notify();
                    }
                    switch ((string)data["status"])
                    {
                        case "Issue": next = replace ? ParcelAvailability.PendingMail : ParcelAvailability.Missing; break;
                        case "PendingMail": next = ParcelAvailability.PendingMail; break;
                        case "Owned": next = ParcelAvailability.Owned; break;
                        case "NotActive": case "Placed": case "MissingProfile": case "AwaitingClaim": next = ParcelAvailability.Unavailable; break;
                        default: return false;
                    }
                    return true;
                }
                catch { return false; }
            }, "E17 q4 request status", ok => accepted = ok, attempts: replace ? 2 : 1, delay: 0f);
            var deadline = Time.unscaledTime + 12f;
            while (version == generation && Time.unscaledTime < deadline)
            {
                bool more;
                try { more = call.MoveNext(); }
                catch (Exception e) { Plugin.Warn("E17 q4 request interrupted: " + e.Message); break; }
                if (!more) break;
                yield return null;
            }
        }
        finally
        {
            try { (call as IDisposable)?.Dispose(); }
            catch (Exception e) { Plugin.Warn("E17 q4 request cleanup: " + e.Message); }
            if (version == generation && ReferenceEquals(owner, captured))
            {
                pending = null;
                retryAt = Time.unscaledTime + 5f;
                var updated = accepted ? next : ParcelAvailability.Unknown;
                var changed = value != updated;
                value = updated; expiresAt = Time.unscaledTime + 10f;
                if (changed) Notify();
            }
        }
    }

    static void Notify()
    {
        try { ChapterEvents.Raise(); VisitFlow.RefreshParcelChoices(); }
        catch (Exception e) { Plugin.Warn("E17 q4 request UI refresh: " + e.Message); }
    }

    internal static void Dispose()
    {
        generation++;
        if (pending != null && Plugin.Instance != null) Plugin.Instance.StopCoroutine(pending);
        pending = null; owner = null; value = ParcelAvailability.Unknown;
        retryAt = expiresAt = 0f;
    }
}
