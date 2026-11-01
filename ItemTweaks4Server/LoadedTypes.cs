namespace ItemTweaks4Server;

/// <summary>Finds a type of an optional mod by full name across the loaded assemblies, without a hard reference. Only hits are cached:
/// a miss is looked up again next time, in case the assembly loads later. Called during load only (single-threaded).</summary>
internal static class LoadedTypes
{
    private static readonly Dictionary<string, Type> Cache = new();

    public static Type? Find(string fullName)
    {
        if (Cache.TryGetValue(fullName, out var cached)) return cached;
        var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(fullName)).FirstOrDefault(t => t != null);
        if (type != null) Cache[fullName] = type;
        return type;
    }
}
