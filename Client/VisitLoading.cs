using System;
using System.Collections;
using System.Reflection;
using System.Threading.Tasks;
using EFT;
using EFT.Dialogs;
using EFT.InventoryLogic;
using EFT.Quests;
using EFT.UI;
using EFT.UI.Screens;
using HarmonyLib;
using UnityEngine;
using VisitAPI.Dialog;
using VisitAPI.Native;

namespace CustomQuests.E17;

// Own only our lobby visits. Native raid/hideout dialogs and other mods are unchanged.
internal static class VisitLoading
{
    internal static readonly MethodInfo OpenMethod = AccessTools.DeclaredMethod(typeof(DialogOpener), "TryOpen", new[]
    {
        typeof(DialogTree), typeof(Profile), typeof(QuestController), typeof(InventoryController),
        typeof(TraderScreensGroup), typeof(string), typeof(bool), typeof(string).MakeByRefType(), typeof(bool)
    }) ?? throw new MissingMethodException("VisitAPI DialogOpener.TryOpen signature changed");
    static readonly FieldInfo RoomBusy = AccessTools.Field(typeof(SceneLoader), "_busy");
    static readonly FieldInfo RoomCamera = AccessTools.Field(typeof(SceneCamera), "_cam");
    static readonly FieldInfo ScreenDialog = AccessTools.Field(typeof(TraderDialogScreen), "dialogController");
    static Host active;
    static bool replaying;
    static bool changingUi;

    internal static void Dispose()
    {
        var previous = active;
        active = null;
        if (previous != null) { previous.Stopped = true; previous.Release(false); }
    }

    [HarmonyPatch]
    static class OpenPatch
    {
        static MethodBase TargetMethod() => OpenMethod;
        // EntryNode resolves the authored entry before we capture the delayed call.
        [HarmonyPriority(Priority.Last)]
        static bool Prefix(DialogTree tree, Profile profile, QuestController quests, InventoryController inventory,
            TraderScreensGroup tradeScreen, string forceNode, bool scene, bool atOptions, ref string error, ref bool __result)
        {
            if (replaying || !scene || tree?.Scene == null || !VisitFlow.OurTree(tree) ||
                !QuestOps.IsLobby(quests) || !QuestOps.IsLive(quests)) return true;
            error = null;
            __result = true; // Accepted asynchronously; VisitAPI's button waits for its dialog tracker.
            if (active != null) return false;
            active = new Host(quests, tree, new object[] { tree, profile, quests, inventory, tradeScreen, forceNode, scene, null, atOptions });
            Plugin.Instance.StartCoroutine(Drive(active));
            return false;
        }
    }

    static IEnumerator Drive(Host host)
    {
        var sequence = new VisitLoadSequence();
        var steps = sequence.Run(host);
        Exception failure = null;
        try
        {
            while (true)
            {
                bool moved;
                try { moved = steps.MoveNext(); }
                catch (Exception e) { failure = e; break; }
                if (!moved) break;
                yield return steps.Current;
            }
        }
        finally
        {
            try { (steps as IDisposable)?.Dispose(); }
            finally { if (ReferenceEquals(active, host)) active = null; }
        }
        if (failure != null || sequence.Outcome is VisitLoadOutcome.Failed or VisitLoadOutcome.TimedOut)
        {
            Plugin.Warn("Visit loading failed: " + (failure?.ToString() ?? host.Error ?? sequence.Outcome.ToString()));
            EFT.Communications.NotificationManager.DisplayMessageNotification(LatePolicy.SystemText(
                /* release-text:Client.VisitLoading.2 */ "",
                /* release-text:Client.VisitLoading.1 */ "",
                /* release-text:Client.VisitLoading.0 */ ""));
        }
    }

    [HarmonyPatch(typeof(DialogSession), nameof(DialogSession.Begin))]
    static class CaptureDialog
    {
        static void Postfix(ClientDialogController dc, DialogTree tree, QuestController quests)
        {
            if (replaying && active != null && ReferenceEquals(active.Tree, tree) && ReferenceEquals(active.Quests, quests))
                active.Dialog = dc;
        }
    }

    [HarmonyPatch]
    static class PrepareScreen
    {
        static MethodBase TargetMethod() => AccessTools.Method(typeof(TraderDialogScreen.TraderDialogScreenController), "PrepareShow");
        static bool Prefix(object __instance, EScreenState screenState, bool forced, ref Task<bool> __result)
        {
            if (!replaying || active == null || __instance is not TraderDialogScreen.TraderDialogScreenController controller ||
                controller.TraderId != active.Tree.TraderId || !ReferenceEquals(controller.QuestController, active.Quests)) return true;
            active.Dialog = controller.DialogController;
            __result = Prepare(active, controller, screenState, forced);
            return false;
        }
        static async Task<bool> Prepare(Host host, TraderDialogScreen.TraderDialogScreenController controller, EScreenState state, bool forced)
        {
            try
            {
                var result = await VisitScreenPreparation.Run(new Preparation(host, controller, state, forced));
                if (!result) host.Stopped = true;
                return result;
            }
            catch (Exception e) { host.PreparationError = true; host.Error = e.ToString(); return false; }
        }
    }

    sealed class Preparation : IVisitScreenPreparation
    {
        readonly Host host;
        readonly TraderDialogScreen.TraderDialogScreenController controller;
        readonly EScreenState state;
        readonly bool forced;
        readonly ScreenManager<EEftScreenType> manager = ScreenManager<EEftScreenType>._privateInstance;
        internal Preparation(Host h, TraderDialogScreen.TraderDialogScreenController c, EScreenState s, bool f)
        { host = h; controller = c; state = s; forced = f; }
        public object Current => manager.CurrentBaseScreenController;
        public bool Cancelled => host.Stopped || host.Released || !ReferenceEquals(active, host) || host.Cancelled;
        public void Queue() => controller.QueueScreen(state, true);
        public Task<bool> CloseCurrent() => controller.CloseCurrentScreen(forced);
        public bool Closed(object screen) => ((IBaseScreenController<EEftScreenType>)screen).Closed;
        public void Restore(object screen)
        {
            manager.CurrentBaseScreenController = null;
            ((IBaseScreenController<EEftScreenType>)screen).ShowScreenAsPrevious();
        }
        public void Commit()
        {
            manager.CurrentBaseScreenController = controller;
            controller.InitScreen();
            controller.OnShowStart();
        }
    }

    [HarmonyPatch]
    static class LoadingInput
    {
        static MethodBase TargetMethod() => AccessTools.Method(typeof(TraderDialogScreen), "HandleKeyboardInput");
        static bool Prefix(TraderDialogScreen __instance) => active == null || !active.BlocksInput(__instance);
    }

    [HarmonyPatch(typeof(PreloaderUI), nameof(PreloaderUI.SetLoaderStatus))]
    static class LoaderOwnership
    {
        static void Prefix(PreloaderUI __instance) { if (!changingUi) active?.HandOff(__instance, false); }
    }
    [HarmonyPatch]
    static class CoverOwnership
    {
        static System.Collections.Generic.IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(PreloaderUI), nameof(PreloaderUI.SetBlackImageAlpha));
            yield return AccessTools.Method(typeof(PreloaderUI), nameof(PreloaderUI.StartBlackScreenShow));
            yield return AccessTools.Method(typeof(PreloaderUI), nameof(PreloaderUI.FadeBlackScreen));
        }
        static void Prefix(PreloaderUI __instance) { if (!changingUi) active?.HandOff(__instance, true); }
    }

    sealed class Host : IVisitLoadHost
    {
        internal readonly QuestController Quests;
        internal readonly DialogTree Tree;
        readonly object[] arguments;
        readonly ClientDialogController previousDialog;
        internal ClientDialogController Dialog;
        internal bool Stopped;
        internal bool PreparationError;
        internal bool Released => released;
        internal string Error;
        PreloaderUI ui;
        bool acquired, released, opened, roomOpened, seenDialog;
        VisitUiLease lease;
        bool oldLoader, oldBlackActive, oldRaycast;
        Color oldColor;

        internal Host(QuestController quests, DialogTree tree, object[] args)
        { Quests = quests; Tree = tree; arguments = args; previousDialog = VisitFlow.CurrentDialog; }
        public float Now => Time.realtimeSinceStartup;
        bool Busy => (bool)RoomBusy.GetValue(null);
        bool MatchingScreen => DialogScreenTracker.Live != null && Dialog != null &&
            ReferenceEquals(ScreenDialog.GetValue(DialogScreenTracker.Live), Dialog);
        public bool Cancelled => Stopped || Input.GetKeyDown(KeyCode.Escape) || !QuestOps.IsLobby(Quests) ||
            !QuestOps.IsLive(Quests) || !ReferenceEquals(QuestOps.Resolve(), Quests) || (seenDialog && !MatchingScreen) ||
            (opened && DialogScreenTracker.Open && !MatchingScreen);
        internal bool BlocksInput(TraderDialogScreen screen)
        {
            if (!acquired || released) return false;
            var dc = ScreenDialog.GetValue(screen);
            return (Dialog != null && ReferenceEquals(dc, Dialog)) || (previousDialog != null && ReferenceEquals(dc, previousDialog));
        }
        public bool PreviousClosed => !Busy && !SceneLoader.IsOpen && !DialogScreenTracker.Open;
        bool CameraReady => RoomCamera.GetValue(null) is Camera camera && camera.isActiveAndEnabled;
        public bool RoomFailed => PreparationError || (roomOpened && !Busy && (!SceneLoader.IsOpen || !CameraReady || Dialog?._animationController == null));
        public bool Ready => DialogReady && !Busy && SceneLoader.IsOpen && CameraReady && Dialog._animationController != null;
        public bool DialogReady
        {
            get
            {
                if (!MatchingScreen) return false;
                seenDialog = true;
                var window = DialogScreenTracker.Live._dialogWindow;
                return Dialog.CurrentDialog != null && window != null && window.isActiveAndEnabled &&
                    ((!string.IsNullOrEmpty(window._traderText?.text)) || window._linesContainer.childCount > 0);
            }
        }
        public bool Acquire()
        {
            if (RoomBusy == null || RoomCamera == null || ScreenDialog == null) throw new MissingFieldException("VisitAPI loading contract changed");
            ui = MonoBehaviourSingleton<PreloaderUI>.Instance;
            if (ui == null || ui._loader == null || ui._overlapBlackImage == null) throw new InvalidOperationException("Native PreloaderUI unavailable");
            // Never take over another native operation's cover/loader.
            if (ui._loader.activeSelf || ui._overlapBlackImage.gameObject.activeSelf && ui._overlapBlackImage.color.a > 0.001f) return false;
            oldLoader = ui._loader.activeSelf;
            oldBlackActive = ui._overlapBlackImage.gameObject.activeSelf;
            oldColor = ui._overlapBlackImage.color;
            oldRaycast = ui._overlapBlackImage.raycastTarget;
            lease = new VisitUiLease(() => ui.SetLoaderStatus(oldLoader), () =>
            {
                ui._overlapBlackImage.color = oldColor;
                ui._overlapBlackImage.raycastTarget = oldRaycast;
                ui._overlapBlackImage.gameObject.SetActive(oldBlackActive);
            });
            acquired = true;
            changingUi = true;
            try
            {
                ui.SetBlackImageAlpha(1f);
                ui._overlapBlackImage.raycastTarget = true;
                ui.SetLoaderStatus(true);
            }
            finally { changingUi = false; }
            return true;
        }
        public void ClosePrevious()
        {
            DialogScreenTracker.Live?.method_8();
            SceneLoader.Close();
        }
        public bool Open()
        {
            opened = true;
            replaying = true;
            try
            {
                arguments[6] = false; // Scene starts after native Show/StartDialog, below.
                var result = (bool)OpenMethod.Invoke(null, arguments);
                Error = arguments[7] as string;
                return result;
            }
            finally { replaying = false; }
        }
        public void OpenRoom()
        {
            roomOpened = true;
            SceneLoader.Open(Tree.Scene == "auto" ? Tree.TraderId : Tree.Scene, Dialog);
        }
        internal void HandOff(PreloaderUI owner, bool cover)
        {
            if (!acquired || released || owner != ui) return;
            Stopped = true;
            // Restore our modifications before the newer native setter applies its own state.
            RestoreUi(cover, !cover);
        }
        void RestoreUi(bool cover, bool loader)
        {
            if (ui == null) return;
            changingUi = true;
            try
            {
                lease?.Restore(cover, loader);
            }
            finally { changingUi = false; }
        }
        public void Release(bool success)
        {
            if (released) return;
            released = true;
            if (!success) Stopped = true;
            try
            {
                if (!success && opened)
                {
                    if (MatchingScreen) DialogScreenTracker.Live.method_8();
                    // A new foreign dialog must not be closed by this request's cleanup.
                    if (roomOpened && (!DialogScreenTracker.Open || MatchingScreen)) SceneLoader.Close();
                }
            }
            finally
            {
                if (acquired) RestoreUi(true, true);
            }
        }
    }
}
