using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MoonCleaners;
using UnityEngine;

namespace MarsMod
{
    /// <summary>
    /// Mars buildings (our own building system). The host owns the truth: it keeps the list in the Mars save
    /// data, runs production, and applies every player's request (build, remove, take, insert, deposit, sell).
    /// Everyone draws the buildings from that data, so the whole crew always sees the same machines.
    /// </summary>
    internal static class Buildings
    {
        // ------------------------------------------------------------------ data (saved, synced)

        public sealed class Rec
        {
            public int Id;
            public string Type, Owner;
            public float Px, Py, Pz, Qx, Qy, Qz, Qw = 1f;
            public int Mine = -1;
            public bool Vanilla;        // placed with the game's build tool: the game owns it (Id = VanillaIds + depot id)
            public Dictionary<string, int> Items = new Dictionary<string, int>();
            public float Progress;
            public float Dust;          // storm dust on the machine, 0..1; at 1 it is jammed
            public float Energy;        // a battery's charge (kW·min)
            public float Burn;          // a generator's seconds of fuel left in the chamber
            public float Supply = 1f;   // share of its power the grid gives it (0..1), set by the host
            public bool Active;         // a shield's dome is up
            public Vector3 Pos => new Vector3(Px, Py, Pz);
            public Quaternion Rot => new Quaternion(Qx, Qy, Qz, Qw);
        }

        public sealed class State
        {
            public int NextId = 1;
            public List<Rec> List = new List<Rec>();
        }

        public sealed class Stock { public Dictionary<string, Dictionary<string, int>> Backpacks = new Dictionary<string, Dictionary<string, int>>(); public Dictionary<string, int> Storage = new Dictionary<string, int>(); }

        const string BuildingsKey = "buildings", StockKey = "stock";
        public static State Data = new State();
        public static Stock Items = new Stock();
        static bool dirty, stockDirty;
        static float nextSync, nextProduction, lastProduction, nextWorldSync;

        public const int VanillaIds = 1000000;
        public static float SpeedCheat = 1f;
        public static bool FreeBuild;

        static Buildings()
        {
            MarsData.Changed += key =>
            {
                if (key == "" || key == BuildingsKey) { Data = MarsData.Get<State>(BuildingsKey); viewsDirty = true; }
                if (key == "" || key == StockKey) Items = MarsData.Get<Stock>(StockKey);
            };
            MarsNet.On(MarsNet.Kind.Request, (from, r) => { if (MarsNet.IsHost) HandleRequest(r, from); });
            MarsNet.On(MarsNet.Kind.Notice, (from, r) => { if (from == Lobby.Owner()) MarsWorld.Urgent(r.ReadString()); });
        }

        public static void Init() { }

        public static string Me
        {
            get { try { return MoonCrew.LocalPlayerId ?? "local"; } catch { return "local"; } }
        }

        public static Dictionary<string, int> Backpack(string player)
        {
            if (!Items.Backpacks.TryGetValue(player, out var b)) Items.Backpacks[player] = b = new Dictionary<string, int>();
            return b;
        }

        public static void StockChanged() => stockDirty = true;
        public static void Changed() { dirty = true; stockDirty = true; viewsDirty = true; }

        public static int Count(Dictionary<string, int> inv, string item) => inv != null && inv.TryGetValue(item, out var n) ? n : 0;
        public static void AddTo(Dictionary<string, int> inv, string item, int n) { inv[item] = Count(inv, item) + n; if (inv[item] <= 0) inv.Remove(item); }
        public static int Total(Dictionary<string, int> inv) => inv?.Values.Sum() ?? 0;

        // ------------------------------------------------------------------ requests (anyone → host)

        public enum Op : byte { Place = 1, Remove = 2, Use = 3, UseAlt = 4, Give = 5, Money = 6, Free = 7, Speed = 8, Clean = 9 }

        /// <summary>Ask the host to do something (the host just does it).</summary>
        public static void Ask(Op op, Action<BinaryWriter> body)
        {
            var ms = new MemoryStream(); var w = new BinaryWriter(ms);
            w.Write((byte)op); w.Write(Me); body?.Invoke(w); w.Flush();
            var bytes = ms.ToArray();
            if (MarsNet.IsHost) using (var r = new BinaryReader(new MemoryStream(bytes))) HandleRequest(r, Lobby.Me());
            else MarsNet.SendToHost(MarsNet.Kind.Request, x => x.Write(bytes));
        }

        static void HandleRequest(BinaryReader r, ulong from)
        {
            try
            {
                var op = (Op)r.ReadByte();
                string player = r.ReadString();
                string reply = Apply(op, player, r);
                if (!string.IsNullOrEmpty(reply)) Tell(from, reply);
            }
            catch (Exception e) { Plugin.Log.LogWarning("[build] request failed: " + e.Message); }
        }

        static void Tell(ulong to, string text)
        {
            if (to == Lobby.Me() || to == 0) MarsWorld.Urgent(text);
            else MarsNet.Send(to, MarsNet.Kind.Notice, w => w.Write(text));
        }

        static string Apply(Op op, string player, BinaryReader r)
        {
            var world = Commands.FindWorld();
            switch (op)
            {
                case Op.Place:
                {
                    string type = r.ReadString();
                    var pos = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    var rot = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    var def = MarsCatalog.Get(type);
                    if (def == null) return "Unknown building.";
                    string why = CanPlace(def, pos, out var mine);
                    if (why != null) return why;
                    string lack = MissingItems(def);
                    if (lack != null) return lack + ".";
                    if (!FreeBuild && !Pay(world, player, def.Price)) return $"Not enough money for the {def.Name} (${def.Price}).";
                    TakeItems(def);
                    var rec = new Rec { Id = Data.NextId++, Type = type, Owner = player, Px = pos.x, Py = pos.y, Pz = pos.z, Qx = rot.x, Qy = rot.y, Qz = rot.z, Qw = rot.w, Mine = mine?.Id ?? -1 };
                    if (mine != null) { rec.Px = mine.Position.x; rec.Py = mine.Position.y; rec.Pz = mine.Position.z; }
                    Data.List.Add(rec);
                    dirty = true; Sync(true);
                    Plugin.Log.LogMessage($"[build] {player} built {def.Name} #{rec.Id}");
                    return null;
                }
                case Op.Remove:
                {
                    int id = r.ReadInt32();
                    var rec = Data.List.Find(b => b.Id == id);
                    if (rec == null) return null;
                    if (rec.Vanilla) return "Remove it with the build tool, like any other building.";
                    if (rec.Owner == Aliens.Owner) return "The aliens would not like that.";
                    if (rec.Owner == Meteors.Owner) return Meteors.Take(rec, player);
                    var def = MarsCatalog.Get(rec.Type);
                    Data.List.Remove(rec);
                    // Contents go to the player who removed it, the price back to their wallet.
                    foreach (var kv in rec.Items) AddTo(Backpack(player), kv.Key, kv.Value);
                    if (!FreeBuild && def != null) Earn(world, player, def.Price);
                    dirty = true; stockDirty = true; Sync(true);
                    return $"{def?.Name ?? "Building"} removed, ${def?.Price ?? 0} refunded.";
                }
                case Op.Use:
                case Op.UseAlt:
                {
                    int id = r.ReadInt32();
                    var rec = Data.List.Find(b => b.Id == id);
                    if (rec == null) return null;
                    string msg = Use(rec, player, op == Op.UseAlt, world);
                    dirty = true; stockDirty = true; Sync(true);
                    return msg;
                }
                case Op.Give:
                {
                    string item = r.ReadString(); int n = r.ReadInt32();
                    if (!MarsItems.All.ContainsKey(item)) return "Unknown item. Items: " + string.Join(", ", MarsItems.All.Keys);
                    AddTo(Backpack(player), item, n);
                    stockDirty = true; Sync(true);
                    return $"+{n} {MarsItems.Name(item)}";
                }
                case Op.Money:
                {
                    double d = r.ReadDouble();
                    Earn(world, player, d);
                    return $"+${d:0}";
                }
                case Op.Clean:
                {
                    int id = r.ReadInt32();
                    var rec = Data.List.Find(b => b.Id == id);
                    if (rec == null || rec.Dust <= 0f) return null;
                    rec.Dust = 0f; dirty = true; Sync(true);
                    return $"{MarsCatalog.Get(rec.Type)?.Name ?? "Machine"} scrubbed clean.";
                }
                case Op.Free: FreeBuild = r.ReadBoolean(); VanillaBuild.SetPrices(); return "Free building " + (FreeBuild ? "ON" : "off") + " (for everyone in this world).";
                case Op.Speed: SpeedCheat = Mathf.Clamp(r.ReadSingle(), 0.1f, 50f); return $"Machines run at {SpeedCheat:0.#}x speed.";
            }
            return null;
        }

        static string Use(Rec rec, string player, bool alt, MoonWorld world)
        {
            var def = MarsCatalog.Get(rec.Type);
            return def == null ? null : Machines.Use(rec, def, player, alt, world);
        }

        // ------------------------------------------------------------------ money (host)

        public static bool Pay(MoonWorld world, string player, double dollars)
        {
            try
            {
                double have = Il2CppSystem.Decimal.ToDouble(world.WalletDollars(player));
                if (have + 0.001 < dollars) return false;
                world.ChargeBuild(player, Dec(dollars));
                return true;
            }
            catch (Exception e) { Plugin.Log.LogWarning("[build] payment failed: " + e.Message); return false; }
        }

        /// <summary>
        /// Add money to the team wallet. MoonWorld.DevAddMoney did nothing in the release game (2026-11-11 report:
        /// "money" asked for $10,000, the wallet stayed at $0), so the team balance is set directly and checked.
        /// </summary>
        public static void Earn(MoonWorld world, string player, double dollars)
        {
            if (world == null || dollars == 0) return;
            try
            {
                double before = Il2CppSystem.Decimal.ToDouble(world.WalletDollars(player));
                double want = Math.Round(before + dollars, 2);
                world.SetTeamDollars(Dec(want));
                double after = Il2CppSystem.Decimal.ToDouble(world.WalletDollars(player));
                if (Math.Abs(after - want) < 0.5) return;
                world.DevAddMoney(player, Dec(dollars));
                after = Il2CppSystem.Decimal.ToDouble(world.WalletDollars(player));
                Log.Once("earn-check", $"[build] wallet did not change as asked: before ${before:0.##}, wanted ${want:0.##}, now ${after:0.##}");
            }
            catch (Exception e) { Plugin.Log.LogWarning("[build] payout failed: " + e.Message); }
        }

        /// <summary>A game Decimal from a number, through its text (the struct constructors are not reliable through interop).</summary>
        public static Il2CppSystem.Decimal Dec(double v)
        {
            long cents = (long)Math.Round(v * 100.0);
            if (cents % 100 == 0) { Il2CppSystem.Decimal d = (long)(cents / 100); return d; }   // whole dollars: the runtime's own conversion
            bool neg = cents < 0; ulong m = (ulong)Math.Abs(cents);
            return new Il2CppSystem.Decimal((int)(m & 0xFFFFFFFF), (int)(m >> 32), 0, neg, 2);
        }

        public static double Wallet()
        {
            try { return Il2CppSystem.Decimal.ToDouble(Commands.FindWorld().WalletDollars(Me)); } catch { return 0; }
        }

        // ------------------------------------------------------------------ item costs (taken from the crew storage)

        /// <summary>What the crew storage still lacks to build this (null = it has everything).</summary>
        public static string MissingItems(MarsCatalog.Def def)
        {
            if (FreeBuild || def.ItemCost.Count == 0) return null;
            var lack = def.ItemCost.Where(kv => Count(Items.Storage, kv.Key) < kv.Value)
                .Select(kv => $"{kv.Value} {MarsItems.Name(kv.Key).ToLowerInvariant()} (have {Count(Items.Storage, kv.Key)})").ToList();
            return lack.Count == 0 ? null : "Needs in the crew storage: " + string.Join(", ", lack);
        }

        static void TakeItems(MarsCatalog.Def def)
        {
            if (FreeBuild) return;
            foreach (var kv in def.ItemCost) AddTo(Items.Storage, kv.Key, -Math.Min(kv.Value, Count(Items.Storage, kv.Key)));
            if (def.ItemCost.Count > 0) stockDirty = true;
        }

        // ------------------------------------------------------------------ placement rules (everyone checks, host decides)

        public static string CanPlace(MarsCatalog.Def def, Vector3 pos, out Mines.Mine mine)
        {
            mine = null;
            if (def.NeedsMine)
            {
                var m = Mines.Nearest(pos, 6f);
                mine = m;
                if (m == null) return $"The {def.Name.ToLowerInvariant()} must be built on a mine.";
                if (Data.List.Any(b => b.Mine == m.Id)) return "This mine already has a drill.";
                pos = m.Position;
            }
            else if (Mines.Nearest(pos, 5f + def.Radius) != null) return "Too close to a mine (keep mines free for drills).";
            foreach (var c in Look.All<MoonCollector>())
                if (Look.InScene(c.gameObject) && Vector3.Distance(c.transform.position, pos) < 22f) return "Too close to the Collector.";
            foreach (var b in Data.List)
            {
                var other = MarsCatalog.Get(b.Type);
                if (Vector3.Distance(b.Pos, pos) < def.Radius + (other?.Radius ?? 2f)) return "Too close to another building.";
            }
            try
            {
                var t = Commands.FindWorld().Terrain;
                var up = pos.normalized;
                var e = Vector3.Cross(up, Vector3.forward); if (e.sqrMagnitude < 1e-4f) e = Vector3.right; e.Normalize();
                var n = Vector3.Cross(up, e);
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (var off in new[] { e + n, e - n, -e + n, -e - n })
                {
                    float h = t.SurfacePoint((pos + off * def.Radius * 0.8f).normalized).magnitude;
                    lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h);
                }
                if (!def.NeedsMine && hi - lo > 1.4f) return "The ground is too steep here.";
            }
            catch { }
            return null;
        }

        // ------------------------------------------------------------------ production (host)

        public static void Tick(float now, float dt)
        {
            if (!MarsData.Loaded) return;
            if (MarsNet.IsHost && now >= nextProduction)
            {
                float step = lastProduction > 0f ? now - lastProduction : 0.5f;
                lastProduction = now;
                nextProduction = now + 0.5f;
                Produce(Mathf.Clamp(step, 0f, 2f) * SpeedCheat);
            }
            if (MarsNet.IsHost && now >= nextWorldSync)
            {
                nextWorldSync = now + 1f; SyncWithWorld();
                if (Aliens.EnsureHost(Data.List, ref Data.NextId)) { dirty = true; viewsDirty = true; }
            }
            if (MarsNet.IsHost) Sync(false);
            UpdateViews(dt);
        }

        /// <summary>Host: keep our records in step with the buildings the game placed or removed with its own tool.</summary>
        static bool worldSynced;

        static void SyncWithWorld()
        {
            if (!VanillaBuild.Ready) return;
            try { SyncWithWorldOnce(); } finally { worldSynced = true; }
        }

        static void SyncWithWorldOnce()
        {
            var world = Commands.FindWorld();
            var placed = VanillaBuild.Current(world);
            var seen = new HashSet<int>();
            foreach (var p in placed)
            {
                var e = VanillaBuild.ByKind(p.Kind);
                if (e == null) continue;
                int id = VanillaIds + p.Id;
                seen.Add(id);
                var rec = Data.List.Find(b => b.Id == id);
                if (rec == null)
                {
                    rec = new Rec { Id = id, Vanilla = true, Type = e.Def.Id, Owner = p.Owner };
                    Data.List.Add(rec);
                    Plugin.Log.LogMessage($"[build] {p.Owner} built {e.Def.Name} (depot {p.Id})");
                    if (worldSynced) TakeItems(e.Def);   // not for buildings found on the first pass after loading
                    dirty = true;
                }
                if ((rec.Pos - p.Pos).sqrMagnitude > 0.01f || rec.Mine < 0 && e.Def.NeedsMine)
                {
                    rec.Px = p.Pos.x; rec.Py = p.Pos.y; rec.Pz = p.Pos.z;
                    rec.Qx = p.Rot.x; rec.Qy = p.Rot.y; rec.Qz = p.Rot.z; rec.Qw = p.Rot.w;
                    if (e.Def.NeedsMine)
                    {
                        var m = Mines.Nearest(p.Pos, 6f);
                        int mine = m != null && !Data.List.Any(b => b != rec && b.Mine == m.Id) ? m.Id : -1;
                        if (mine != rec.Mine) { rec.Mine = mine; if (mine >= 0) rec.Progress = 0f; }
                    }
                    dirty = true; viewsDirty = true;
                }
            }
            for (int i = Data.List.Count - 1; i >= 0; i--)
            {
                var b = Data.List[i];
                if (!b.Vanilla || seen.Contains(b.Id)) continue;
                // Removed with the game's tool (it refunds the price): whatever was inside goes to the crew storage.
                foreach (var kv in b.Items) AddTo(Items.Storage, kv.Key, kv.Value);
                if (b.Items.Count > 0) stockDirty = true;
                Data.List.RemoveAt(i);
                dirty = true; viewsDirty = true;
            }
        }

        public const float JamSeconds = 75f;      // a full storm jams an unprotected machine in this time
        public const float BaseCleanSeconds = 3.5f;   // holding E this long scrubs a machine (before upgrades)
        static float cleanCached = BaseCleanSeconds, cleanCheckedAt = -10f;

        /// <summary>Scrubbing gets faster with the game's cleaning width upgrades.</summary>
        public static float CleanSeconds
        {
            get
            {
                float now = Time.realtimeSinceStartup;
                if (now - cleanCheckedAt < 5f) return cleanCached;
                cleanCheckedAt = now;
                try { cleanCached = BaseCleanSeconds / Mathf.Clamp(Commands.FindWorld().CleaningWidthMultiplier(Me), 1f, 3f); }
                catch { cleanCached = BaseCleanSeconds; }
                return cleanCached;
            }
        }

        public static bool Jams(Rec b) => MarsCatalog.Get(b.Type)?.Jams ?? false;
        public static bool Jammed(Rec b) => b.Dust >= 1f;

        static readonly Dictionary<int, (float until, bool roofed)> roofCache = new Dictionary<int, (float, bool)>();
        static bool Roofed(Rec b)
        {
            float now = Time.realtimeSinceStartup;
            if (roofCache.TryGetValue(b.Id, out var c) && now < c.until) return c.roofed;
            bool roofed = false;
            try
            {
                float top = Models.Get(MarsCatalog.Get(b.Type).Model).Bounds.max.y + 0.4f;
                var up = b.Pos.normalized;
                roofed = Shelter.Roofed(b.Pos + up * top, up);
            }
            catch { }
            roofCache[b.Id] = (now + 5f, roofed);
            return roofed;
        }

        static int lastPowerHash;

        static void Produce(float dt)
        {
            float storm = Haze.Storm;
            foreach (var b in Data.List)
            {
                if (Jams(b) && storm > 0.25f && !Roofed(b) && !Shields.Covered(b.Pos))
                {
                    float before = b.Dust;
                    b.Dust = Mathf.Min(1f, b.Dust + dt / SpeedCheatSafe() * storm / JamSeconds);
                    if (before < 1f && b.Dust >= 1f) { dirty = true; Plugin.Log.LogMessage($"[build] {b.Type} #{b.Id} jammed by the storm"); }
                    if ((int)(before * 10) != (int)(b.Dust * 10)) dirty = true;
                }
            }
            Power.Step(Data.List, dt);
            int hash = 17;
            foreach (var b in Data.List)
            {
                var def = MarsCatalog.Get(b.Type);
                if (def == null) continue;
                if (def.Role == MarsCatalog.Role.Shield)
                {
                    bool up = Shields.Wanted && !Jammed(b) && b.Supply >= 0.99f;
                    if (up) Goals.ShieldRaised();
                    if (up != b.Active) { b.Active = up; dirty = true; Plugin.Log.LogMessage($"[shield] #{b.Id} dome {(up ? "up" : "down")}"); }
                }
                hash = hash * 31 + (int)(b.Supply * 20f) + (int)(b.Energy / 5f) * 7 + (b.Burn > 0 ? 3 : 0);
                if (Jammed(b)) continue;
                if (Machines.Produce(b, def, dt, Power.Factor(b, def))) dirty = true;
            }
            if (hash != lastPowerHash) { lastPowerHash = hash; dirty = true; }
        }

        static float SpeedCheatSafe() => Mathf.Max(0.1f, SpeedCheat);   // the speed cheat does not make storms faster

        static void Sync(bool now)
        {
            if (!MarsNet.IsHost) return;
            float t = Time.realtimeSinceStartup;
            if (!now && t < nextSync) return;
            nextSync = t + 1f;
            if (dirty) { MarsData.Set(BuildingsKey, Data); dirty = false; }
            if (stockDirty) { MarsData.Set(StockKey, Items); stockDirty = false; }
        }

        // ------------------------------------------------------------------ views (everyone)

        static readonly Dictionary<int, GameObject> views = new Dictionary<int, GameObject>();
        static bool viewsDirty = true;
        static float nextMineCull, nextFind;

        static void UpdateViews(float dt)
        {
            if (viewsDirty)
            {
                viewsDirty = false;
                var ids = new HashSet<int>(Data.List.Select(b => b.Id));
                foreach (var id in views.Keys.ToList())
                    if (!ids.Contains(id)) { if (views[id] != null && id < VanillaIds) UnityEngine.Object.Destroy(views[id]); views.Remove(id); }
                foreach (var b in Data.List)
                {
                    if (b.Vanilla || views.TryGetValue(b.Id, out var v) && v != null) continue;
                    var def = MarsCatalog.Get(b.Type);
                    if (def == null) continue;
                    try
                    {
                        var go = Models.Spawn(def.Model, b.Pos, b.Rot);
                        go.name = $"Mars {def.Name} #{b.Id}";
                        views[b.Id] = go;
                        if (b.Owner == Aliens.Owner) Aliens.Decorate(go);
                        if (b.Owner == Meteors.Owner) Meteors.Decorate(go);
                    }
                    catch (Exception e) { Log.Once("view-" + b.Type, $"[build] {def.Name} model failed: {e.Message}"); }
                }
            }
            // Game-built machines: find the objects the game made from our prefabs (once per new building).
            if (Time.realtimeSinceStartup >= nextFind && Data.List.Any(b => b.Vanilla && (!views.TryGetValue(b.Id, out var v) || v == null)))
            {
                nextFind = Time.realtimeSinceStartup + 1.5f;
                var found = VanillaBuild.Instances();
                var taken = new HashSet<IntPtr>(views.Values.Where(x => x != null).Select(x => x.Pointer));
                foreach (var b in Data.List)
                {
                    if (!b.Vanilla || views.TryGetValue(b.Id, out var have) && have != null) continue;
                    GameObject best = null; float bd = 5f;
                    foreach (var (type, go) in found)
                    {
                        if (type != b.Type || taken.Contains(go.Pointer)) continue;
                        float d = Vector3.Distance(go.transform.position, b.Pos);
                        if (d < bd) { bd = d; best = go; }
                    }
                    if (best != null) { views[b.Id] = best; taken.Add(best.Pointer); Models.Track(best); if (b.Type == "lightpole") LightPoles.Attach(best); }
                }
            }
            // Mines: hidden under their drill (the survey stake would poke through) and when far away (frame rate).
            if (Time.realtimeSinceStartup >= nextMineCull)
            {
                nextMineCull = Time.realtimeSinceStartup + 1f;
                var cam = Camera.main;
                var cp = cam != null ? cam.transform.position : Vector3.zero;
                foreach (var m in Mines.All)
                {
                    if (m.View == null) continue;
                    bool show = !Data.List.Any(b => b.Mine == m.Id) && (cam == null || Vector3.Distance(cp, m.Position) < 500f);
                    if (m.View.activeSelf != show) m.View.SetActive(show);
                }
            }
            // Moving parts only turn while the machine works.
            foreach (var b in Data.List)
                if (views.TryGetValue(b.Id, out var v) && v != null)
                {
                    var def = MarsCatalog.Get(b.Type);
                    bool run = def != null && !Jammed(b) && (def.Role == MarsCatalog.Role.Generator ? b.Burn > 0f : def.Role == MarsCatalog.Role.Shield ? b.Active : Machines.Busy(b, def) && b.Supply > 0.01f || b.Progress > 0.001f);
                    Models.SetRunning(v, run);
                }
        }

        public static void ClearViews()
        {
            if (MarsNet.IsHost && MarsData.Loaded) Sync(true);   // nothing unsaved is lost when leaving
            foreach (var kv in views) if (kv.Value != null && kv.Key < VanillaIds) UnityEngine.Object.Destroy(kv.Value);
            views.Clear(); viewsDirty = true; worldSynced = false;
            Data = new State(); Items = new Stock();
        }

        public static Rec Target(Vector3 eye, Vector3 forward, float reach)
        {
            Rec best = null; float bestScore = float.MaxValue;
            foreach (var b in Data.List)
            {
                var def = MarsCatalog.Get(b.Type);
                var to = b.Pos + b.Pos.normalized * 1.2f - eye;
                float d = to.magnitude;
                if (d > reach + (def?.Radius ?? 2f)) continue;
                float ang = Vector3.Angle(forward, to);
                if (ang > 40f) continue;
                float score = ang + d * 2f;
                if (score < bestScore) { bestScore = score; best = b; }
            }
            return best;
        }

        public static string Describe(Rec b)
        {
            var def = MarsCatalog.Get(b.Type);
            return def == null ? b.Type : Machines.Describe(b, def);
        }

        public static string DustLine(Rec b)
        {
            if (!Jams(b) || b.Dust < 0.05f) return "";
            return Jammed(b) ? "  <color=#FF6B5A><b>JAMMED</b> by storm dust</color>" : $"  <color=#FFC266>dusty {b.Dust * 100f:0}%</color>";
        }

        public static string Hint(Rec b, string useKey)
        {
            var def = MarsCatalog.Get(b.Type);
            return def == null ? "" : Machines.Hint(b, def, useKey);
        }
    }
}
