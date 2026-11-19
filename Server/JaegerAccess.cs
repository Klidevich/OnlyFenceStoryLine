using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;

namespace CustomQuests.E17.Server;

public sealed record JaegerAccessResult(bool Changed, string? Warning = null);

public static class JaegerAccess
{
    public static int OpenInTemplates(IEnumerable<ProfileSides> profiles)
    {
        var changed = 0;
        foreach (var profile in profiles)
        {
            if (Open(profile.Bear?.Trader)) changed++;
            if (Open(profile.Usec?.Trader)) changed++;
        }
        return changed;
    }

    static bool Open(ProfileTraderTemplate? trader)
    {
        if (trader == null || trader.JaegerUnlocked == true) return false;
        trader.JaegerUnlocked = true;
        return true;
    }

    // The caller holds the per-session request gate. Only the native helper changes
    // a valid existing entry; missing trader data is never rebuilt by this migration.
    public static async Task<JaegerAccessResult> Migrate(SptProfile? profile,
        Action unlock, Func<Task> save)
    {
        if (profile?.ProfileInfo == null || profile.ProfileInfo.InvalidOrUnloadableProfile == true)
            return new(false, "profile is unavailable or invalid; Jaeger access retained");
        var traders = profile.CharacterData?.PmcData?.TradersInfo;
        if (traders == null || !traders.TryGetValue(Traders.JAEGER, out var jaeger) || jaeger == null)
            return new(false, "Jaeger entry is missing; trader data was not recreated");
        if (jaeger.Unlocked == true) return new(false);

        var original = jaeger.Unlocked;
        // SPT 4.1.6 caches the save hash BEFORE writing the file. Saving the locked
        // baseline first makes retries safe after a failed unlocked-state write:
        // the next unlocked snapshot cannot be skipped because of that stale hash.
        await save();
        try
        {
            unlock();
            if (jaeger.Unlocked != true)
                throw new InvalidOperationException("Native trader helper did not unlock Jaeger.");
            await save();
        }
        catch
        {
            jaeger.Unlocked = original;
            throw;
        }
        return new(true);
    }
}
