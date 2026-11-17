using System.Threading.Tasks;

namespace CustomQuests.E17;

internal interface IVisitScreenPreparation
{
    object Current { get; }
    bool Cancelled { get; }
    void Queue();
    Task<bool> CloseCurrent();
    bool Closed(object screen);
    void Restore(object screen);
    void Commit();
}

// Mirrors the installed PrepareShow, adding cancellation before manager/UI ownership changes.
internal static class VisitScreenPreparation
{
    internal static async Task<bool> Run(IVisitScreenPreparation host)
    {
        if (host.Cancelled) return false;
        var previous = host.Current;
        host.Queue();
        if (!await host.CloseCurrent()) return false;
        if (!ReferenceEquals(previous, host.Current)) return false;
        if (host.Cancelled)
        {
            if (previous != null && host.Closed(previous)) host.Restore(previous);
            return false;
        }
        host.Commit();
        return true;
    }
}
