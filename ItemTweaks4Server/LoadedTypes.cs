using System.Collections.Concurrent;

namespace ItemTweaks4Server;

/// <summary>Finds a type of an optional mod by full name across the loaded assemblies, without a hard reference. Cached per name, misses
/// too: every mod assembly is loaded before any OnLoad runs, so the answer never changes.</summary>
internal static class LoadedTypes
{
    private static readonly ConcurrentDictionary<string, Type?> Cache = new();

    public static Type? Find(string fullName) => Cache.GetOrAdd(fullName, name => AppDomain.CurrentDomain.GetAssemblies()
        .Select(a => a.GetType(name))
        .FirstOrDefault(t => t != null));
}
