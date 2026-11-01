using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Krane.AdsCameraDistance
{
    // Key -> offset map persisted as a flat JSON object. Plain I/O and a tiny parser, so it is unit-testable.
    internal sealed class OffsetStore
    {
        private readonly Dictionary<string, float> _map = new Dictionary<string, float>();

        // Bumped on every change, so callers can cache lookups.
        public int Version { get; private set; }
        public bool Dirty { get; private set; }
        public int Count => _map.Count;

        public float Get(string key)
        {
            return _map.TryGetValue(key, out float value) ? value : 0f;
        }

        // A vanilla (zero) offset removes the entry.
        public void Set(string key, float value)
        {
            if (OffsetMath.IsVanilla(value))
                Remove(key);
            else if (!_map.TryGetValue(key, out float old) || old != value)
            {
                _map[key] = value;
                Touch();
            }
        }

        public void Remove(string key)
        {
            if (_map.Remove(key))
                Touch();
        }

        private void Touch()
        {
            Version++;
            Dirty = true;
        }

        // Set when the file existed but could not be read (locked, no permission). Saving is then refused, so
        // a valid file is never replaced by this session's partial map.
        public bool ReadFailed { get; private set; }

        // Loads the map as stored; values are clamped where they are used, so a narrower Min / Max in the
        // config never rewrites them. Falls back to a leftover .tmp when a save was interrupted before its
        // rename. Returns a message when the file could not be read or was corrupt (moved to .bak; store then
        // empty); null otherwise (also when no file exists).
        public string Load(string path)
        {
            _map.Clear();
            Version++;
            Dirty = false;
            ReadFailed = false;
            string tmp = path + ".tmp";
            if (!File.Exists(path) && File.Exists(tmp))
                path = tmp;
            if (!File.Exists(path))
                return null;

            string text;
            try
            {
                text = File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception e)
            {
                ReadFailed = true;
                return "could not be read (" + e.Message + "), offsets are not applied or saved this session";
            }

            Dictionary<string, float> parsed = Parse(text, out string error);
            if (parsed == null)
            {
                string bak = path + ".bak";
                try
                {
                    if (File.Exists(bak))
                        File.Delete(bak);
                    File.Move(path, bak);
                }
                catch (Exception e)
                {
                    return "corrupt (" + error + "), could not move to .bak: " + e.Message;
                }
                return "corrupt (" + error + "), moved to " + bak;
            }

            foreach (KeyValuePair<string, float> pair in parsed)
            {
                if (!float.IsNaN(pair.Value) && !float.IsInfinity(pair.Value) && !OffsetMath.IsVanilla(pair.Value))
                    _map[pair.Key] = pair.Value;
            }
            return null;
        }

        // Writes a temp file, then swaps it in (File.Replace when the target exists), so an interrupted save
        // leaves either the old file or the .tmp that Load falls back to.
        public void Save(string path)
        {
            if (ReadFailed)
                throw new InvalidOperationException("file could not be read at start, not overwriting it");
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, Serialize(_map), new UTF8Encoding(false));
            if (File.Exists(path))
                File.Replace(tmp, path, null);
            else
                File.Move(tmp, path);
            Dirty = false;
        }

        public static string Serialize(Dictionary<string, float> map)
        {
            var keys = new List<string>(map.Keys);
            keys.Sort(StringComparer.Ordinal);
            var sb = new StringBuilder("{");
            for (int i = 0; i < keys.Count; i++)
            {
                sb.Append(i == 0 ? "\n  " : ",\n  ");
                AppendString(sb, keys[i]);
                sb.Append(": ").Append(map[keys[i]].ToString("R", CultureInfo.InvariantCulture));
            }
            sb.Append(keys.Count == 0 ? "}" : "\n}");
            return sb.ToString();
        }

        private static void AppendString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                if (c == '"' || c == '\\')
                    sb.Append('\\').Append(c);
                else if (c < ' ')
                    sb.Append("\\u").Append(((int)c).ToString("x4"));
                else
                    sb.Append(c);
            }
            sb.Append('"');
        }

        // Flat {"key": number, ...} only. Null on any deviation.
        public static Dictionary<string, float> Parse(string text, out string error)
        {
            error = null;
            var result = new Dictionary<string, float>();
            int i = 0;
            if (text.Length > 0 && text[0] == (char)0xFEFF)
                i = 1;
            SkipWs(text, ref i);
            if (i >= text.Length || text[i] != '{')
            {
                error = "expected {";
                return null;
            }
            i++;
            SkipWs(text, ref i);
            if (i < text.Length && text[i] == '}')
                return Finish(text, i + 1, result, out error);
            while (true)
            {
                SkipWs(text, ref i);
                if (!ReadString(text, ref i, out string key))
                {
                    error = "bad key at " + i;
                    return null;
                }
                SkipWs(text, ref i);
                if (i >= text.Length || text[i] != ':')
                {
                    error = "expected : at " + i;
                    return null;
                }
                i++;
                SkipWs(text, ref i);
                int start = i;
                while (i < text.Length && "+-0123456789.eE".IndexOf(text[i]) >= 0)
                    i++;
                if (i == start || !float.TryParse(text.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                {
                    error = "bad number at " + start;
                    return null;
                }
                result[key] = value;
                SkipWs(text, ref i);
                if (i < text.Length && text[i] == ',')
                {
                    i++;
                    continue;
                }
                if (i < text.Length && text[i] == '}')
                    return Finish(text, i + 1, result, out error);
                error = "expected , or } at " + i;
                return null;
            }
        }

        private static Dictionary<string, float> Finish(string text, int i, Dictionary<string, float> result, out string error)
        {
            error = null;
            SkipWs(text, ref i);
            if (i != text.Length)
            {
                error = "trailing data at " + i;
                return null;
            }
            return result;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i]))
                i++;
        }

        private static bool ReadString(string s, ref int i, out string value)
        {
            value = null;
            if (i >= s.Length || s[i] != '"')
                return false;
            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"')
                {
                    value = sb.ToString();
                    return true;
                }
                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }
                if (i >= s.Length)
                    return false;
                char e = s[i++];
                switch (e)
                {
                    case '"': case '\\': case '/': sb.Append(e); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (i + 4 > s.Length || !int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                            return false;
                        sb.Append((char)code);
                        i += 4;
                        break;
                    default: return false;
                }
            }
            return false;
        }
    }
}
