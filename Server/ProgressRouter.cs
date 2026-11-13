using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;

namespace CustomQuests.E17.Server;

public sealed class ProgressRequest : IRequestData
{
    [JsonPropertyName("profileId")] public string? ProfileId { get; set; }
    [JsonPropertyName("facts")] public string[]? Facts { get; set; }
}

public static class ProgressPolicy
{
    public static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "invitation", "rescue", "bill", "closed", "motive", "company", "books", "booksKnown", "dispatched",
        "actExplained", "coordinatorClaim", "route", "briefed", "ours", "archive", "uncertain",
        "ticketFirst", "ticketSecond", "radioFirst", "radioSecond", "workshop", "cargo", "finished",
        "intro2_rescue", "intro2_bill", "intro2_closed", "intro2_act", "intro2_original", "intro2_proof",
        "intro2_motive", "intro2_company", "intro2_delivery", "intro2_van", "intro2_briefed"
        , "q2Ready", "q2Brief", "q2Wait", "q2Replacement", "q2Reported", "q2Result", "q2Cargo", "q2Received"
    };
    public static MongoId Id(string fact) => new(Convert.ToHexString(
        MD5.HashData(Encoding.UTF8.GetBytes("visitapi.var|cq_e17_v1_" + fact)))[..24].ToLowerInvariant());
    public static bool Valid(string[]? facts) => facts is { Length: > 0 and <= 128 } && facts.All(Allowed.Contains);
    public static async Task<bool> Persist(SptProfile? profile, ProgressRequest request, Func<Task> save)
    {
        var pmc = profile?.CharacterData?.PmcData;
        if (pmc == null || pmc.Id.ToString() != request.ProfileId || !Valid(request.Facts)) return false;
        pmc.Variables ??= new Dictionary<MongoId, int>();
        foreach (var fact in request.Facts!) pmc.Variables[Id(fact)] = 1;
        // SPT 4.1.6 caches the hash before disk I/O. Every retry needs different JSON.
        profile!.ExtensionData["customquestsE17ProgressRevision"] = Guid.NewGuid().ToString("N");
        await save();
        return true;
    }
}

[Injectable]
public class ProgressRouter(JsonUtil json, ProfileHelper profiles, SaveServer saves, HttpResponseUtil response)
    : StaticRouter(json, [new RouteAction("/customquests/e17/progress/mark",
        async (url, info, sessionId, output, ct) =>
        {
            var request = (ProgressRequest)info;
            var saved = await ParcelRequestGate.Run(sessionId, ct, async () =>
            {
                var profile = profiles.GetFullProfile(sessionId);
                return await ProgressPolicy.Persist(profile, request, async () => { await saves.SaveProfileAsync(sessionId, ct); });
            });
            return response.GetBody(new { saved });
        }, typeof(ProgressRequest))]) { }
