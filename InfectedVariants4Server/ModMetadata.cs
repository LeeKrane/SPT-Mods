using SPTarkov.Server.Core.Models.Spt.Mod;
using SemanticVersioning;
using Range = SemanticVersioning.Range;
using Version = SemanticVersioning.Version;

namespace InfectedVariants4Server;

public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "krane.infectedvariantsserver";
    public string Name { get; init; } = "Infected Variants";
    public string Author { get; init; } = "krane";
    public List<string>? Contributors { get; init; }
    public Version Version { get; init; } = new("1.0.0");
    public Range SptVersion { get; init; } = new("~4.1.6");
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, Range>? ModDependencies { get; init; } = new()
    {
        // 1.1.0 for DeclareVariant (API 3.1), which FactoryClassic's Halloween 2026 variant needs.
        ["com.lennoxp90.mapvariants"] = new Range(">=1.1.0 <2.0.0"),
        ["com.lennoxp90.factoryclassic"] = new Range(">=1.2.0 <2.0.0"),
    };
    public string? Url { get; init; }
    public string License { get; init; } = "MIT";
    public bool HasPrepatcher { get; init; }
}
