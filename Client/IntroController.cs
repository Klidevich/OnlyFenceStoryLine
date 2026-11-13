using System;
using System.Collections;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.InputSystem;
using EFT.Quests;
using EFT.UI;
using EFT.UI.Screens;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VisitAPI.Native;

namespace CustomQuests.E17;

internal static class IntroController
{
    static QuestController session;
    static float? resourcesAt;
    static bool menuReached, requested, resolved, eligible, used;
    static IntroScreen screen;
    static Coroutine request;

    internal static bool MainMenuVisible => CommonUI.Instance?.MenuScreen != null &&
        CommonUI.Instance.MenuScreen.gameObject.activeInHierarchy &&
        EftScreenManager.Instance?.CheckCurrentScreen(EEftScreenType.MainMenu) == true;

    // Playback has no artificial delay. The chapter's independent five-second timer keeps ticking.
    // Waiting for the saved playback status never blocks menu input.
    internal static bool Tick(QuestController qc, bool menuVisible)
    {
        if (!ReferenceEquals(session, qc))
        {
            Dispose(); session = qc; resourcesAt = null;
            menuReached = requested = resolved = eligible = used = false;
        }
        if (!QuestOps.IsLobby(qc) || !QuestOps.IsLive(qc)) return false;
        menuVisible &= MainMenuVisible;
        menuReached |= menuVisible;
        if (!menuReached) return false;
        if (!requested)
        {
            requested = true;
            request = Plugin.Instance.StartCoroutine(Status(qc));
        }
        if (!resolved) return true;
        if (!eligible || used) return screen != null;
        if (!menuVisible || ModalActive()) return true;
        var text = VisitAPI.Loc.Key(IntroPolicy.JournalKey);
        var pages = IntroPolicy.Pages(text);
        if (pages.Length == 0 || text == IntroPolicy.JournalKey)
        {
            Plugin.Warn("E17 intro: localized journal unavailable; leaving menu usable."); used = true; return false;
        }
        try
        {
            if (!IntroScreen.TryResources(out var font, out var input))
            {
                if (!resourcesAt.HasValue) resourcesAt = Time.unscaledTime;
                if (Time.unscaledTime - resourcesAt.Value < 15f) return true;
                Plugin.Warn("E17 intro resources unavailable: menu font=" + (font != null) + ", application InputTree=" + (input != null) + "; leaving menu usable.");
                used = true; return false;
            }
            var go = new GameObject("E17Intro", typeof(RectTransform));
            screen = go.AddComponent<IntroScreen>();
            screen.Begin(pages, qc, font, input, Complete);
            used = true;
        }
        catch (Exception e) { Plugin.Warn("E17 intro could not open: " + e); Dispose(); used = true; }
        return screen != null;
    }

    internal static bool ModalActive()
    {
        var context = ItemUiContext.Instance;
        if (context == null || context.MessageWindowActive || context.ScrolledMessageWindowActive) return true;
        var preloader = MonoBehaviourSingleton<PreloaderUI>.Instantiated ? MonoBehaviourSingleton<PreloaderUI>.Instance : null;
        return preloader != null && ((preloader.ErrorScreen != null && preloader.ErrorScreen.gameObject.activeInHierarchy) ||
            (preloader.UnknownErrorScreen != null && preloader.UnknownErrorScreen.gameObject.activeInHierarchy) ||
            (preloader.ErrorScreenInputNode != null && preloader.ErrorScreenInputNode.transform.childCount > 0 &&
                preloader.ErrorScreenInputNode.GetComponentsInChildren<ErrorScreen>().Any(s => s.gameObject.activeInHierarchy)));
    }

    static IEnumerator Status(QuestController captured)
    {
        bool success = false, allowed = false;
        for (var attempt = 0; attempt < 3 && !success && ReferenceEquals(session, captured); attempt++)
        {
            yield return BoundedCall("/customquests/e17/intro/status", "{}", data =>
            {
                if ((string)data["profileId"] != captured.Profile.Id || data["eligible"]?.Type != JTokenType.Boolean) return false;
                allowed = (bool)data["eligible"]; return true;
            }, ok => success = ok);
            if (!success && attempt < 2) yield return new WaitForSecondsRealtime(3f);
        }
        if (!ReferenceEquals(session, captured)) yield break;
        eligible = success && allowed; resolved = true; request = null;
        if (!success) Plugin.Warn("E17 intro status unavailable; automatic introduction deferred until next session.");
    }

    static IEnumerator BoundedCall(string route, string body, Func<JObject, bool> parse, Action<bool> done)
    {
        var success = false;
        var call = VisitHttp.Call(route, body, raw =>
        {
            try { return JObject.Parse(raw)["data"] is JObject data && parse(data); }
            catch { return false; }
        }, "E17 intro", ok => success = ok, attempts: 1, delay: 0f);
        var deadline = Time.unscaledTime + 12f;
        try
        {
            while (Time.unscaledTime < deadline)
            {
                bool pending;
                try { pending = call.MoveNext(); }
                catch (Exception error)
                {
                    success = false;
                    Plugin.Warn("E17 intro request interrupted: " + error.Message);
                    break;
                }
                if (!pending) break;
                yield return null;
            }
        }
        finally
        {
            try { (call as IDisposable)?.Dispose(); }
            catch (Exception error) { Plugin.Warn("E17 intro request cleanup: " + error.Message); }
        }
        done(success);
    }

    static void Complete(QuestController captured, bool skipped)
    {
        screen = null;
        if (ReferenceEquals(session, captured)) request = Plugin.Instance.StartCoroutine(Save(captured, skipped));
    }

    static IEnumerator Save(QuestController captured, bool skipped)
    {
        for (var attempt = 0; attempt < 3 && ReferenceEquals(session, captured); attempt++)
        {
            var saved = false;
            var body = new JObject { ["profileId"] = captured.Profile.Id, ["skipped"] = skipped }.ToString();
            yield return BoundedCall("/customquests/e17/intro/finish", body,
                data => (string)data["profileId"] == captured.Profile.Id && (bool?)data["saved"] == true, ok => saved = ok);
            if (saved) { request = null; yield break; }
            if (attempt < 2) yield return new WaitForSecondsRealtime(3f);
        }
        request = null;
        Plugin.Warn("E17 intro completion could not be saved. Menu remains usable; introduction may repeat after restart.");
    }

    internal static void Dispose()
    {
        if (request != null && Plugin.Instance != null) Plugin.Instance.StopCoroutine(request);
        request = null;
        if (screen != null) { screen.Release(); UnityEngine.Object.Destroy(screen.gameObject); }
        screen = null;
    }
}

// Owns every acquired UI resource. Release is idempotent and runs on all exit paths.
internal sealed class IntroScreen : MonoBehaviour
{
    // The game's InputManager has HideFlags.DontSave, so Object.FindObjectOfType misses it.
    // ClientApplication.Init owns the exact InputTree that translates menu input in 40743.
    static readonly System.Reflection.FieldInfo ApplicationInput = AccessTools.Field(typeof(ClientApplication<IEftSession>), "_inputTree");
    QuestController session;
    Action<QuestController, bool> completed;
    InputTree input;
    IntroInputBlocker blocker;
    EventSystem events;
    GameObject selection;
    bool navigation, cursorVisible, cursorCaptured, released, closing, skipped;
    CursorLockMode cursorLock;
    CanvasGroup background, content;
    TextMeshProUGUI label, hint;
    string[] pages;
    int page;
    float start, pageAt, endAt;

    internal static bool TryResources(out TMP_FontAsset font, out InputTree input)
    {
        font = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault(f => f != null && f.name == "Jovanny Lemonad - Bender Normal SDF")
            ?? CommonUI.Instance?.MenuScreen?.GetComponentsInChildren<TextMeshProUGUI>(true).Select(t => t.font).FirstOrDefault(f => f != null);
        input = Singleton<ClientApplication<IEftSession>>.Instantiated
            ? ApplicationInput?.GetValue(Singleton<ClientApplication<IEftSession>>.Instance) as InputTree : null;
        return font != null && input != null && input.isActiveAndEnabled;
    }

    internal void Begin(string[] paragraphs, QuestController qc, TMP_FontAsset font, InputTree menuInput, Action<QuestController, bool> done)
    {
        session = qc; completed = done; pages = paragraphs; input = menuInput;
        var canvas = gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 30000;
        var scaler = gameObject.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        background = gameObject.AddComponent<CanvasGroup>(); background.alpha = 0;
        var shade = new GameObject("Background", typeof(RectTransform), typeof(Image)); shade.transform.SetParent(transform, false);
        Stretch((RectTransform)shade.transform, Vector2.zero, Vector2.one);
        shade.GetComponent<Image>().color = new Color(0.015f, 0.017f, 0.018f, 1f);
        var block = new GameObject("Reading", typeof(RectTransform), typeof(CanvasGroup)); block.transform.SetParent(transform, false);
        Stretch((RectTransform)block.transform, new Vector2(.14f, .22f), new Vector2(.86f, .78f));
        content = block.GetComponent<CanvasGroup>(); content.alpha = 0f;
        label = Text(block.transform, "Text", font, 30f, TextAlignmentOptions.MidlineLeft);
        label.enableAutoSizing = true; label.fontSizeMin = 20f; label.fontSizeMax = 30f;
        label.text = pages[0];
        var help = new GameObject("Controls", typeof(RectTransform)); help.transform.SetParent(transform, false);
        Stretch((RectTransform)help.transform, new Vector2(.14f, .06f), new Vector2(.86f, .14f));
        hint = Text(help.transform, "Hint", font, 20f, TextAlignmentOptions.BottomRight);
        Hint();
        events = EventSystem.current;
        if (events != null) { selection = events.currentSelectedGameObject; navigation = events.sendNavigationEvents; events.SetSelectedGameObject(null); events.sendNavigationEvents = false; }
        cursorVisible = Cursor.visible; cursorLock = Cursor.lockState;
        cursorCaptured = true;
        blocker = gameObject.AddComponent<IntroInputBlocker>(); input.Add(blocker);
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        start = pageAt = Time.unscaledTime;
    }

    static TextMeshProUGUI Text(Transform parent, string name, TMP_FontAsset font, float size, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)); go.transform.SetParent(parent, false);
        Stretch((RectTransform)go.transform, Vector2.zero, Vector2.one);
        var t = go.GetComponent<TextMeshProUGUI>(); t.font = font; t.fontSize = size; t.alignment = alignment;
        t.color = new Color(.85f, .84f, .79f); t.enableWordWrapping = true; t.raycastTarget = false; return t;
    }
    static void Stretch(RectTransform rect, Vector2 min, Vector2 max) { rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero; }
    void Hint() => hint.text = (page + 1) + " / " + pages.Length + "     " + E17Text.Get("tb252a8fcca055b7a");

    void Update()
    {
        try { Frame(); }
        catch (Exception e) { Plugin.Warn("E17 intro interrupted: " + e); Release(); Destroy(gameObject); }
    }
    void Frame()
    {
        if (released) return;
        if (!QuestOps.IsLive(session) || !QuestOps.IsLobby(session) || !ReferenceEquals(QuestOps.Resolve(), session) ||
            CommonUI.Instance?.MenuScreen == null || !CommonUI.Instance.MenuScreen.gameObject.activeInHierarchy ||
            EftScreenManager.Instance?.CheckCurrentScreen(EEftScreenType.MainMenu) != true || IntroController.ModalActive())
        { Release(); Destroy(gameObject); return; }
        if (!Application.isFocused) { start += Time.unscaledDeltaTime; pageAt += Time.unscaledDeltaTime; endAt += Time.unscaledDeltaTime; return; }
        var now = Time.unscaledTime;
        if (closing)
        {
            background.alpha = Mathf.Clamp01(1f - (now - endAt) / IntroPolicy.FadeSeconds);
            if (background.alpha <= 0) { Release(); Destroy(gameObject); completed?.Invoke(session, skipped); }
            return;
        }
        if (Input.GetKeyDown(KeyCode.Escape)) { Close(true); return; }
        background.alpha = Mathf.Clamp01((now - start) / IntroPolicy.FadeSeconds);
        var elapsed = now - pageAt;
        var reading = IntroPolicy.ReadingSeconds(pages[page]);
        // Space starts the same fade-out as automatic playback; repeated presses cannot skip it.
        if (Input.GetKeyDown(KeyCode.Space) && elapsed >= IntroPolicy.FadeSeconds && elapsed < reading + IntroPolicy.FadeSeconds)
        {
            pageAt = now - reading - IntroPolicy.FadeSeconds;
            elapsed = now - pageAt;
        }
        content.alpha = Mathf.Clamp01(Mathf.Min(elapsed / IntroPolicy.FadeSeconds, (reading + 2 * IntroPolicy.FadeSeconds - elapsed) / IntroPolicy.FadeSeconds));
        if (elapsed >= reading + 2 * IntroPolicy.FadeSeconds)
        {
            if (++page >= pages.Length) Close(false);
            else { content.alpha = 0f; label.text = pages[page]; pageAt = now; Hint(); }
        }
    }
    void Close(bool skip) { skipped = skip; closing = true; endAt = Time.unscaledTime; }
    internal void Release()
    {
        if (released) return; released = true;
        if (input != null && blocker != null) input.Remove(blocker);
        if (blocker != null) blocker.enabled = false;
        if (events != null) { events.sendNavigationEvents = navigation; if (selection != null && selection.activeInHierarchy) events.SetSelectedGameObject(selection); }
        if (cursorCaptured) { Cursor.lockState = cursorLock; Cursor.visible = cursorVisible; }
        if (background != null) { background.blocksRaycasts = false; background.interactable = false; }
    }
    void OnDisable() => Release();
    void OnDestroy() => Release();
}

internal sealed class IntroInputBlocker : InputNode
{
    public override ETranslateResult TranslateCommand(ECommand command) => ETranslateResult.BlockAll;
    public override void TranslateAxes(ref float[] axes) { if (axes != null) Array.Clear(axes, 0, axes.Length); }
    public override ECursorResult ShouldLockCursor() => ECursorResult.ShowCursor;
}
