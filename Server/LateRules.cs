using System.Text.Json;

namespace CustomQuests.E17.Server;

/// <summary>Guards authored on native quest/dialogue definitions. Unknown data fails closed.</summary>
public static class LateRules
{
    static readonly Lazy<JsonElement[]> Documents = new(() =>
    {
        var asm = typeof(LateRules).Assembly;
        return asm.GetManifestResourceNames().Where(n => n.EndsWith("late-q05-q13.json", StringComparison.Ordinal) || n.EndsWith("late-q14-q22.json", StringComparison.Ordinal))
            .Select(n => { using var stream = asm.GetManifestResourceStream(n)!; using var doc = JsonDocument.Parse(stream); return doc.RootElement.Clone(); }).ToArray();
    });
    public static bool Guard(JsonElement guard, ILateNativeState n, LateLedger s)
    {
        if (guard.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return true;
        if (guard.ValueKind != JsonValueKind.Object) return false;
        foreach (var p in guard.EnumerateObject())
        {
            if (p.Value.ValueKind != JsonValueKind.Array) return false;
            var values = p.Value.EnumerateArray().ToArray();
            if (p.Name == "or") { if (!values.Any(v => Guard(v, n, s))) return false; }
            else if (p.Name is "all" or "any" or "none")
            {
                if (values.Any(v => v.ValueKind != JsonValueKind.String)) return false;
                var atoms = values.Select(v => Atom(v.GetString()!, n, s)).ToArray();
                if (atoms.Any(x => x == null)) return false;
                if (p.Name == "all" && atoms.Any(x => x != true) || p.Name == "any" && atoms.Length > 0 && !atoms.Any(x => x == true) || p.Name == "none" && atoms.Any(x => x == true)) return false;
            }
            else return false;
        }
        return true;
    }
    static bool? Atom(string atom, ILateNativeState n, LateLedger s)
    {
        var colon = atom.IndexOf(':'); if (colon <= 0) return null;
        var kind = atom[..colon]; var key = atom[(colon + 1)..];
        switch (kind)
        {
            case "quest":
                var parts = key.Split(':'); if (parts.Length != 2) return null;
                return parts[1] switch { "Success" => n.Success(parts[0]), "Active" => n.Active(parts[0]), "Started" => n.Status(parts[0]) == 2,
                    "Available" => n.Status(parts[0]) == 1, "AvailableForFinish" => n.Status(parts[0]) == 3, _ => null };
            case "found": case "given":
                var p = key.Split('/'); if (p.Length != 3 || p[1] != "item") return null;
                return n.Done(p[0] + (kind == "found" ? "/find/" : "/give/") + p[2]);
            case "held": return n.Held(key);
            case "planted": return n.Done(key);
            case "fact": return n.Fact(key);
            case "server": return LatePolicy.SnapshotFlags(s, n).Contains(key);
            case "choice7": return key is "cash" or "tools" ? s.Choice7 == key : null;
            case "keeper": return key is "Fence" or "Ragman" ? s.Keeper == key : null;
            default: return null;
        }
    }
    public static bool CanFinish(string key, ILateNativeState n, LateLedger s)
    {
        foreach (var doc in Documents.Value)
            if (doc.TryGetProperty("quests", out var quests))
                foreach (var q in quests.EnumerateArray())
                    if (q.GetProperty("key").GetString() == key)
                        return q.TryGetProperty("finishWhen", out var guard) && Guard(guard, n, s);
        return false;
    }
    public static bool CanAction(LateCommand command, ILateNativeState n, LateLedger s)
    {
        foreach (var doc in Documents.Value)
            if (doc.TryGetProperty("dialogue", out var nodes))
                foreach (var node in nodes.EnumerateArray())
                    if (node.TryGetProperty("options", out var options))
                        foreach (var option in options.EnumerateArray())
                        {
                            if (!option.TryGetProperty("action", out var action) || action.ValueKind != JsonValueKind.Object ||
                                !action.TryGetProperty("action", out var name) || name.GetString() != command.Action ||
                                !action.TryGetProperty("subject", out var subject) || subject.GetString() != command.Subject) continue;
                            if (action.TryGetProperty("choice", out var choice) && choice.GetString() != command.Choice) continue;
                            var nodeGuard = node.TryGetProperty("when", out var ng) ? ng : default;
                            var optionGuard = option.TryGetProperty("when", out var og) ? og : default;
                            if (Guard(nodeGuard, n, s) && Guard(optionGuard, n, s)) return true;
                        }
        return false;
    }
}
