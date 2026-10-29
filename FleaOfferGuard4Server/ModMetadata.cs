using SPTarkov.Server.Core.Models.Spt.Mod;
using SemanticVersioning;
using Range = SemanticVersioning.Range;
using Version = SemanticVersioning.Version;

namespace FleaOfferGuard4Server;

public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "krane.fleaofferguardserver";
    public string Name { get; init; } = "Flea Offer Guard";
    public string Author { get; init; } = "krane";
    public List<string>? Contributors { get; init; }
    public Version Version { get; init; } = new("1.0.0");
    public Range SptVersion { get; init; } = new("~4.1.6");
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, Range>? ModDependencies { get; init; }
    public string? Url { get; init; }
    public string License { get; init; } = "MIT";
    public bool HasPrepatcher { get; init; }
}
