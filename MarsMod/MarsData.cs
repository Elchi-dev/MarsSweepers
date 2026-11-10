using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace MarsMod
{
    /// <summary>
    /// Everything the Mars mod adds to a world (mines, machines, stored materials, shields, …), saved next to
    /// the game's own save: MarsSessions/slot-N/mars-data.json. Each feature owns a named section (a JSON text).
    ///
    ///  * Host: loads it when the world starts, saves every 30 s when something changed and when leaving,
    ///    written to a temp file first and keeping the last good copy as .bak, so a crash can't eat it.
    ///  * Crew: get a full snapshot from the host when they join and every change afterwards (MarsNet).
    /// </summary>
    internal static class MarsData
    {
        static Dictionary<string, string> sections = new Dictionary<string, string>();
        static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true };
        static string path;
        static bool dirty;
        static float nextSave;
        public static bool Loaded { get; private set; }
        public static event Action<string> Changed;   // section name ("" = everything)

        static MarsData()
        {
            MarsNet.On(MarsNet.Kind.Hello, (from, r) => { if (MarsNet.IsHost && Loaded) SendSnapshot(from); });
            MarsNet.On(MarsNet.Kind.Snapshot, (from, r) =>
            {
                if (MarsNet.IsHost || from != Lobby.Owner()) return;
                int len = r.ReadInt32();
                var json = Unzip(r.ReadBytes(len));
                sections = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
                Loaded = true;
                Plugin.Log.LogMessage($"[data] Mars state received from the host ({sections.Count} sections, {len / 1024f:0.0} KB)");
                Changed?.Invoke("");
            });
            MarsNet.On(MarsNet.Kind.Section, (from, r) =>
            {
                if (MarsNet.IsHost || from != Lobby.Owner()) return;
                string key = r.ReadString();
                int len = r.ReadInt32();
                sections[key] = Unzip(r.ReadBytes(len));
                Changed?.Invoke(key);
            });
        }

        public static void Init() { } // runs the static constructor (message handlers) early

        // ------------------------------------------------------------------ world lifecycle

        /// <summary>Host: open this world's Mars data. Crew: ask the host for it.</summary>
        public static void Open(string slotDir, bool host)
        {
            Close();
            if (!host)
            {
                path = null;
                MarsNet.SendToHost(MarsNet.Kind.Hello, null);
                Plugin.Log.LogInfo("[data] asked the host for the Mars state");
                return;
            }
            path = Path.Combine(slotDir, "mars-data.json");
            sections = ReadFile(path) ?? ReadFile(path + ".bak") ?? new Dictionary<string, string>();
            Loaded = true;
            Plugin.Log.LogMessage($"[data] Mars data: {sections.Count} section(s) from {path}");
            Changed?.Invoke("");
            // Anyone already in the lobby gets it too.
            foreach (var m in Lobby.OtherMembers()) SendSnapshot(m);
        }

        public static void Close()
        {
            if (Loaded && dirty && path != null) Save();
            Loaded = false; dirty = false;
            sections = new Dictionary<string, string>();
            path = null;
        }

        public static void Tick(float now)
        {
            if (!Loaded || path == null || !dirty || now < nextSave) return;
            nextSave = now + 30f;
            Save();
        }

        static Dictionary<string, string> ReadFile(string p)
        {
            try { return File.Exists(p) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(p)) : null; }
            catch (Exception e) { Plugin.Log.LogWarning($"[data] {p} unreadable: {e.Message}"); return null; }
        }

        public static void Save()
        {
            if (path == null) return;
            try
            {
                string json = JsonSerializer.Serialize(sections, new JsonSerializerOptions { WriteIndented = true });
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(path)) File.Copy(path, path + ".bak", true);
                File.Copy(tmp, path, true);
                File.Delete(tmp);
                dirty = false;
            }
            catch (Exception e) { Plugin.Log.LogError("[data] saving Mars data failed: " + e.Message); }
        }

        // ------------------------------------------------------------------ sections

        public static T Get<T>(string key) where T : new()
        {
            try { return sections.TryGetValue(key, out var j) ? JsonSerializer.Deserialize<T>(j, Json) ?? new T() : new T(); }
            catch (Exception e) { Plugin.Log.LogWarning($"[data] section {key} unreadable: {e.Message}"); return new T(); }
        }

        /// <summary>Host only: change a section, mark it for saving and send it to the crew.</summary>
        public static void Set<T>(string key, T value)
        {
            if (!MarsNet.IsHost) return;
            string json = JsonSerializer.Serialize(value, Json);
            if (sections.TryGetValue(key, out var old) && old == json) return;
            sections[key] = json;
            dirty = true;
            var z = Zip(json);
            MarsNet.Broadcast(MarsNet.Kind.Section, w => { w.Write(key); w.Write(z.Length); w.Write(z); });
            Changed?.Invoke(key);
        }

        static void SendSnapshot(ulong to)
        {
            var z = Zip(JsonSerializer.Serialize(sections));
            bool ok = MarsNet.Send(to, MarsNet.Kind.Snapshot, w => { w.Write(z.Length); w.Write(z); });
            Plugin.Log.LogInfo($"[data] snapshot to {to}: {z.Length / 1024f:0.0} KB, {(ok ? "sent" : "FAILED")}");
        }

        public static IEnumerable<string> Status()
        {
            yield return $"Mars data {(Loaded ? "loaded" : "not loaded")}{(path != null ? " from " + path : MarsNet.IsHost ? "" : " (from the host)")}, {(dirty ? "unsaved changes" : "saved")}";
            foreach (var kv in sections) yield return $"  {kv.Key}: {kv.Value.Length} chars";
        }

        static byte[] Zip(string s)
        {
            var ms = new MemoryStream();
            using (var g = new GZipStream(ms, CompressionLevel.Fastest, true)) { var b = Encoding.UTF8.GetBytes(s); g.Write(b, 0, b.Length); }
            return ms.ToArray();
        }

        static string Unzip(byte[] z)
        {
            using (var g = new GZipStream(new MemoryStream(z), CompressionMode.Decompress))
            using (var r = new StreamReader(g, Encoding.UTF8)) return r.ReadToEnd();
        }
    }
}
