using System.Collections;

namespace CustomQuests.E17;

internal interface IVisitLoadHost
{
    float Now { get; }
    bool Cancelled { get; }
    bool PreviousClosed { get; }
    bool RoomFailed { get; }
    bool DialogReady { get; }
    bool Ready { get; }
    bool Acquire();
    void ClosePrevious();
    bool Open();
    void OpenRoom();
    void Release(bool success);
}

internal enum VisitLoadOutcome { Pending, Success, Cancelled, Unavailable, Failed, TimedOut }

// Same sequence runs in Unity and in the offline lifecycle checks.
internal sealed class VisitLoadSequence
{
    internal const float MinimumSeconds = 0.35f;
    internal const float TimeoutSeconds = 120f;
    internal VisitLoadOutcome Outcome { get; private set; }

    internal IEnumerator Run(IVisitLoadHost host)
    {
        var started = host.Now;
        try
        {
            if (!host.Acquire()) { Outcome = VisitLoadOutcome.Unavailable; yield break; }
            // Render the native cover before VisitAPI's synchronous bundle reading.
            yield return null;
            yield return null;
            if (Stop(host, started)) yield break;
            host.ClosePrevious();
            while (!host.PreviousClosed)
            {
                if (Stop(host, started)) yield break;
                yield return null;
            }
            if (Stop(host, started)) yield break;
            if (!host.Open()) { Outcome = VisitLoadOutcome.Failed; yield break; }
            // ShowScreen is queued; never expose the preceding dialog/window.
            yield return null;
            while (!host.DialogReady)
            {
                if (Stop(host, started)) yield break;
                if (host.RoomFailed) { Outcome = VisitLoadOutcome.Failed; yield break; }
                yield return null;
            }
            if (Stop(host, started)) yield break;
            // Native Show calls StartDialog(..., null). Bind room animation only afterwards.
            host.OpenRoom();
            yield return null;
            while (true)
            {
                if (Stop(host, started)) yield break;
                if (host.RoomFailed) { Outcome = VisitLoadOutcome.Failed; yield break; }
                if (host.Ready && host.Now - started >= MinimumSeconds)
                {
                    Outcome = VisitLoadOutcome.Success;
                    yield break;
                }
                yield return null;
            }
        }
        finally { host.Release(Outcome == VisitLoadOutcome.Success); }
    }

    bool Stop(IVisitLoadHost host, float started)
    {
        if (host.Cancelled) { Outcome = VisitLoadOutcome.Cancelled; return true; }
        if (host.Now - started >= TimeoutSeconds) { Outcome = VisitLoadOutcome.TimedOut; return true; }
        return false;
    }
}
