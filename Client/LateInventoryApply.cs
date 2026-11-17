using System;
using Comfort.Common;
using EFT;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CustomQuests.E17;

// Parse the same UnparsedData envelope used by the native backend before updating the live profile.
internal static class LateInventoryApply
{
    internal static ClientBackendSession.QueueData Parse(JObject effect)
    {
        var serializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            Converters = { new UnparsedDataConverter() }
        });
        return effect.ToObject<ClientBackendSession.QueueData>(serializer)
            ?? throw new InvalidOperationException("E17 native inventory response is empty");
    }

    internal static void Apply(ClientBackendSession session, string profileId, JObject effect)
    {
        var changes = Parse(effect);
        if (changes.ProfileChanges == null || changes.ProfileChanges.Count != 1 || !changes.ProfileChanges.ContainsKey(profileId))
            throw new InvalidOperationException("E17 inventory response targets another profile");
        if (session?._profileUpdaters == null || !session._profileUpdaters.TryGetValue(profileId, out var updater) || updater == null)
            throw new InvalidOperationException("E17 native profile updater is not registered");
        session.ApplyProfileChanges(new Result<ClientBackendSession.QueueData>(changes));
    }
}
