using System;
using System.Collections.Generic;
using System.Reflection;
using EFT.UI;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VisitAPI.ChapterUI;

namespace CustomQuests.E17;

// Reuses VisitAPI's full-history scroll and reserves the natural height of the objectives.
// No pooled rows, prefab assets or other chapters are changed.
internal sealed class E17ChapterLayout : MonoBehaviour
{
    static readonly FieldInfo FullHistory = AccessTools.Field(typeof(MainQuestTabView), "_fullHistory");
    readonly List<Action> restore = new();
    readonly HashSet<LayoutElement> addedLayouts = new();
    MainQuestTabView view;
    RectTransform chapterInfo;
    ScrollRect tasksScroll;
    LayoutElement tasksSize;
    Vector2 lastSize;
    bool applied, dirty;

    [HarmonyPatch(typeof(MainQuestTabView), "Select")]
    static class Selected
    {
        static void Prefix(MainQuestTabView __instance, ChapterModel ch)
        {
            var layout = __instance.GetComponent<E17ChapterLayout>();
            var ours = ch?.Quest?.Id == Plugin.ChapterId;
            if (ours && layout == null) layout = __instance.gameObject.AddComponent<E17ChapterLayout>();
            if (layout == null) return;
            if (ours) layout.Apply(__instance); else layout.Restore();
        }
    }
    [HarmonyPatch(typeof(MainQuestTabView), "ToggleHistory")]
    static class KeepScrollableHistory
    {
        static bool Prefix(MainQuestTabView __instance) => __instance.GetComponent<E17ChapterLayout>()?.applied != true;
    }
    [HarmonyPatch(typeof(MainQuestTabView), "FillTasks")]
    static class TasksChanged
    {
        static void Postfix(MainQuestTabView __instance) => Invalidate(__instance);
    }
    [HarmonyPatch(typeof(MainQuestTabView), "FillNotes")]
    static class NotesChanged
    {
        static void Postfix(MainQuestTabView __instance) => Invalidate(__instance);
    }
    static void Invalidate(MainQuestTabView view)
    {
        var layout = view.GetComponent<E17ChapterLayout>();
        if (layout != null) layout.dirty = true;
    }

    void Apply(MainQuestTabView owner)
    {
        if (applied) { dirty = true; return; }
        view = owner;
        chapterInfo = view.transform.Find("ChapterInfo") as RectTransform;
        var content = chapterInfo?.Find("ChapterContent") as RectTransform;
        var data = content?.Find("ChapterData");
        var history = view._shortHistoryView?.transform.parent as RectTransform;
        tasksScroll = view._tasksView?.GetComponentInParent<ScrollRect>();
        if (content == null || data == null || history?.parent != content || tasksScroll?.content == null ||
            view._historyView?._scroll?.verticalScrollbar == null) return;
        try
        {
            var caption = view._tasksView._mainTasksList?.transform.Find("MainTasksCaption")?.GetComponent<LocalizedText>();
            if (caption != null)
            {
                var previousKey = caption.LocalizationKey;
                restore.Add(() => { if (caption != null) caption.LocalizationKey = previousKey; });
                // Retain native locale-change subscription; other chapters keep their own key.
                caption.LocalizationKey = "customquests.e17.mainObjectives";
            }
            var oldFull = (bool)FullHistory.GetValue(view);
            restore.Add(() => { if (view != null) FullHistory.SetValue(view, oldFull); });
            FullHistory.SetValue(view, true);
            var historyIndex = history.GetSiblingIndex();
            restore.Add(() => { if (history != null) history.SetSiblingIndex(historyIndex); });
            // Leave background/shadow siblings in their original drawing order.
            if (historyIndex > data.GetSiblingIndex()) history.SetSiblingIndex(data.GetSiblingIndex());
            Hide(view._expandHistoryButton?.gameObject);
            FlexibleHeight(content);
            FlexibleHeight(history);
            FlexibleHeight((RectTransform)view._historyView.transform);
            tasksSize = OverrideLayout((RectTransform)tasksScroll.transform);
            tasksSize.flexibleHeight = 0;
            var scroll = tasksScroll; var enabledBefore = scroll.enabled;
            restore.Add(() => { if (scroll != null) scroll.enabled = enabledBefore; });
            scroll.StopMovement(); scroll.verticalNormalizedPosition = 1f; scroll.enabled = false;
            Hide(scroll.verticalScrollbar?.gameObject);
            Hide(scroll.transform.parent.Find("TopTasksShadow")?.gameObject);
            Hide(scroll.transform.parent.Find("BottomTasksShadow")?.gameObject);
            applied = dirty = true;
        }
        catch (Exception error) { Restore(); Plugin.Warn("E17 chapter layout unavailable: " + error); }
    }

    LayoutElement OverrideLayout(RectTransform target)
    {
        var element = target.GetComponent<LayoutElement>();
        if (element == null)
        {
            element = target.gameObject.AddComponent<LayoutElement>();
            addedLayouts.Add(element);
        }
        if (addedLayouts.Contains(element))
        {
            var added = element;
            // Keep disabled components for reuse when chapters switch within the same frame.
            restore.Add(() => { if (added != null) added.enabled = false; });
        }
        else
        {
            var old = element; var enabledBefore = old.enabled; var priority = old.layoutPriority;
            var min = old.minHeight; var preferred = old.preferredHeight; var flexible = old.flexibleHeight;
            restore.Add(() => { if (old != null) { old.enabled = enabledBefore; old.layoutPriority = priority;
                old.minHeight = min; old.preferredHeight = preferred; old.flexibleHeight = flexible; } });
        }
        element.enabled = true; element.layoutPriority = 100;
        return element;
    }
    void FlexibleHeight(RectTransform target)
    {
        var element = OverrideLayout(target);
        element.minHeight = 0; element.preferredHeight = 0; element.flexibleHeight = 1;
    }
    void Hide(GameObject target)
    {
        if (target == null) return;
        var active = target.activeSelf;
        restore.Add(() => { if (target != null) target.SetActive(active); });
        target.SetActive(false);
    }

    void LateUpdate()
    {
        if (!applied || chapterInfo == null || !view.gameObject.activeInHierarchy) return;
        if (chapterInfo.rect.size != lastSize) { lastSize = chapterInfo.rect.size; dirty = true; }
        if (!dirty) return;
        dirty = false;
        try
        {
            // First lay out widths, then measure wrapped objectives, then reserve their height.
            LayoutRebuilder.ForceRebuildLayoutImmediate(chapterInfo);
            var container = view._tasksView._mainTasksList._conditionsContainer;
            var label = container.Find("E17CurrentObjective")?.GetComponent<TextMeshProUGUI>();
            if (label != null && label.gameObject.activeSelf)
                label.GetComponent<LayoutElement>().preferredHeight = label.GetPreferredValues(label.text,
                    Math.Max(250f, container.rect.width - 24f), float.PositiveInfinity).y + 24f;
            LayoutRebuilder.ForceRebuildLayoutImmediate(tasksScroll.content);
            var height = Mathf.Ceil(LayoutUtility.GetPreferredHeight(tasksScroll.content)) + 2f;
            tasksSize.minHeight = tasksSize.preferredHeight = height;
            LayoutRebuilder.ForceRebuildLayoutImmediate(chapterInfo);
            tasksScroll.StopMovement(); tasksScroll.verticalNormalizedPosition = 1f;
        }
        catch (Exception error) { Restore(); Plugin.Warn("E17 chapter layout reset: " + error); }
    }
    void Restore()
    {
        applied = dirty = false;
        for (var i = restore.Count - 1; i >= 0; i--) restore[i]();
        restore.Clear(); tasksSize = null;
        if (chapterInfo != null) LayoutRebuilder.MarkLayoutForRebuild(chapterInfo);
    }
    void OnDestroy()
    {
        Restore();
        foreach (var added in addedLayouts) if (added != null) Destroy(added);
    }
}
