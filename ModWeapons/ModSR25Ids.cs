namespace ModWeapons;

/// <summary>Fixed ids. Item and preset ids must match db/CustomItems/ModSR25.json.</summary>
internal static class Ids
{
    public const string Sr25 = "5df8ce05b11454561e39243b";
    public const string Sr25DefaultPreset = "5e03511086f7744ccb1fb6cf";

    public const string ModSr25 = "6adb4485f8053e5cbd898d23";
    public const string ModSr25Preset = "6af31c1282f53f3fe42b025a";

    public const string Peacekeeper = "5935c25fb3acc3127c3d8cd9";
    public const string Jaeger = "5c0647fdd443bc2504c2d371";
    public const string NatoQuartermaster = "bb371fed0e82b224f2d82c0d";

    // Trader assort item ids for the default build, in preset item order (root first, 13 parts).
    public static readonly string[] JaegerAssort =
    [
        "6a6a63318824fe3e1798218c", "6af0bd0e03a6ecb97097ba83", "6aa93c9f9ffabe038a40c939", "6a23b9ed07d55be269ac1e5e",
        "6aa5ef25e408a86af979bba5", "6abb4e80fc2e013abc861288", "6a630b15d63da6f7bca4c06f", "6abb26482071d095b051d091",
        "6a2046c554b451bccd09bab8", "6ae9c38902e7544d0cee9be6", "6ac996a8a903981cb7651a7c", "6ab18bf74fe2391f44ee5e97",
        "6a2c701411929abfda42550c",
    ];

    public static readonly string[] NatoAssort =
    [
        "6ab01c4539e64fe13189ff75", "6a8cf31aacfd26a834f6000a", "6abb1bf47829e60b42122a3e", "6aac3cd6f2c45c706511487b",
        "6a6c1a9de78c7c5a4b954b5c", "6a49963d9154349edc3ffaa7", "6a6016ce020bb79712f27c67", "6ad2f67389db63116e1013bb",
        "6a3a005b52550a5fbf1ee22e", "6abc8766192681194300f289", "6a958629996fd37192c700b5", "6a50644d50001bbc286fa66b",
        "6a44af7b77f3112f6e3ab712",
    ];
}
