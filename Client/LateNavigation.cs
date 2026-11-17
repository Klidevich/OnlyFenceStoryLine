using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using EFT;
using EFT.Quests;
using EFT.UI;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;
using VisitAPI.Dialog;
using VisitAPI.Native;
namespace CustomQuests.E17;

// Reuse native STORY controls and VisitAPI's room lifecycle for each authored trader.
internal static class LateNavigation
{
    static DialogLoader loader;
    static readonly MethodInfo OpenAtScene = AccessTools.DeclaredMethod(typeof(DialogOpener), "TryOpen", new[]
    {
        typeof(DialogTree), typeof(Profile), typeof(QuestController), typeof(EFT.InventoryLogic.InventoryController),
        typeof(TraderScreensGroup), typeof(string), typeof(bool), typeof(string).MakeByRefType(), typeof(bool)
    });
    internal static bool Accessible(QuestController qc, string trader) => trader switch
    {
        "Therapist" => LateFlow.Quest(qc, "q14")?.QuestStatus == EQuestStatus.Success,
        "Prapor" => LateFlow.Quest(qc, "q17")?.QuestStatus == EQuestStatus.Success,
        "Ragman" => Q4Flow.Quest(qc)?.QuestStatus == EQuestStatus.Success,
        "Fence" => true,
        _ => false
    };
    internal static void Open(QuestController qc, string trader, string node = null)
    {
        if (!QuestOps.IsLobby(qc) || !QuestOps.IsLive(qc) || !Accessible(qc, trader)) return;
        Plugin.Instance.StartCoroutine(OpenRoutine(qc, trader, node));
    }
    static IEnumerator OpenRoutine(QuestController qc, string trader, string node)
    {
        var id = LatePolicy.Trader(trader);
        var scene = true;
        if (scene)
        {
            var roomDir = Path.Combine(BepInEx.Paths.PluginPath, "VisitAPI", "rooms");
            if (!Directory.Exists(roomDir) || !Directory.EnumerateFiles(roomDir, id + "*").Any())
            {
                var message = LatePolicy.SystemText(/* release-text:Client.LateNavigation.5 */ "",
                    /* release-text:Client.LateNavigation.4 */ "", /* release-text:Client.LateNavigation.3 */ "");
                Plugin.Warn(message + " Trader: " + id);
                EFT.Communications.NotificationManager.DisplayMessageNotification(message);
                yield break;
            }
        }
        // VisitLoading owns the cover and waits for the old room to finish unloading.
        loader ??= new DialogLoader(Path.Combine(BepInEx.Paths.ConfigPath, "VisitAPI"));
        var next = loader.Load(id);
        if (next == null) { Plugin.Warn("E17 dialogue missing for " + id); yield break; }
        node ??= LateFlow.Entry(qc, id) ?? "e17_root";
        if (!next.Nodes.ContainsKey(node)) { Plugin.Warn("E17 dialogue node missing: " + node); yield break; }
        var arguments = new object[] { next, qc.Profile, qc, qc.InventoryController, null, node, scene, null, false };
        if (!(bool)OpenAtScene.Invoke(null, arguments)) Plugin.Warn("E17 story visit failed: " + arguments[7]);
        yield break;
    }
    internal static void BindButton(QuestController qc, Transform parent, DefaultUIButton template, bool isChapter)
    {
        var old = parent.Find("E17StoryTalk");
        var definition = isChapter ? LatePolicy.Quests.FirstOrDefault(d =>
            LateFlow.Quest(qc, (string)d["key"])?.QuestStatus is EQuestStatus.AvailableForStart or EQuestStatus.Started or EQuestStatus.AvailableForFinish) : null;
        if (definition == null || !QuestOps.IsLobby(qc)) { if (old != null) old.gameObject.SetActive(false); return; }
        if (template == null) return;
        var button = old != null ? old.GetComponent<DefaultUIButton>() : UnityEngine.Object.Instantiate(template, parent);
        button.name = "E17StoryTalk";
        button.gameObject.SetActive(true);
        var layout = button.GetComponent<LayoutElement>() ?? button.gameObject.AddComponent<LayoutElement>();
        layout.minHeight = 36; layout.preferredHeight = 36;
        button.SetRawText(LatePolicy.SystemText(/* release-text:Client.LateNavigation.2 */ "", /* release-text:Client.LateNavigation.1 */ "", /* release-text:Client.LateNavigation.0 */ ""), 14);
        button.OnClick.RemoveAllListeners();
        button.OnClick.AddListener(() =>
        {
            var route = (definition["routes"] as JArray ?? new JArray()).OfType<JObject>().FirstOrDefault(r => LateFlow.Test(qc, r["when"]));
            var entry = (string)route?["node"] ?? (string)definition["entry"];
            var trader = (string)LatePolicy.Node(entry)?["trader"] ?? (string)definition["trader"];
            Open(qc, trader, entry);
        });
        button.transform.SetAsFirstSibling();
    }
}
