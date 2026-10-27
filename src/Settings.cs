using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace PaintDark
{
    /// <summary>Tiny INI store in %APPDATA%\PaintDark. Everything the app remembers between sessions lives here.</summary>
    public static class Settings
    {
        static readonly Dictionary<string, string> _v = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public static string DataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PaintDark");
        static string FilePath => Path.Combine(DataDir, "settings.ini");

        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int i = line.IndexOf('=');
                    if (i <= 0 || line.StartsWith(";")) continue;
                    _v[line.Substring(0, i).Trim()] = line.Substring(i + 1);
                }
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                var sb = new StringBuilder();
                sb.AppendLine("; Paint Dark Mode settings");
                var keys = new List<string>(_v.Keys);
                keys.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (var k in keys) sb.Append(k).Append('=').AppendLine(_v[k]);
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), Encoding.UTF8);
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
            }
            catch { }
        }

        public static string Get(string k, string def = null) => _v.TryGetValue(k, out var s) ? s : def;
        public static void Set(string k, string v) { if (v == null) _v.Remove(k); else _v[k] = v; }
        public static int GetInt(string k, int def) => _v.TryGetValue(k, out var s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int r) ? r : def;
        public static void SetInt(string k, int v) => _v[k] = v.ToString(CultureInfo.InvariantCulture);
        public static double GetDouble(string k, double def) => _v.TryGetValue(k, out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double r) ? r : def;
        public static void SetDouble(string k, double v) => _v[k] = v.ToString("R", CultureInfo.InvariantCulture);
        public static bool GetBool(string k, bool def) => _v.TryGetValue(k, out var s) ? s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase) : def;
        public static void SetBool(string k, bool v) => _v[k] = v ? "1" : "0";

        public static IEnumerable<KeyValuePair<string, string>> WithPrefix(string prefix)
        {
            foreach (var kv in _v) if (kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) yield return kv;
        }

        public static void RemovePrefix(string prefix)
        {
            var del = new List<string>();
            foreach (var k in _v.Keys) if (k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) del.Add(k);
            foreach (var k in del) _v.Remove(k);
        }

        // ---------- recent pictures ----------
        public sealed class RecentItem { public string Path; public bool Pinned; }

        public static List<RecentItem> Recent
        {
            get
            {
                var list = new List<RecentItem>();
                for (int i = 0; i < 20; i++)
                {
                    var s = Get("Recent" + i);
                    if (string.IsNullOrEmpty(s)) continue;
                    bool pin = s.StartsWith("*");
                    list.Add(new RecentItem { Path = pin ? s.Substring(1) : s, Pinned = pin });
                }
                return list;
            }
        }

        static void StoreRecent(List<RecentItem> list)
        {
            RemovePrefix("Recent");
            for (int i = 0; i < list.Count && i < 20; i++) Set("Recent" + i, (list[i].Pinned ? "*" : "") + list[i].Path);
        }

        public static void AddRecent(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            var list = Recent;
            bool pinned = false;
            list.RemoveAll(r => { if (string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase)) { pinned = r.Pinned; return true; } return false; });
            list.Insert(0, new RecentItem { Path = path, Pinned = pinned });
            // keep pinned items, trim unpinned to a sensible count
            int unpinned = 0;
            list.RemoveAll(r => !r.Pinned && ++unpinned > 12);
            StoreRecent(list);
            try { Native.SHAddToRecentDocs(3, path); } catch { }
        }

        public static void TogglePin(string path)
        {
            var list = Recent;
            foreach (var r in list) if (string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase)) r.Pinned = !r.Pinned;
            StoreRecent(list);
        }

        public static void RemoveRecent(string path)
        {
            var list = Recent;
            list.RemoveAll(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
            StoreRecent(list);
        }
    }
}
