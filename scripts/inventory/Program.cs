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

if (args.Length >= 4 && args[0] == "matrix") return Matrix.Run(args[1], args[2], args[3]);
if (args.Length < 2) { Console.Error.WriteLine("usage: Inventory bepinex|server <dir> [--json] | matrix <mods.json> <snapshot dir> <footer.md>"); return 1; }
var dir = Path.GetFullPath(args[1]);
if (!Directory.Exists(dir)) { Console.Error.WriteLine("no such dir: " + dir); return 1; }
Out.Json = args.Contains("--json");
if (args[0] == "bepinex") Bep.Run(dir);
else if (args[0] == "server") Srv.Run(dir);
else return 1;
if (Out.Json) Out.Flush(args[0] == "server" ? "server" : "bepinex");
return 0;

record Part(string kind, string key, string path, string? name, string? guid, string? version, string? link);
record Snap(string kind, string generated, List<Part> parts);

static class Out
{
    public static bool Json;
    static readonly List<Part> parts = new();
    public static void Md(string s = "") { if (!Json) Console.WriteLine(s); }
    public static void Add(string kind, string key, string path, string? name, string? guid, string? version, string? link) =>
        parts.Add(new Part(kind, key, path, Clean(name), Clean(guid), Clean(version), link));
    static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) || s == "?" || s == "-" ? null : s;
    public static void Flush(string kind) =>
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new Snap(kind, DateTime.Now.ToString("yyyy-MM-dd"), parts),
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
}

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
    public static string? LinkCommit(string file, string root)
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
                    return Commit(full);
                }
                cur = Path.GetDirectoryName(cur);
            }
        }
        catch { }
        return null;
    }

    public static string LinkNote(string file, string root) { var c = LinkCommit(file, root); return c == null ? "" : " (symlink @ " + c + ")"; }

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
        Out.Md("**Plugins**\n");
        Out.Md("| Folder/File | Name | GUID | Version |\n|---|---|---|---|");
        Scan(Path.Combine(root, "plugins"), root, false);
        Out.Md("\n**Patchers**\n");
        Out.Md("| Folder/File | Name | GUID | Version |\n|---|---|---|---|");
        Scan(Path.Combine(root, "patchers"), root, true);
    }

    static void Scan(string dir, string root, bool patchers)
    {
        if (!Directory.Exists(dir)) { Out.Md("| (absent) | | | |"); return; }
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
            var link = Util.LinkCommit(f, root);
            var kind = patchers ? "patcher" : "plugin";
            var seg = rel.Split(Path.DirectorySeparatorChar)[0];
            var key = seg.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? seg[..^4] : seg;
            if (found.Count > 0)
                foreach (var (g, n, v) in found)
                {
                    Out.Md($"| {Util.Esc(rel)}{note} | {Util.Esc(n)} | {Util.Esc(g)} | {Util.Esc(v)} |");
                    Out.Add(kind, key, rel, n, g, v, link);
                }
            else if (patchers)
            {
                var an = r.GetString(r.GetAssemblyDefinition().Name);
                var v = Util.AsmAttr(r, "AssemblyInformationalVersionAttribute") ?? Util.AsmAttr(r, "AssemblyFileVersionAttribute")
                        ?? r.GetAssemblyDefinition().Version.ToString();
                var av = r.GetAssemblyDefinition().Version.ToString();
                Out.Md($"| {Util.Esc(rel)}{note} | {Util.Esc(an)} | - | {Util.Esc(v)} (asm {av}) |");
                Out.Add(kind, key, rel, an, null, v, link);
            }
            else libs++;
        }
        if (libs > 0) Out.Md($"| _{libs} other dll(s) without BepInPlugin (libraries/native)_ | | | |");
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

        Out.Md("| Folder/File | Name | GUID | Version | Author | SPT target |\n|---|---|---|---|---|---|");
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
                Out.Add("server", folder, Path.GetRelativePath(root, f), m.Name, m.Guid, m.Version, Util.LinkCommit(f, root));
                Out.Md($"| {Util.Esc(folder)}{Util.Esc(Path.GetRelativePath(mod, f) == Path.GetFileName(f) ? "/" + Path.GetFileName(f) : "/" + Path.GetRelativePath(mod, f))}{Util.LinkNote(f, root)} | {Util.Esc(m.Name)} | {Util.Esc(m.Guid)} | {Util.Esc(m.Version)} | {Util.Esc(m.Author)} | {Util.Esc(m.Spt)} |");
            }
            if (rows > 0) continue;
            // fallback: package.json / mod.json
            string? pj = new[] { "package.json", "mod.json" }.Select(n => Path.Combine(mod, n)).FirstOrDefault(File.Exists);
            if (pj != null)
            {
                string t = File.ReadAllText(pj);
                string? J(string k) { var mt = System.Text.RegularExpressions.Regex.Match(t, "\"" + k + "\"\\s*:\\s*\"([^\"]*)\""); return mt.Success ? mt.Groups[1].Value : null; }
                Out.Add("server", folder, folder + "/" + Path.GetFileName(pj), J("name"), J("modGuid") ?? J("guid"), J("version"), null);
                Out.Md($"| {Util.Esc(folder)}/{Path.GetFileName(pj)} | {Util.Esc(J("name"))} | {Util.Esc(J("modGuid") ?? J("guid"))} | {Util.Esc(J("version"))} | {Util.Esc(J("author"))} | {Util.Esc(J("sptVersion") ?? J("akiVersion"))} |");
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
            Out.Add("server", folder, folder, null, null, fv, null);
            Out.Md($"| {Util.Esc(folder)} | ? | ? | {Util.Esc(fv)} | ? | ? |");
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

// Renders INVENTORY.md from per-device snapshots + the intent manifest (mods.json).
static class Matrix
{
    record Ids(List<string>? server, List<string>? client);
    record Mod(string name, Ids ids, List<string> targets);
    record Manifest(List<Mod> mods);

    static readonly (string id, string title, string target)[] Devices =
    {
        ("server", "Server", "server"), ("headless1", "Headless 1", "headless"), ("headless2", "Headless 2", "headless"),
        ("tarmantria", "tarmantria", "clients"), ("tariognatha", "tariognatha", "clients"),
    };

    static string Ver(string? v) => string.IsNullOrEmpty(v) ? "?" : v.Split('+')[0].Split(' ')[0];

    sealed class Row
    {
        public string Name = ""; public bool Unknown; public string[] Cells = new string[5]; public bool Attention;
        public List<string> Missing = new(), Extra = new();
    }

    public static int Run(string manifestPath, string snapDir, string footerPath)
    {
        var jo = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip };
        var man = System.Text.Json.JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath), jo)!;
        var snaps = new Snap?[Devices.Length];
        for (int d = 0; d < Devices.Length; d++)
        {
            var p = Path.Combine(snapDir, Devices[d].id + ".json");
            if (File.Exists(p)) snaps[d] = System.Text.Json.JsonSerializer.Deserialize<Snap>(File.ReadAllText(p), jo);
        }

        // id lookup per side ("server" / "client"), case-insensitive
        var lookup = new Dictionary<string, int>[] { new(StringComparer.OrdinalIgnoreCase), new(StringComparer.OrdinalIgnoreCase) };
        for (int m = 0; m < man.mods.Count; m++)
            for (int side = 0; side < 2; side++)
                foreach (var id in (side == 0 ? man.mods[m].ids.server : man.mods[m].ids.client) ?? new())
                {
                    if (lookup[side].TryGetValue(id, out var other) && other != m)
                        Console.Error.WriteLine($"warning: id '{id}' claimed by both '{man.mods[other].name}' and '{man.mods[m].name}'");
                    lookup[side][id] = m;
                }

        // matched[mod][device] = parts; unknown[key] = (name, parts per device)
        var matched = new List<Part>[man.mods.Count, Devices.Length];
        for (int m = 0; m < man.mods.Count; m++) for (int d = 0; d < Devices.Length; d++) matched[m, d] = new();
        var unknown = new Dictionary<string, (string name, List<Part>[] parts)>(StringComparer.OrdinalIgnoreCase);
        for (int d = 0; d < Devices.Length; d++)
        {
            if (snaps[d] == null) continue;
            int side = d == 0 ? 0 : 1;
            foreach (var part in snaps[d]!.parts)
            {
                // GUID first (splits mods sharing a folder, e.g. Fika.Core / Fika.Headless), then folder/file key
                int m = part.guid != null && lookup[side].TryGetValue(part.guid, out var a) ? a
                      : lookup[side].TryGetValue(part.key, out var b) ? b : -1;
                if (m >= 0) { matched[m, d].Add(part); continue; }
                var uk = part.guid ?? part.key;
                if (!unknown.TryGetValue(uk, out var u))
                    unknown[uk] = u = (part.name ?? part.key, Devices.Select(_ => new List<Part>()).ToArray());
                u.parts[d].Add(part);
            }
        }

        string Cell(List<Part> parts)
        {
            var vs = parts.Select(p => Ver(p.version)).Distinct().ToList();
            var s = string.Join('/', vs);
            var links = parts.Select(p => p.link).Where(l => l != null).Distinct().ToList();
            return links.Count > 0 ? s + " @" + string.Join('/', links) : s;
        }

        var rows = new List<Row>();
        var usedMods = new HashSet<int>();
        for (int m = 0; m < man.mods.Count; m++)
        {
            var mod = man.mods[m];
            var row = new Row { Name = mod.name };
            for (int d = 0; d < Devices.Length; d++)
            {
                bool intended = mod.targets.Contains(Devices[d].target, StringComparer.OrdinalIgnoreCase);
                var parts = matched[m, d];
                if (snaps[d] == null) { row.Cells[d] = "?"; continue; }
                if (parts.Count > 0) usedMods.Add(m);
                if (parts.Count > 0 && intended) row.Cells[d] = Cell(parts);
                else if (parts.Count > 0) { row.Cells[d] = Cell(parts) + " ⚠"; row.Extra.Add(Devices[d].title); }
                else if (intended) { row.Cells[d] = "!"; row.Missing.Add(Devices[d].title); }
                else row.Cells[d] = "-";
            }
            row.Attention = row.Missing.Count + row.Extra.Count > 0;
            rows.Add(row);
        }
        foreach (var (uk, u) in unknown)
        {
            var row = new Row { Name = u.name + " _(not in manifest)_", Unknown = true, Attention = true };
            for (int d = 0; d < Devices.Length; d++)
                row.Cells[d] = snaps[d] == null ? "?" : u.parts[d].Count > 0 ? Cell(u.parts[d]) : "-";
            rows.Add(row);
        }
        for (int m = 0; m < man.mods.Count; m++)
            if (!usedMods.Contains(m) && snaps.Any(s => s != null))
                Console.Error.WriteLine($"warning: manifest mod '{man.mods[m].name}' matches nothing on any device");

        rows = rows.OrderBy(r => r.Attention ? 0 : 1).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();

        var sb = new StringBuilder();
        string E(string s) => s.Replace("|", "\\|");
        sb.AppendLine("# SPT mod inventory\n");
        sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd") + " (generated file, do not edit). Source: installed files only (assembly metadata, no logs, nothing loaded).\n");
        sb.AppendLine("Snapshots: " + string.Join(", ", Devices.Select((dv, i) => dv.title + " " + (snaps[i]?.generated ?? "none"))) + ".\n");
        sb.AppendLine("Regenerate: `scripts/inventory.sh all` (read-only rsync from taragarmr, this machine's snapshot, then the matrix). `scripts/inventory.sh matrix` re-renders from the stored snapshots (`inventory/*.json`) and the intent manifest `scripts/inventory/mods.json`. tariognatha adds its own snapshot: on that machine `git pull`, `scripts/inventory.sh all` (or `scripts/inventory.sh local --json > inventory/tariognatha.json`), then commit `inventory/tariognatha.json` and `INVENTORY.md`. Needs `dotnet` 10 or `nix` (see `scripts/inventory/README.md`).\n");
        sb.AppendLine("## Legend\n");
        sb.AppendLine("Server column: version of the server part. Device columns: version of the client plugin(s) and patchers. Differing versions among a mod's parts on one device show as `a/b` (often companion plugins or bundled libraries with their own version). Symlinked krane plugins add `@<commit>` (last repo commit touching the plugin).\n");
        sb.AppendLine("| Cell | Meaning |\n|---|---|\n| `1.2.3` | intended and installed |\n| `!` | intended but missing |\n| `-` | not intended (e.g. server only or client only) and absent |\n| `1.2.3 ⚠` | installed but not intended on that device |\n| `?` | no snapshot for that device yet |\n");
        sb.AppendLine("Intent comes from `scripts/inventory/mods.json` (targets `server`, `headless` for both headless clients, `clients` for tarmantria and tariognatha). Mods not in the manifest are flagged _(not in manifest)_; classify them there.\n");
        sb.AppendLine("## Matrix\n");
        sb.AppendLine("Mods needing attention (`!`, `⚠`, not in manifest) come first, then the rest alphabetically.\n");
        sb.AppendLine("| Mod | " + string.Join(" | ", Devices.Select(d => d.title)) + " |");
        sb.AppendLine("|---|" + string.Join("|", Devices.Select(_ => "---")) + "|");
        foreach (var r in rows) sb.AppendLine("| " + E(r.Name).Replace("\\_", "_") + " | " + string.Join(" | ", r.Cells.Select(E)) + " |");
        sb.AppendLine("\n## Attention\n");
        var att = rows.Where(r => r.Attention).ToList();
        if (att.Count == 0) sb.AppendLine("Nothing.");
        foreach (var r in att.Where(r => r.Missing.Count > 0))
            sb.AppendLine($"- `!` missing: {r.Name} on {string.Join(", ", r.Missing)}");
        foreach (var r in att.Where(r => r.Extra.Count > 0))
            sb.AppendLine($"- `⚠` unexpected: {r.Name} on {string.Join(", ", r.Extra)}");
        foreach (var r in att.Where(r => r.Unknown))
            sb.AppendLine($"- not in manifest: {r.Name.Replace(" _(not in manifest)_", "")} (present on {string.Join(", ", Devices.Where((_, i) => r.Cells[i] != "-" && r.Cells[i] != "?").Select(d => d.title))})");
        sb.AppendLine();
        if (File.Exists(footerPath)) sb.Append(File.ReadAllText(footerPath));

        Console.Write(sb.ToString());
        var unk = rows.Where(r => r.Unknown).Select(r => r.Name.Replace(" _(not in manifest)_", "")).ToList();
        if (unk.Count > 0) Console.Error.WriteLine($"warning: {unk.Count} mod(s) not in manifest: {string.Join("; ", unk)}");
        return 0;
    }
}
