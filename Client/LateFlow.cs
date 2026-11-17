using System;
using System.Collections.Generic;
using System.Linq;
using EFT.Quests;
using Newtonsoft.Json.Linq;
using VisitAPI.Dialog;
using VisitAPI.Native;
namespace CustomQuests.E17;

internal static class LateFlow
{
    static readonly Dictionary<DialogOption, JObject> bound = new();
    internal static void Clear() { bound.Clear(); }
    internal static void Bind(string node, int index, DialogOption option)
    {
        var definition = LatePolicy.Option(node, index);
        if (definition != null) bound[option] = definition;
    }
    internal static JObject Definition(DialogOption option) => bound.TryGetValue(option, out var d) ? d : null;
    internal static Quest Quest(QuestController qc, string key) => qc?.Quests?.GetConditional(LatePolicy.Id(key));
    internal static bool Active(Quest q) => q?.QuestStatus is EQuestStatus.Started or EQuestStatus.AvailableForFinish;
    internal static bool Atom(QuestController qc, string atom)
    {
        if (qc?.Profile == null || string.IsNullOrEmpty(atom)) return false;
        var fields = atom.Split(new[] { ':' }, 3);
        if (fields.Length < 2) return false;
        var key = fields[1];
        switch (fields[0])
        {
            case "fact": return VisitFlow.Has(qc, key);
            case "quest":
                var q = Quest(qc, key); var value = fields.Length > 2 ? fields[2] : "";
                return value == "Active" ? Active(q) : value == "Available" ? q?.QuestStatus == EQuestStatus.AvailableForStart :
                    Enum.TryParse<EQuestStatus>(value, out var status) && q?.QuestStatus == status;
            case "found":
                var owner = key.Split('/')[0]; var foundQuest = Quest(qc, owner);
                return Plugin.Found(foundQuest, LatePolicy.Id(key), qc.Profile);
            case "given":
                return LatePolicy.Quests.Any(d => VisitFlow.Given(Quest(qc, (string)d["key"]), LatePolicy.Id(key)));
            case "held": return VisitFlow.Held(qc, LatePolicy.Id(key));
            case "planted":
                var plantQuest = Quest(qc, key.Split('/')[0]);
                return plantQuest?.GetConditions<ConditionLeaveItemAtLocation>(EQuestStatus.AvailableForFinish)
                    .Any(c => c.id == LatePolicy.Id(key) && plantQuest.IsConditionDone(c)) == true;
            case "choice7": return (string)LateSync.State(qc)?["choice7"] == key ||
                !QuestOps.IsLobby(qc) && VisitFlow.Has(qc, "server_choice7_" + key);
            case "keeper": return (string)LateSync.State(qc)?["keeper"] == key ||
                !QuestOps.IsLobby(qc) && VisitFlow.Has(qc, "server_keeper_" + key);
            case "server":
                var server = LateSync.State(qc);
                var flags = server?["serverFacts"] ?? server?["flags"];
                if (flags != null) return flags is JArray list ? list.Values<string>().Contains(key) : flags[key]?.Value<bool>() == true;
                return !QuestOps.IsLobby(qc) && VisitFlow.Has(qc, "server_" + key);
            default: return false;
        }
    }
    internal static bool Test(QuestController qc, JToken guard) => LatePolicy.Test(guard, atom => Atom(qc, atom));
    internal static bool Allowed(DialogOption option, QuestController qc)
    {
        var d = Definition(option);
        if (d != null && !Test(qc, d["when"])) return false;
        var node = LatePolicy.Node(option.Target);
        var changesState = option.AcceptIds.Count > 0 || option.CompleteIds.Count > 0 ||
            !string.IsNullOrEmpty(option.HandoverId) || d?["action"] != null;
        // Destination guards describe the response after a confirmed action, not its prerequisites.
        if (!changesState && node != null && !Test(qc, node["when"])) return false;
        // Generated topic menu entries have no authored option metadata.
        if (d == null && node != null)
        {
            var owner = LatePolicy.Quest((string)node["quest"]);
            if (owner != null && (string)owner["entry"] == option.Target)
            {
                var q = Quest(qc, (string)owner["key"]);
                if (q == null || !(Active(q) || q.QuestStatus == EQuestStatus.AvailableForStart)) return false;
            }
        }
        foreach (var id in option.AcceptIds)
        {
            var q = LatePolicy.Quest(id);
            if (q != null && !Test(qc, q["acceptWhen"])) return false;
        }
        foreach (var id in option.CompleteIds)
        {
            var q = LatePolicy.Quest(id);
            // This confirmation first records settlement intent; completion runs only after that RPC succeeds.
            if (d?["action"] != null) continue;
            if (q != null && !Test(qc, q["finishWhen"])) return false;
        }
        return true;
    }
    internal static bool Shown(string name, QuestController qc)
    {
        var node = LatePolicy.Node(name);
        if (node == null || !Test(qc, node["when"])) return false;
        var changed = false;
        foreach (var fact in (node["shown"] ?? new JArray()).Values<string>())
            changed |= VisitFlow.Mark(qc, fact);
        return changed;
    }
    internal static string Entry(QuestController qc, string trader, string requested = null)
    {
        var candidates = LatePolicy.Quests.Where(q => requested == null || (string)q["key"] == requested).Reverse();
        foreach (var d in candidates)
        {
            var q = Quest(qc, (string)d["key"]);
            if (q == null || q.QuestStatus is not (EQuestStatus.AvailableForStart or EQuestStatus.Started or EQuestStatus.AvailableForFinish or EQuestStatus.Success)) continue;
            foreach (var route in (d["routes"] ?? new JArray()).OfType<JObject>())
                if (Test(qc, route["when"]) && LatePolicy.Trader((string)LatePolicy.Node((string)route["node"])?["trader"]) == trader)
                    return (string)route["node"];
            if (q.QuestStatus != EQuestStatus.Success && LatePolicy.Trader((string)d["trader"]) == trader) return (string)d["entry"];
        }
        return null;
    }
    internal static string Objective(QuestController qc)
    {
        foreach (var d in LatePolicy.Quests)
        {
            var q = Quest(qc, (string)d["key"]);
            if (q == null || q.QuestStatus is not (EQuestStatus.AvailableForStart or EQuestStatus.Started or EQuestStatus.AvailableForFinish)) continue;
            return string.Join("\n", (d["objectives"] ?? new JArray()).OfType<JObject>()
                .Where(x => Test(qc, x["when"])).Select(x => LatePolicy.Text(x["text"])));
        }
        return "";
    }
    internal static IEnumerable<(string key, string text, Quest quest)> Journal(QuestController qc)
    {
        foreach (var d in LatePolicy.Quests)
        {
            var q = Quest(qc, (string)d["key"]);
            if (q == null) continue;
            foreach (var row in LatePolicy.JournalRows(d, (int)q.QuestStatus, atom => Atom(qc, atom)))
                yield return ((string)row["key"], LatePolicy.Text(row["text"]), q);
        }
    }
    internal static string HandoverItem(DialogOption o) => (string)Definition(o)?["item"];
}
