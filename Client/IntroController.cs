using System;
using System.Collections;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.Quests;
using EFT.UI;
using EFT.UI.Screens;
using Newtonsoft.Json.Linq;
using UnityEngine;
using VisitAPI.Native;

namespace CustomQuests.E17;

internal static class IntroController
{
    static QuestController session;
    static bool menuReached, requested, resolved, eligible, used;
    static Coroutine request;

    internal static bool MainMenuVisible => CommonUI.Instance?.MenuScreen != null &&
        CommonUI.Instance.MenuScreen.gameObject.activeInHierarchy &&
        EftScreenManager.Instance?.CheckCurrentScreen(EEftScreenType.MainMenu) == true;

    // A nonmodal invitation follows the independent five-second chapter start.
    // Existing Completed/Skipped markers suppress replay; no journal or input surface is opened.
    internal static bool Tick(QuestController qc, bool menuVisible)
    {
        if (!ReferenceEquals(session, qc))
        {
            Dispose(); session = qc;
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
        var chapterStatus = (int?)qc.Quests?.GetConditional(Plugin.ChapterId)?.QuestStatus ?? 0;
        if (!IntroPolicy.ShouldNotify(resolved, eligible, used, menuVisible, chapterStatus) || ModalActive()) return false;
        var text = E17Text.Get(IntroPolicy.NotificationKey);
        if (string.IsNullOrWhiteSpace(text) || text == IntroPolicy.NotificationKey || text == E17Text.Prefix + IntroPolicy.NotificationKey) return false;
        NotificationManager.DisplayMessageNotification(text);
        used = true;
        request = Plugin.Instance.StartCoroutine(Save(qc, false));
        return false;
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
    }
}
