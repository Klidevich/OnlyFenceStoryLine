using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
namespace CustomQuests.E17;

// Presentation rules over native quests. Physical progress and transactions stay in SPT.
internal static class LatePolicy
{
    internal static string Id(string key)
    {
        using var hash = SHA256.Create();
        return string.Concat(hash.ComputeHash(Encoding.UTF8.GetBytes("CustomQuests/E17/v1/" + key))
            .Take(12).Select(b => b.ToString("x2")));
    }
    internal static readonly Dictionary<string, string> Traders = new()
    {
        ["Fence"] = VisitEntryPolicy.FenceId,
        ["Ragman"] = "5ac3b934156ae10c4430e83c",
        ["Therapist"] = "54cb57776803fa99248b456e",
        ["Prapor"] = "54cb50c76803fa8b248b4571"
    };
    static readonly List<JObject> quests = new();
    static readonly Dictionary<string, JObject> nodes = new(StringComparer.Ordinal);
    static readonly Dictionary<string, JObject> items = new(StringComparer.Ordinal);
    internal static IReadOnlyList<JObject> Quests => quests;
    internal static IReadOnlyDictionary<string, JObject> Nodes => nodes;
    internal static IReadOnlyDictionary<string, JObject> Items => items;
    internal static string Trader(string name) => name != null && Traders.TryGetValue(name, out var id) ? id : null;

    internal static void Load()
    {
        quests.Clear(); nodes.Clear(); items.Clear();
        foreach (var name in new[] { "late-q05-q13.json", "late-q14-q22.json" })
        {
            using var stream = typeof(LatePolicy).Assembly.GetManifestResourceStream("CustomQuests.E17." + name)
                ?? throw new InvalidDataException("Missing E17 content resource " + name);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var data = JObject.Parse(reader.ReadToEnd());
            foreach (var q in data["quests"].OfType<JObject>())
            {
                quests.Add(q);
                foreach (var item in (q["items"] ?? new JArray()).OfType<JObject>())
                    items.Add((string)item["key"], item);
            }
            foreach (var node in data["dialogue"].OfType<JObject>()) nodes.Add((string)node["name"], node);
        }
        if (quests.Count != 18 || quests.Select(q => (string)q["key"]).Distinct().Count() != 18)
            throw new InvalidDataException("E17 late quest catalog is incomplete");
        quests.Sort((a, b) => ((int)a["number"]).CompareTo((int)b["number"]));
    }
    internal static bool Test(JToken rule, Func<string, bool> atom)
    {
        if (rule == null || rule.Type == JTokenType.Null) return true;
        if (rule is not JObject obj) throw new InvalidDataException("E17 guard must be an object");
        foreach (var property in obj.Properties())
            if (property.Name is not ("all" or "any" or "none" or "or"))
                throw new InvalidDataException("Unknown E17 guard member: " + property.Name);
        if (obj["all"] is JArray all && !all.All(x => atom((string)x))) return false;
        if (obj["any"] is JArray any && !any.Any(x => atom((string)x))) return false;
        if (obj["none"] is JArray none && none.Any(x => atom((string)x))) return false;
        if (obj["or"] is JArray alternatives && !alternatives.Any(x => Test(x, atom))) return false;
        return true;
    }
    internal static JObject Quest(string keyOrId) => quests.FirstOrDefault(q =>
        (string)q["key"] == keyOrId || Id((string)q["key"]) == keyOrId);
    internal static JObject Node(string name)
    {
        if (name == null) return null;
        var separator = name.IndexOf(':');
        if (separator == 24) name = name.Substring(separator + 1);
        return nodes.TryGetValue(name, out var n) ? n : null;
    }
    internal static JObject Option(string node, int index) => Node(node)?["options"]?.ElementAtOrDefault(index) as JObject;
    internal static IEnumerable<JObject> JournalRows(JObject definition, int status, Func<string, bool> atom)
    {
        // Briefing facts are durable knowledge even if the player leaves before accepting the assignment.
        if (status is not (1 or 2 or 3 or 4)) yield break;
        foreach (var row in (definition["journal"] ?? new JArray()).OfType<JObject>())
            if (Test(row["when"], atom)) yield return row;
    }
    internal static string Text(JToken entry)
    {
        if (entry == null) return "";
        // Register a stable locale key from generated pack data, not the OS language.
        var ru = entry.Type == JTokenType.String ? (string)entry : (string)entry["ru"];
        if (string.IsNullOrWhiteSpace(ru)) return "";
        var identity = entry.Type == JTokenType.String ? ru : ru + "\n" + (string)entry["en"] + "\n" + (string)entry["kr"];
        return FillName(VisitAPI.Loc.Key("customquests.e17.late." + Id("locale/" + identity)) ?? ru);
    }
    internal static string FillName(string text)
    {
        var name = VisitAPI.Native.QuestOps.Resolve()?.Profile?.Nickname ?? "";
        return text?.Replace(/* release-text:Client.LatePolicy.2 */ "", name).Replace(/* release-text:Client.LatePolicy.1 */ "", name).Replace(/* release-text:Client.LatePolicy.0 */ "", name);
    }
    internal static string SystemText(string ru, string en, string kr) => VisitAPI.Loc.Code == "ru" ? ru : VisitAPI.Loc.Code == "kr" ? kr : en;
}
