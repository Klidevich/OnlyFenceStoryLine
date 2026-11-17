using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Utils.Cloners;

namespace CustomQuests.E17.Server;

public static class LateTransaction
{
    /// <summary>Call under ParcelRequestGate. Roll back the same inventory, mail and ledger boundary on a failed save.</summary>
    public static async Task<bool> Run(SptProfile profile, ICloner cloner, Func<Task<bool>> mutate, Func<Task> save)
    {
        var characters = cloner.Clone(profile.CharacterData);
        var dialogue = cloner.Clone(profile.DialogueRecords);
        var extension = cloner.Clone(profile.ExtensionData);
        try
        {
            if (!await mutate()) return false;
            await save();
            return true;
        }
        catch
        {
            profile.CharacterData = characters;
            profile.DialogueRecords = dialogue;
            profile.ExtensionData = extension ?? new();
            // SaveServer updates its hash before its write. A retry/rollback must not reuse that cached hash.
            profile.ExtensionData["customquestsE17LateRevision"] = Guid.NewGuid().ToString("N");
            throw;
        }
    }
}
