using System;
using EFT.Dialogs;
using EFT.Quests;
using EFT.UI;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VisitAPI.ChapterUI;
using VisitAPI.Dialog;
using VisitAPI.Native;

namespace CustomQuests.E17;

// The approved q5 approach illustration is visible before acceptance and can be reopened in STORY.
internal static class LateNavigationImage
{
    const string ImageUrl = "/files/quest/icon/customquests_e17_q5_navigation.png";
    static string Caption => LatePolicy.SystemText(/* release-text:Client.LateNavigationImage.2 */ "",
        /* release-text:Client.LateNavigationImage.1 */ "", /* release-text:Client.LateNavigationImage.0 */ "");
    static Image Make(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>(); image.raycastTarget = false; image.preserveAspect = true;
        image.color = Color.white; image.enabled = false;
        return image;
    }
    [HarmonyPatch(typeof(MainQuestTabView), "FillNotes")]
    static class JournalIllustration
    {
        static void Postfix(MainQuestTabView __instance, ChapterModel ch)
        {
            var parent = __instance._historyView?._container;
            if (parent == null) return;
            var old = parent.Find("E17Q5Navigation");
            var qc = QuestOps.Resolve();
            var visible = ch?.Quest?.Id == Plugin.ChapterId && Q4Flow.Quest(qc)?.QuestStatus == EQuestStatus.Success;
            if (!visible) { if (old != null) old.gameObject.SetActive(false); return; }
            var image = old != null ? old.GetComponent<Image>() : Make(parent, "E17Q5Navigation");
            image.gameObject.SetActive(true);
            image.GetComponent<LayoutElement>().preferredHeight = 360;
            image.transform.SetAsLastSibling();
            ChapterImages.Apply(image, ImageUrl);
        }
    }
    [HarmonyPatch(typeof(TraderDialogWindow), nameof(TraderDialogWindow.Redraw))]
    static class OfferIllustration
    {
        static void Postfix(TraderDialogWindow __instance, BaseTraderDialog dialog, bool __runOriginal)
        {
            if (!__runOriginal) return;
            var existing = __instance.transform.Find("E17Q5Approach");
            var show = dialog != null && DialogTemplateBuilder.NodeByDialog.TryGetValue(dialog.Id, out var node) && node == "e17_q5_offer";
            if (!show) { if (existing != null) existing.gameObject.SetActive(false); return; }
            var image = existing != null ? existing.GetComponent<Image>() : Make(__instance.transform, "E17Q5Approach");
            image.gameObject.SetActive(true);
            image.GetComponent<LayoutElement>().ignoreLayout = true;
            var rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f); rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0, 30);
            var canvas = __instance.GetComponentInParent<Canvas>();
            var canvasRect = canvas?.GetComponent<RectTransform>();
            var width = canvasRect?.rect.width ?? 1280f;
            var height = canvasRect?.rect.height ?? 720f;
            rect.sizeDelta = new Vector2(Math.Min(900f, width * 0.65f), Math.Min(280f, height * 0.3f));
            ChapterImages.Apply(image, ImageUrl);
            var label = image.transform.Find("Caption")?.GetComponent<TextMeshProUGUI>();
            if (label == null)
            {
                var go = new GameObject("Caption", typeof(RectTransform), typeof(TextMeshProUGUI));
                go.transform.SetParent(image.transform, false); label = go.GetComponent<TextMeshProUGUI>();
                var sample = __instance.GetComponentInChildren<TextMeshProUGUI>(true);
                if (sample != null) label.font = sample.font;
                label.fontSize = 16; label.raycastTarget = false; label.alignment = TextAlignmentOptions.Center;
                label.rectTransform.anchorMin = new Vector2(0, 0); label.rectTransform.anchorMax = new Vector2(1, 0);
                label.rectTransform.pivot = new Vector2(0.5f, 1); label.rectTransform.sizeDelta = new Vector2(0, 28);
            }
            label.text = Caption;
        }
    }
}
