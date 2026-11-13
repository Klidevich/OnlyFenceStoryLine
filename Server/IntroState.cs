using System.Text.Json;
using SPTarkov.Server.Core.Models.Eft.Profile;

namespace CustomQuests.E17.Server;

// Account metadata survives character creation, wipe and prestige; PMC Variables do not.
public static class IntroState
{
    public const string Key = "customquestsE17IntroV1";
    public enum Stage { Absent, Registered, Ready, Completed, Skipped, Disabled }

    public static Stage Read(Info? info)
    {
        if (info?.ExtensionData?.TryGetValue(Key, out var value) != true) return Stage.Absent;
        var text = value is JsonElement element && element.ValueKind == JsonValueKind.String ? element.GetString() : value as string;
        return Enum.TryParse<Stage>(text, out var result) && Enum.IsDefined(result) ? result : Stage.Absent;
    }

    // Legacy Registered/Ready/Disabled were installation-time gates, not evidence of watching.
    // Only an actual completion or explicit skip consumes automatic playback.
    public static Stage Finish(Stage previous, bool skipped) => previous is Stage.Completed or Stage.Skipped
        ? previous : (skipped ? Stage.Skipped : Stage.Completed);

    public static bool Eligible(SptProfile? profile) => profile?.ProfileInfo != null &&
        profile.CharacterData?.PmcData != null && Read(profile.ProfileInfo) is not (Stage.Completed or Stage.Skipped);

    public static async Task<bool> Complete(SptProfile? profile, string? expectedProfileId, bool skipped, Func<Task> save)
    {
        // The authenticated session selects the account; the client ID can only reject a stale request.
        if (profile?.ProfileInfo == null || profile.CharacterData?.PmcData == null ||
            string.IsNullOrEmpty(expectedProfileId) || profile.CharacterData.PmcData.Id.ToString() != expectedProfileId) return false;
        await Persist(profile.ProfileInfo, Finish(Read(profile.ProfileInfo), skipped), save);
        return true;
    }

    public static async Task Persist(Info info, Stage next, Func<Task> save)
    {
        var old = Read(info);
        if (old == next) return;
        info.ExtensionData![Key] = next.ToString();
        info.ExtensionData[Key + "Revision"] = Guid.NewGuid().ToString("N");
        try { await save(); }
        catch { info.ExtensionData[Key] = old.ToString(); throw; }
    }

}
