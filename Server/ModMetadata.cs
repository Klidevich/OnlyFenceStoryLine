using SPTarkov.Server.Core.Models.Spt.Mod;
using Version = SemanticVersioning.Version;
using Range = SemanticVersioning.Range;

namespace CustomQuests.E17.Server;

public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.klidevich.customquests.e17.server";
    public string Name { get; init; } = "OnlyFenceStoryLine";
    public string Author { get; init; } = "Klidevich";
    public List<string>? Contributors { get; init; } = ["Klidevich", "Codex (OpenAI)"];
    public Version Version { get; init; } = new("0.2.4");
    public Range SptVersion { get; init; } = new("4.1.6");
    public bool HasPrepatcher { get; init; }
    public List<string>? Incompatibilities { get; init; } = [];
    public Dictionary<string, Range>? ModDependencies { get; init; } = new() { ["com.sora.visitapi.server"] = new("~1.3.7") };
    public string? Url { get; init; } = "https://github.com/Klidevich/OnlyFenceStoryLine";
    public string License { get; init; } = "UNLICENSED";
}
