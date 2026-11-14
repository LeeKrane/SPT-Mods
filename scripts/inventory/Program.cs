// Mod inventory reader. Reads metadata only (System.Reflection.Metadata), never loads assemblies.
// Supports SPT 4.x metadata types deriving AbstractModMetadata or implementing IModMetadata.
// Usage: Inventory bepinex <BepInExDir> | Inventory server <ModsDir>
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Diagnostics;
using System.Text;

if (args.Length < 2) { Console.Error.WriteLine("usage: Inventory bepinex|server <dir>"); return 1; }
var dir = Path.GetFullPath(args[1]);
if (!Directory.Exists(dir)) { Console.Error.WriteLine("no such dir: " + dir); return 1; }
if (args[0] == "bepinex") Bep.Run(dir);
else if (args[0] == "server") Srv.Run(dir);
else return 1;
return 0;

static class Util
{
    public static string Esc(string? s) => (s ?? "").Replace("|", "\\|");

    public static MetadataReader? Open(string path, out PEReader? pe)
    {
        pe = null;
        try
        {
            var fs = File.OpenRead(path);
            pe = new PEReader(fs);
            if (!pe.HasMetadata) return null;
            return pe.GetMetadataReader();
        }
        catch { return null; }
    }

    public static string TypeName(MetadataReader r, EntityHandle h)
    {
        switch (h.Kind)
        {
            case HandleKind.TypeReference: return r.GetString(r.GetTypeReference((TypeReferenceHandle)h).Name);
            case HandleKind.TypeDefinition: return r.GetString(r.GetTypeDefinition((TypeDefinitionHandle)h).Name);
            case HandleKind.TypeSpecification: return "";
            default: return "";
        }
    }

    // Declaring type name of a custom attribute ctor.
    public static string AttrType(MetadataReader r, CustomAttribute ca)
    {
        if (ca.Constructor.Kind == HandleKind.MemberReference)
            return TypeName(r, r.GetMemberReference((MemberReferenceHandle)ca.Constructor).Parent);
        if (ca.Constructor.Kind == HandleKind.MethodDefinition)
        {
            var m = r.GetMethodDefinition((MethodDefinitionHandle)ca.Constructor);
            return r.GetString(r.GetTypeDefinition(m.GetDeclaringType()).Name);
        }
        return "";
    }

    // Decode attribute blob consisting only of string args (null if not decodable).
    public static List<string?>? StringArgs(MetadataReader r, CustomAttribute ca)
    {
        var br = r.GetBlobReader(ca.Value);
        if (br.Length < 2 || br.ReadUInt16() != 1) return null;
        var l = new List<string?>();
        try
        {
            while (br.RemainingBytes > 2) l.Add(br.ReadSerializedString());
        }
        catch { }
        return l;
    }

    public static string? AsmAttr(MetadataReader r, string attrName)
    {
        foreach (var h in r.GetAssemblyDefinition().GetCustomAttributes())
        {
            var ca = r.GetCustomAttribute(h);
            if (AttrType(r, ca) == attrName) return StringArgs(r, ca)?.FirstOrDefault();
        }
        return null;
    }

    // Marks symlinked krane plugins with the repo commit of the target plugin dir.
    public static string LinkNote(string file, string root)
    {
        try
        {
            string? cur = file;
            while (cur != null && cur.Length > root.Length)
            {
                var fi = new FileInfo(cur);
                var di = new DirectoryInfo(cur);
                string? target = fi.Exists ? fi.LinkTarget : di.Exists ? di.LinkTarget : null;
                if (target != null)
                {
                    var full = Path.GetFullPath(target, Path.GetDirectoryName(cur)!);
                    return " (symlink @ " + Commit(full) + ")";
                }
                cur = Path.GetDirectoryName(cur);
            }
        }
        catch { }
        return "";
    }

    static string Commit(string target)
    {
        // expect .../<repo>/<Name>/out/<file>
        var parts = target.Split('/');
        int i = Array.LastIndexOf(parts, "out");
        if (i < 2) return "?";
        var plugDir = string.Join('/', parts[..i]);
        var repo = string.Join('/', parts[..(i - 1)]);
        try
        {
            var p = Process.Start(new ProcessStartInfo("git", new[] { "-C", repo, "log", "-1", "--format=%h", "--", parts[i - 1] })
            { RedirectStandardOutput = true, RedirectStandardError = true });
            var o = p!.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit();
            return o.Length > 0 ? o : "?";
        }
        catch { return "?"; }
    }
}

static class Bep
{
    public static void Run(string root)
    {
        Console.WriteLine("**Plugins**\n");
        Console.WriteLine("| Folder/File | Name | GUID | Version |\n|---|---|---|---|");
        Scan(Path.Combine(root, "plugins"), root, false);
        Console.WriteLine("\n**Patchers**\n");
        Console.WriteLine("| Folder/File | Name | GUID | Version |\n|---|---|---|---|");
        Scan(Path.Combine(root, "patchers"), root, true);
    }

    static void Scan(string dir, string root, bool patchers)
    {
        if (!Directory.Exists(dir)) { Console.WriteLine("| (absent) | | | |"); return; }
        var files = Directory.EnumerateFiles(dir, "*.dll", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal).ToList();
        int libs = 0;
        foreach (var f in files)
        {
            var rel = Path.GetRelativePath(dir, f);
            var r = Util.Open(f, out var pe);
            using var _ = pe;
            if (r == null) { libs++; continue; }
            var found = new List<(string? g, string? n, string? v)>();
            foreach (var th in r.TypeDefinitions)
                foreach (var ah in r.GetTypeDefinition(th).GetCustomAttributes())
                {
                    var ca = r.GetCustomAttribute(ah);
                    if (Util.AttrType(r, ca) != "BepInPlugin") continue;
                    var a = Util.StringArgs(r, ca);
                    if (a is { Count: >= 3 }) found.Add((a[0], a[1], a[2]));
                }
            var note = Util.LinkNote(f, root);
            if (found.Count > 0)
                foreach (var (g, n, v) in found)
                    Console.WriteLine($"| {Util.Esc(rel)}{note} | {Util.Esc(n)} | {Util.Esc(g)} | {Util.Esc(v)} |");
            else if (patchers)
            {
                var an = r.GetString(r.GetAssemblyDefinition().Name);
                var v = Util.AsmAttr(r, "AssemblyInformationalVersionAttribute") ?? Util.AsmAttr(r, "AssemblyFileVersionAttribute")
                        ?? r.GetAssemblyDefinition().Version.ToString();
                var av = r.GetAssemblyDefinition().Version.ToString();
                Console.WriteLine($"| {Util.Esc(rel)}{note} | {Util.Esc(an)} | - | {Util.Esc(v)} (asm {av}) |");
            }
            else libs++;
        }
        if (libs > 0) Console.WriteLine($"| _{libs} other dll(s) without BepInPlugin (libraries/native)_ | | | |");
    }
}

static class Srv
{
    record Meta(string? Name, string? Author, string? Guid, string? Version, string? Spt);

    static Dictionary<short, OpCode> ops = new();
    static System.Version asmVer = new();

    public static void Run(string root)
    {
        foreach (var f in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            if (f.GetValue(null) is OpCode oc) ops[oc.Value] = oc;

        Console.WriteLine("| Folder/File | Name | GUID | Version | Author | SPT target |\n|---|---|---|---|---|---|");
        foreach (var mod in Directory.EnumerateDirectories(root).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var folder = Path.GetFileName(mod);
            int rows = 0;
            foreach (var f in Directory.EnumerateFiles(mod, "*.dll", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
            {
                var r = Util.Open(f, out var pe);
                using var _ = pe;
                if (r == null) continue;
                var m = FindMeta(r, pe!);
                if (m == null) continue;
                rows++;
                Console.WriteLine($"| {Util.Esc(folder)}{Util.Esc(Path.GetRelativePath(mod, f) == Path.GetFileName(f) ? "/" + Path.GetFileName(f) : "/" + Path.GetRelativePath(mod, f))}{Util.LinkNote(f, root)} | {Util.Esc(m.Name)} | {Util.Esc(m.Guid)} | {Util.Esc(m.Version)} | {Util.Esc(m.Author)} | {Util.Esc(m.Spt)} |");
            }
            if (rows > 0) continue;
            // fallback: package.json / mod.json
            string? pj = new[] { "package.json", "mod.json" }.Select(n => Path.Combine(mod, n)).FirstOrDefault(File.Exists);
            if (pj != null)
            {
                string t = File.ReadAllText(pj);
                string? J(string k) { var mt = System.Text.RegularExpressions.Regex.Match(t, "\"" + k + "\"\\s*:\\s*\"([^\"]*)\""); return mt.Success ? mt.Groups[1].Value : null; }
                Console.WriteLine($"| {Util.Esc(folder)}/{Path.GetFileName(pj)} | {Util.Esc(J("name"))} | {Util.Esc(J("modGuid") ?? J("guid"))} | {Util.Esc(J("version"))} | {Util.Esc(J("author"))} | {Util.Esc(J("sptVersion") ?? J("akiVersion"))} |");
                continue;
            }
            // fallback: any dll file/assembly version
            var dll = Directory.EnumerateFiles(mod, "*.dll", SearchOption.AllDirectories).FirstOrDefault();
            string fv = "?";
            if (dll != null)
            {
                var r = Util.Open(dll, out var pe);
                using var _ = pe;
                if (r != null) fv = (Util.AsmAttr(r, "AssemblyInformationalVersionAttribute") ?? r.GetAssemblyDefinition().Version.ToString()) + " (assembly, no metadata type)";
            }
            Console.WriteLine($"| {Util.Esc(folder)} | ? | ? | {Util.Esc(fv)} | ? | ? |");
        }
    }

    static Meta? FindMeta(MetadataReader r, PEReader pe)
    {
        asmVer = r.GetAssemblyDefinition().Version;
        var defs = r.TypeDefinitions.Select(h => (h, td: r.GetTypeDefinition(h))).ToList();
        bool Derives(TypeDefinition td, int depth = 0)
        {
            foreach (var ih in td.GetInterfaceImplementations())
            {
                var it = r.GetInterfaceImplementation(ih).Interface;
                if (it.Kind == HandleKind.TypeReference && Util.TypeName(r, it) is "IModMetadata" or "AbstractModMetadata") return true;
            }
            if (depth > 6 || td.BaseType.IsNil) return false;
            if (td.BaseType.Kind == HandleKind.TypeReference) return Util.TypeName(r, td.BaseType) == "AbstractModMetadata";
            if (td.BaseType.Kind == HandleKind.TypeDefinition) return Derives(r.GetTypeDefinition((TypeDefinitionHandle)td.BaseType), depth + 1);
            return false;
        }
        foreach (var (h, td) in defs)
        {
            if (!Derives(td)) continue;
            if ((td.Attributes & TypeAttributes.Abstract) != 0) continue;
            var props = new Dictionary<string, string?>();
            foreach (var mh in td.GetMethods())
            {
                var md = r.GetMethodDefinition(mh);
                var mn = r.GetString(md.Name);
                if (md.RelativeVirtualAddress == 0) continue;
                if (mn == ".ctor" || mn.StartsWith("get_")) Scan(r, pe, md, mn, props);
            }
            string? P(string k) => props.GetValueOrDefault(k);
            var ver = P("Version");
            if (string.IsNullOrEmpty(ver))
            {
                // Version computed at runtime: fall back to assembly informational version (hash stripped)
                var iv = Util.AsmAttr(r, "AssemblyInformationalVersionAttribute") ?? r.GetAssemblyDefinition().Version.ToString();
                ver = iv.Split('+')[0] + " (assembly)";
            }
            return new Meta(P("Name"), P("Author"), P("ModGuid"), ver, P("SptVersion"));
        }
        return null;
    }

    static void Scan(MetadataReader r, PEReader pe, MethodDefinition md, string mn, Dictionary<string, string?> props)
    {
        var il = pe.GetMethodBody(md.RelativeVirtualAddress).GetILBytes()!;
        string? lastStr = null, cur = null;
        var ints = new List<int>();
        bool fromAsm = false;
        int p = 0;
        while (p < il.Length)
        {
            short v = il[p++];
            if (v == 0xFE) v = (short)(0xFE00 | il[p++]);
            if (!ops.TryGetValue(v, out var oc)) return;
            int size = oc.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, p),
                _ => 4
            };
            int op = size >= 4 ? BitConverter.ToInt32(il, p) : size == 1 ? (sbyte)il[p] : 0;
            p += size;
            string n = oc.Name!;
            if (n == "ldstr") lastStr = r.GetUserString(MetadataTokens.UserStringHandle(op & 0xFFFFFF));
            else if (n.StartsWith("ldc.i4"))
            {
                int val = n switch { "ldc.i4.m1" => -1, "ldc.i4.0" => 0, "ldc.i4.1" => 1, "ldc.i4.2" => 2, "ldc.i4.3" => 3, "ldc.i4.4" => 4,
                    "ldc.i4.5" => 5, "ldc.i4.6" => 6, "ldc.i4.7" => 7, "ldc.i4.8" => 8, _ => op };
                ints.Add(val);
            }
            else if (n is "call" or "callvirt")
            {
                var h = MetadataTokens.EntityHandle(op);
                if (h.Kind == HandleKind.MemberReference && r.GetString(r.GetMemberReference((MemberReferenceHandle)h).Name) is "GetExecutingAssembly" or "GetName")
                    fromAsm = true;
            }
            else if (n == "newobj")
            {
                var h = MetadataTokens.EntityHandle(op);
                string tn = "";
                if (h.Kind == HandleKind.MemberReference) tn = Util.TypeName(r, r.GetMemberReference((MemberReferenceHandle)h).Parent);
                if (tn is "Version" or "Range" or "Comparator")
                {
                    if (fromAsm)
                    {
                        int cnt = ints.Count > 0 && ints[0] is >= 2 and <= 4 ? ints[0] : 3;
                        var parts = new[] { asmVer.Major, asmVer.Minor, asmVer.Build, asmVer.Revision };
                        cur = string.Join('.', parts.Take(cnt));
                    }
                    else if (ints.Count >= 2 && lastStr == null) cur = string.Join('.', ints);
                    else cur = lastStr;
                }
                ints.Clear(); lastStr = null; fromAsm = false;
            }
            else if (n == "stfld")
            {
                var h = MetadataTokens.EntityHandle(op);
                string fn = h.Kind == HandleKind.FieldDefinition ? r.GetString(r.GetFieldDefinition((FieldDefinitionHandle)h).Name) : "";
                if (fn.StartsWith("<") && fn.EndsWith(">k__BackingField"))
                    props[fn[1..fn.IndexOf('>')]] = cur ?? lastStr;
                cur = null; lastStr = null; ints.Clear();
            }
            else if (n == "ret" && mn.StartsWith("get_"))
            {
                var k = mn[4..];
                if (!props.ContainsKey(k)) props[k] = cur ?? lastStr;
            }
        }
    }
}
