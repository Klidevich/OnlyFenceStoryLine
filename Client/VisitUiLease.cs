using System;

namespace CustomQuests.E17;

internal sealed class VisitUiLease
{
    readonly Action loader, cover;
    bool ownsLoader = true, ownsCover = true;
    internal VisitUiLease(Action restoreLoader, Action restoreCover) { loader = restoreLoader; cover = restoreCover; }
    internal void Restore(bool restoreCover, bool restoreLoader)
    {
        try
        {
            if (restoreLoader && ownsLoader) { ownsLoader = false; loader(); }
        }
        finally
        {
            if (restoreCover && ownsCover) { ownsCover = false; cover(); }
        }
    }
}
