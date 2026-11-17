using System.Collections.Generic;
using System.Linq;
using VisitAPI.Dialog;

namespace CustomQuests.E17;

internal static class DialogueText
{
    // VisitAPI's line-oriented DSL preserves backslash escapes verbatim. Decode our
    // authored line breaks after parsing, before both native text and history are built.
    internal static void Prepare(DialogTree tree)
    {
        if (tree == null || !LatePolicy.Traders.Values.Contains(tree.TraderId) || !tree.Nodes.ContainsKey("e17_root")) return;
        foreach (var node in tree.Nodes.Values)
        {
            node.NpcText = Decode(node.NpcText);
            Translate(node.NpcTr);
            foreach (var option in node.Options)
            {
                option.Text = Decode(option.Text);
                Translate(option.Tr);
            }
            foreach (var line in node.Narration)
            {
                line.Text = Decode(line.Text);
                Translate(line.Tr);
            }
        }
    }

    static string Decode(string text) => text?.Replace("\\r\\n", "\n").Replace("\\n", "\n")
        .Replace(/* release-text:Client.DialogueText.2 */ "", "{playerName}").Replace(/* release-text:Client.DialogueText.1 */ "", "{playerName}").Replace(/* release-text:Client.DialogueText.0 */ "", "{playerName}");
    static void Translate(Dictionary<string, string> translations)
    {
        foreach (var key in translations.Keys.ToArray()) translations[key] = Decode(translations[key]);
    }
}
