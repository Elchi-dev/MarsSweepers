using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using MoonCleaners;
using UnityEngine;

namespace MarsMod
{
    /// <summary>The dev terminal's commands. Everything here is local to your own game.</summary>
    internal static class Commands
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static readonly (string usage, string what)[] Help =
        {
            ("help", "this list"),
            ("info", "where you are: position, altitude, latitude/longitude, speed, sun, storm"),
            ("mars", "mod status: version, world, slot, hooks"),
            ("mars slot <n>", "make empty save slot n a Mars world (if the Moon or Mars question did not show up)"),
            ("storm on | now | off | stop | auto | <0-1>", "storm with a 20 s front / instantly / dies down / gone / back to schedule / fixed level"),
            ("storm", "storm status and the next scheduled storm"),
            ("net", "co-op channel status, pings every crew member"),
            ("data | data test", "Mars save data status / (host) write a test section that the crew should receive"),
            ("model <name> | model list | model clear", "place a Mars model in front of you (look test of the Blender models)"),
            ("money [amount]", "CHEAT: add money (default $10,000)"),
            ("unlock", "CHEAT: buy every upgrade in the upgrade tree for free"),
            ("give <item> [count]", "CHEAT: put Mars items in your backpack (give alone lists them)"),
            ("goals", "the Mars contract goals and the bonus for each"),
            ("power", "every power grid: what it makes, uses and stores"),
            ("belts", "research: belts near you and what the game says about the nearest Mars building's ports"),
            ("ending", "CHEAT (host): play the Mars ending now, the Moon in the sky gets it"),
            ("freebuild [on|off] | fastmachines <x>", "CHEAT: Mars buildings cost nothing / machines run x times faster"),
            ("build <id> | buildings", "place a Mars building directly / list all of them"),
            ("mines | tp mine [id]", "list the nearest mines / teleport to one"),
            ("research", "study the game's machines and build menu, render reference pictures (for new Mars machines)"),
            ("collect", "write everything needed for the next features: economy, belts, machines, power, a snapshot"),
            ("market", "Mars market prices, what moved them, and any boom or crash going on"),
            ("economy", "write the upgrade tree, prices and money numbers to research/economy.txt (works in Moon worlds too)"),
            ("terrain", "check the base site: ground vs. base floors, and whether the valley rim is walkable"),
            ("config", "where this world's settings are saved (every Mars world has its own)"),
            ("time [morning|noon|evening|night|<0-1>]", "show or set the time of day (host only)"),
            ("haze <0-1> | on | off | reset", "haze strength (default 0.5) / back to default colour and strength"),
            ("haze color <r> <g> <b>", "haze colour, 0-1 or 0-255, e.g. haze color 0 0 255"),
            ("haze test | debug", "turn the haze magenta for 10 s / show what the haze is doing"),
            ("spawn <cube|sphere|cylinder|capsule> [size] [colour] [look|here|x y z] [phys]", "place a test block (default: cube, 1 m, orange, where you look)"),
            ("blocks | del | clearblocks", "list blocks, remove the last one, remove all"),
            ("tp <x> <y> <z> | tp up <m> | tp look | tp base", "teleport"),
            ("fly [on|off] | noclip [on|off] | speed <x>", "movement helpers"),
            ("settings | set <name> <value>", "read / change MoonSettings live (terrain values only apply to a new world load)"),
            ("snap | tint", "MarsProbe: full snapshot / Mars tint test"),
            ("clear | close", "clear the terminal / close it"),
        };

        public static void Run(string line, Action<string> say)
        {
            var a = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (a.Length == 0) return;
            Plugin.Log.LogInfo("[terminal] > " + line);
            string c = a[0].ToLowerInvariant();
            string Arg(int i) => i < a.Length ? a[i].ToLowerInvariant() : "";
            switch (c)
            {
                case "help": case "?":
                    foreach (var (u, w) in Help) say($"  {u,-58} {w}");
                    say("Ctrl+Shift+K or Esc closes the terminal. Up/Down = history, Left/Right/Home/End move the cursor (Ctrl = by word),");
                    say("Ctrl+V paste, Ctrl+C copy the line (empty line: the last answer), Ctrl+Shift+C copy everything, Ctrl+L clear.");
                    break;
                case "clear": Terminal.Clear(); break;
                case "close": MarsWorld.Defer(Terminal.CloseNow); break;
                case "info": case "pos": Info(say); break;
                case "mars": case "status":
                    if (Arg(1) == "slot") { say(MakeMarsSlot(Arg(2))); break; }
                    Status(say); break;
                case "storm": say(a.Length > 1 ? Haze.Command(Arg(1)) : Haze.Info()); break;
                case "haze": HazeCmd(a, say); break;
                case "net": MarsNet.PingAll(); foreach (var l in MarsNet.Status()) say(l); say("(pinged everyone; type 'net' again in a second for the times)"); break;
                case "data":
                    if (Arg(1) == "test")
                    {
                        if (!MarsNet.IsHost) { say("Only the host writes Mars data. Ask the host to run 'data test', then type 'data' here."); break; }
                        MarsData.Set("test", new Dictionary<string, string> { ["written"] = DateTime.Now.ToString("HH:mm:ss"), ["by"] = Environment.UserName });
                        say("Test section written and sent to the crew. On a crew PC, 'data' should now show 'test'.");
                    }
                    foreach (var l in MarsData.Status()) say(l);
                    break;
                case "model": ModelCmd(a, say); break;
                case "money": { double n = a.Length > 1 ? F(a[1], 10000f) : 10000f; Buildings.Ask(Buildings.Op.Money, w => w.Write(n)); say($"Asked for ${n:N0}."); break; }
                case "unlock": Unlock(say); break;
                case "give":
                {
                    if (a.Length < 2) { say("give <item> [count]   items: " + string.Join(", ", MarsItems.All.Keys)); break; }
                    string item = Arg(1); int n = a.Length > 2 ? (int)F(a[2], 10f) : 20;
                    Buildings.Ask(Buildings.Op.Give, w => { w.Write(item); w.Write(n); });
                    break;
                }
                case "freebuild": { bool on = Arg(1) != "off"; Buildings.Ask(Buildings.Op.Free, w => w.Write(on)); break; }
                case "speed2": case "fastmachines": { float x = a.Length > 1 ? F(a[1], 5f) : 5f; Buildings.Ask(Buildings.Op.Speed, w => w.Write(x)); break; }
                case "build": say(BuildMode.Place(Arg(1))); break;
                case "buildings":
                    foreach (var b in Buildings.Data.List) say($"  #{b.Id} {b.Type}{(b.Vanilla ? "" : " (old N menu)")} by {b.Owner}{(b.Mine >= 0 ? ", mine " + b.Mine : "")}: {MarsItems.List(b.Items)}");
                    say($"{Buildings.Data.List.Count} Mars building(s). Crew storage: {MarsItems.List(Buildings.Items.Storage)}");
                    foreach (var l in VanillaBuild.Status()) say(l);
                    break;
                case "ending": if (!MarsNet.IsHost) { say("Only the host can start the ending."); break; } Ending.Fire(); say("Ending in " + Ending.Countdown + " s (cheat: no cannon needed)."); break;
                case "belts": { var w0 = FindWalker(); foreach (var l in BeltResearch.Report(w0 != null ? w0.transform.position : Vector3.zero)) say(l); break; }
                case "goals": foreach (var l in Goals.Report()) say(l); say(Goals.Current()); break;
                case "power": foreach (var l in Power.Report()) say(l); break;
                case "mines":
                {
                    var w0 = FindWalker(); var p0 = w0 != null ? w0.transform.position : Vector3.zero;
                    int i = 0;
                    foreach (var m in Mines.All.OrderBy(m => Vector3.Distance(m.Position, p0)).Take(8))
                        say($"  mine {m.Id}{(m.Home ? " (home)" : "")}: {MarsItems.Name(m.Ore)}, {Vector3.Distance(m.Position, p0):0} m to the {Haze.Compass((m.Position - p0).normalized)}{(i++ == 0 ? "  ← tp mine" : "")}");
                    say($"{Mines.All.Count} mines on this world. 'tp mine' goes to the nearest, 'tp mine <id>' to one.");
                    break;
                }
                case "research": Research.Request(say); break;
                case "collect": foreach (var l in Economy.Collect(true)) say(l); break;
                case "deep": foreach (var l in Deep.Dump(Arg(1))) say(l); break;
                case "market": foreach (var l in Market.Report()) say(l); break;
                case "economy": foreach (var l in Economy.Dump()) say(l); break;
                case "terrain": foreach (var l in TerrainReport()) say(l); break;
                case "config": say("world settings file: " + MarsProfile.WorldConfigPath); say("PC settings (volume, fog quality): BepInEx/config/marsmod.mars.cfg"); break;
                case "time": { SetTime(Arg(1), out string msg); say(msg); break; }
                case "spawn": Spawn(a, say); break;
                case "blocks": Blocks.List(say); break;
                case "del": say(Blocks.RemoveLast()); break;
                case "clearblocks": say(Blocks.Clear()); break;
                case "tp": Teleport(a, say); break;
                case "fly": Walker(w => { bool on = Arg(1) != "off"; w.SetFlying(on, false); say("Flying " + (on ? "on" : "off")); }, say); break;
                case "noclip": Walker(w => { bool on = Arg(1) != "off"; w.SetFlying(on, on); say("Noclip " + (on ? "on (fly through everything)" : "off")); }, say); break;
                case "speed":
                    Walker(w =>
                    {
                        float v = F(Arg(1), 1f);
                        w.DevSpeedMultiplier = v;
                        try { w.FlySpeed = 10f * v; } catch { }
                        say($"Speed x{v}");
                    }, say);
                    break;
                case "settings": ListSettings(say); break;
                case "set": SetSetting(a, say); break;
                case "snap": say(Probe("RequestSnapshot") ?? "MarsProbe is not installed."); break;
                case "tint": say(Probe("ToggleTint") != null ? "Toggled the MarsProbe tint test." : "MarsProbe is not installed."); break;
                default: say($"Unknown command '{c}'. Type help."); break;
            }
        }

        static float F(string s, float fallback) =>
            float.TryParse(s, NumberStyles.Float, Inv, out float v) ? v : fallback;

        // ------------------------------------------------------------------ player

        /// <summary>Set the day time for the whole crew (host). "morning" = sun about 30° up and rising where you stand.</summary>
        internal static bool SetTime(string arg, out string msg)
        {
            try
            {
                var solar = Look.All<MoonSolarSystem>().FirstOrDefault(x => Look.InScene(x.gameObject));
                if (solar == null) { msg = "No sun here (are you in a world?)."; return false; }
                var w = FindWalker();
                var cam = Camera.main;
                var up = (w != null ? w.transform.position : cam != null ? cam.transform.position : Vector3.up).normalized;
                float Elev(float f) => Mathf.Asin(Mathf.Clamp(Vector3.Dot(MoonSolarSystem.SunDirectionAt(f), up), -1f, 1f)) * Mathf.Rad2Deg;
                float now = solar.LunarTime;
                if (string.IsNullOrEmpty(arg)) { msg = $"time {now:0.000} of the day, sun {Elev(now):0.0}° above your horizon. 'time morning|noon|evening|night|<0-1>' changes it."; return true; }
                if (Lobby.IsClient) { msg = "Only the host can change the time."; return false; }

                float target;
                if (!float.TryParse(arg, NumberStyles.Float, Inv, out target))
                {
                    const int N = 720;
                    var e = new float[N];
                    for (int i = 0; i < N; i++) e[i] = Elev(i / (float)N);
                    int best = 0; float bestScore = float.MaxValue;
                    for (int i = 0; i < N; i++)
                    {
                        bool rising = e[(i + 1) % N] > e[i];
                        float score;
                        switch (arg)
                        {
                            case "morning": case "day": score = rising ? Mathf.Abs(e[i] - 30f) : 999f; break;
                            case "noon": score = -e[i]; break;
                            case "evening": case "dusk": case "sunset": score = !rising ? Mathf.Abs(e[i] - 3f) : 999f; break;
                            case "dawn": case "sunrise": score = rising ? Mathf.Abs(e[i] + 1f) : 999f; break;
                            case "night": case "midnight": score = e[i]; break;
                            default: msg = "Usage: time morning | noon | evening | dawn | night | <0-1>"; return false;
                        }
                        if (score < bestScore) { bestScore = score; best = i; }
                    }
                    target = best / (float)N;
                }
                solar.SetLunarTime(Mathf.Repeat(target, 1f));
                msg = $"Time set to {target:0.000} (sun {Elev(target):0.0}° above your horizon).";
                Plugin.Log.LogMessage("[time] " + msg);
                return true;
            }
            catch (Exception e) { msg = "Could not change the time: " + (e.InnerException ?? e).Message; return false; }
        }

        /// <summary>Is the base site intact, and can you walk out of the home valley? (also logged once per Mars world)</summary>
        internal static IEnumerable<string> TerrainReport()
        {
            var world = FindWorld();
            if (world == null) { yield return "No world here."; yield break; }
            var t = world.Terrain;
            float limit = world.settings.slopeLimit;
            yield return $"terrain profile {MarsState.CurrentProfile}, seed {world.settings.seed}, walkable slope {limit:0}°";

            MoonCollector main = null; float best = float.MaxValue;
            var w = FindWalker();
            foreach (var c in Look.All<MoonCollector>().Where(x => Look.InScene(x.gameObject)))
            {
                var pos = c.transform.position;
                var dir = pos.normalized;
                // Ground just around the station: is it poking up through the floor?
                float worst = float.MinValue;
                var tn = Vector3.Cross(dir, Vector3.up); if (tn.sqrMagnitude < 1e-4f) tn = Vector3.right; tn.Normalize();
                var tb = Vector3.Cross(dir, tn);
                for (int i = 0; i < 12; i++)
                {
                    float a = i * Mathf.PI / 6f;
                    foreach (float r in new[] { 3f, 7f })
                    {
                        var d2 = (pos + (tn * Mathf.Cos(a) + tb * Mathf.Sin(a)) * r).normalized;
                        worst = Mathf.Max(worst, t.SurfacePoint(d2).magnitude - pos.magnitude);
                    }
                }
                yield return $"{c.gameObject.name,-34} ground under it {t.SurfacePoint(dir).magnitude - pos.magnitude:+0.0;-0.0} m, highest ground within 7 m {worst:+0.0;-0.0} m vs. its origin";
                if (w != null) { float dd = (pos - w.transform.position).sqrMagnitude; if (dd < best) { best = dd; main = c; } }
            }
            if (w != null)
            {
                var wp = w.transform.position;
                yield return $"you: {wp.magnitude - t.SurfacePoint(wp.normalized).magnitude:+0.0;-0.0} m above the ground";
            }

            var centre = (main != null ? main.transform.position : w != null ? w.transform.position : Vector3.up * world.settings.radius).normalized;
            var east = Vector3.Cross(Vector3.up, centre); if (east.sqrMagnitude < 1e-4f) east = Vector3.right; east.Normalize();
            var north = Vector3.Cross(centre, east).normalized;
            float R = world.settings.radius;
            int walkable = 0; var steep = new List<string>();
            for (int k = 0; k < 16; k++)
            {
                float az = k * 22.5f;
                var heading = north * Mathf.Cos(az * Mathf.Deg2Rad) + east * Mathf.Sin(az * Mathf.Deg2Rad);
                var axis = Vector3.Cross(centre, heading).normalized;
                // Sustained slope over 15 m stretches (single bumps like a crater rim are fine to hop over).
                var hs = new List<float>();
                for (float dist = 0f; dist <= 480f; dist += 5f)
                    hs.Add(t.SurfacePoint(Quaternion.AngleAxis(dist / R * Mathf.Rad2Deg, axis) * centre).magnitude);
                float maxSlope = 0f, at = 0f;
                for (int i = 3; i < hs.Count; i++)
                {
                    float slope = Mathf.Atan2(Mathf.Abs(hs[i] - hs[i - 3]), 15f) * Mathf.Rad2Deg;
                    if (slope > maxSlope) { maxSlope = slope; at = i * 5f; }
                }
                if (maxSlope <= limit) walkable++;
                else steep.Add($"{az:0}° ({maxSlope:0}° at {at:0} m)");
            }
            yield return $"walking out of the valley: {walkable}/16 directions stay under {limit:0}°";
            if (steep.Count > 0) yield return "too steep: " + string.Join(", ", steep);
        }

        static readonly List<GameObject> placedModels = new List<GameObject>();

        static void ModelCmd(string[] a, Action<string> say)
        {
            string arg = a.Length > 1 ? a[1].ToLowerInvariant() : "list";
            if (arg == "list") { say("models: " + string.Join(", ", Models.Available())); return; }
            if (arg == "clear") { foreach (var g in placedModels) if (g != null) UnityEngine.Object.Destroy(g); say($"{placedModels.Count} model(s) removed."); placedModels.Clear(); return; }
            var cam = Camera.main;
            if (cam == null) { say("No camera."); return; }
            Vector3 pos;
            if (!Look2(out pos)) pos = cam.transform.position + cam.transform.forward * 8f;
            var up = pos.normalized;
            var fwd = Vector3.ProjectOnPlane(cam.transform.position - pos, up).normalized;   // face the player
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.Cross(up, Vector3.right);
            var world = FindWorld();
            if (world != null) { try { pos = world.Terrain.SurfacePoint(up); } catch { } }
            try
            {
                var go = Models.Spawn(arg, pos, Quaternion.LookRotation(fwd, up));
                placedModels.Add(go);
                var t = Models.Get(arg);
                say($"'{arg}' placed: {t.Triangles} triangles, ports: {string.Join(", ", t.Markers.Keys.Where(k => k.StartsWith("port_")))}. 'model clear' removes it.");
            }
            catch (Exception e) { say("Model failed: " + (e.InnerException ?? e).Message); Plugin.Log.LogWarning("[models] " + e); }
        }

        static void Unlock(Action<string> say)
        {
            var world = FindWorld();
            if (world == null) { say("Go into a world first."); return; }
            string me = Buildings.Me;
            int bought = 0;
            var nodes = MoonUpgradeTree.Nodes.Concat(MoonUpgradeTree.Gear).ToList();
            for (int round = 0; round < 12; round++)
            {
                int before = bought;
                foreach (var n in nodes)
                {
                    int max = n.maxLevel;
                    try { max = world.TreeMaxLevel(n); } catch { }
                    int level = world.TreeLevel(me, n.id);
                    while (level < max)
                    {
                        try { Buildings.Earn(world, me, 100000.0); } catch { }
                        string msg;
                        bool ok = world.TryPurchaseTreeNode(me, n.id, level + 1, out msg) || world.TryPurchaseTreeNode(me, n.id, level, out msg);
                        if (!ok) break;
                        int now = world.TreeLevel(me, n.id);
                        if (now <= level) break;
                        level = now; bought++;
                    }
                }
                if (bought == before) break;
            }
            say($"Bought {bought} upgrade level(s). (Money used for it was added first, so your balance is a bit higher now.)");
        }

        internal static MoonWalker FindWalker() => Look.All<MoonWalker>().FirstOrDefault(w => Look.InScene(w.gameObject));
        internal static MoonWorld FindWorld() => Look.All<MoonWorld>().FirstOrDefault(w => Look.InScene(w.gameObject));

        static void Walker(Action<MoonWalker> act, Action<string> say)
        {
            var w = FindWalker();
            if (w == null) { say("No player here (are you in a world?)."); return; }
            act(w);
        }

        static Vector3 lastPos;
        static float lastPosTime;
        public static float Speed;

        /// <summary>Called every frame: speed measurement and physics for spawned blocks.</summary>
        public static void Frame(float now, float dt)
        {
            Blocks.Frame(dt);
            if (now - lastPosTime < 0.25f) return;
            try
            {
                var w = FindWalker();
                if (w == null) return;
                var p = w.transform.position;
                if (lastPosTime > 0) Speed = (p - lastPos).magnitude / (now - lastPosTime);
                lastPos = p; lastPosTime = now;
            }
            catch { }
        }

        static void Info(Action<string> say)
        {
            var w = FindWalker();
            if (w == null) { say("No player here (are you in a world?)."); return; }
            var p = w.transform.position;
            var dir = p.normalized;
            float lat = Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;
            float lon = Mathf.Atan2(dir.z, dir.x) * Mathf.Rad2Deg;
            say($"position   {V(p)}   (distance from centre {p.magnitude:0.0} m)");
            try
            {
                var world = FindWorld();
                var ground = world.Terrain.SurfacePoint(dir);
                say($"altitude   {p.magnitude - ground.magnitude:0.0} m above the ground   (ground {ground.magnitude:0.0} m from centre)");
                say($"gravity    {world.settings.gravity:0.00} m/s²   planet radius {world.settings.radius:0} m   seed {world.settings.seed}");
            }
            catch (Exception e) { say("altitude   ? (" + e.Message + ")"); }
            say($"latitude   {lat:0.000}°   longitude {lon:0.000}°");
            say($"speed      {Speed:0.0} m/s   grounded {Safe(() => w.Grounded)}");
            say($"world      {(MarsState.CurrentIsMars ? "MARS" : "Moon")}, slot {MarsState.ActiveSlot}, {Haze.Info()}");
        }

        static string Safe<T>(Func<T> f) { try { return f()?.ToString(); } catch { return "?"; } }
        static string V(Vector3 v) => $"({v.x:0.0}, {v.y:0.0}, {v.z:0.0})";

        static void Status(Action<string> say)
        {
            say($"Mars Sweepers {Plugin.Version}{(Plugin.SafeMode ? "  (SAFE MODE this start)" : "")}");
            say($"hooks: {Patches.Ok} ok, {Patches.Failed.Count} failed" + (Patches.Failed.Count > 0 ? ": " + string.Join("; ", Patches.Failed) : ""));
            say($"world: {(MarsState.CurrentIsMars ? "MARS, terrain profile " + MarsState.CurrentProfile : "Moon")}, active slot {MarsState.ActiveSlot}, crew client: {Lobby.IsClient}");
            var mars = Enumerable.Range(0, MoonSaveSlots.Count + 1).Where(MarsState.IsMarsSlot).ToList();
            say("Mars slots: " + (mars.Count > 0 ? string.Join(", ", mars) : "none") + $"   (saves in {MarsState.MarsRoot})");
        }

        static void HazeCmd(string[] a, Action<string> say)
        {
            string arg = a.Length > 1 ? a[1].ToLowerInvariant() : "";
            if (arg == "on") { MarsProfile.HazeOn.Value = true; say("Haze on."); }
            else if (arg == "off") { MarsProfile.HazeOn.Value = false; say("Haze off."); }
            else if (arg == "test") say(Haze.Test());
            else if (arg == "debug") { foreach (var l in Haze.Debug()) say(l); }
            else if ((arg == "color" || arg == "colour") && a.Length >= 5)
            {
                MarsProfile.HazeDayColor.Value = $"{F(a[2], .84f).ToString(Inv)}, {F(a[3], .68f).ToString(Inv)}, {F(a[4], .54f).ToString(Inv)}";
                say("Haze colour " + MarsProfile.HazeDayColor.Value + " (saved to the config). 'haze reset' brings back the defaults.");
            }
            else if (float.TryParse(arg, NumberStyles.Float, Inv, out float v))
            {
                MarsProfile.HazeStrength.Value = Mathf.Clamp(v, 0f, 0.95f);
                say($"Haze strength {MarsProfile.HazeStrength.Value:0.00} (saved to the config).");
            }
            else if (arg == "reset")
            {
                MarsProfile.HazeDayColor.Value = (string)MarsProfile.HazeDayColor.DefaultValue;
                MarsProfile.HazeStrength.Value = (float)MarsProfile.HazeStrength.DefaultValue;
                MarsProfile.HazeOn.Value = true;
                say($"Haze back to the defaults: colour {MarsProfile.HazeDayColor.Value}, strength {MarsProfile.HazeStrength.Value:0.00}.");
            }
            else say($"Haze is {(MarsProfile.HazeOn.Value ? "on" : "off")}, strength {MarsProfile.HazeStrength.Value:0.00}, colour {MarsProfile.HazeDayColor.Value}.");
        }

        // ------------------------------------------------------------------ teleport

        static void Teleport(string[] a, Action<string> say)
        {
            var w = FindWalker();
            if (w == null) { say("No player here."); return; }
            Vector3 p = w.transform.position, target;
            string mode = a.Length > 1 ? a[1].ToLowerInvariant() : "";
            if (mode == "up") target = p + p.normalized * F(a.Length > 2 ? a[2] : "20", 20f);
            else if (mode == "look")
            {
                if (!Look2(out var hit)) { say("Nothing in sight within 2 km."); return; }
                target = hit + hit.normalized * 1.5f;
            }
            else if (mode == "base")
            {
                var c = Look.All<MoonCollector>().Where(x => Look.InScene(x.gameObject)).OrderBy(x => (x.transform.position - p).sqrMagnitude).FirstOrDefault();
                if (c == null) { say("No Collector found."); return; }
                var cp = c.transform.position;
                target = cp + cp.normalized * 3f;
            }
            else if (mode == "mine")
            {
                Mines.Mine m = null;
                if (a.Length > 2 && int.TryParse(a[2], out int mid)) m = Mines.ById(mid);
                else m = Mines.All.OrderBy(x => Vector3.Distance(x.Position, p)).FirstOrDefault();
                if (m == null) { say("No such mine (are mines generated yet? try 'mines')."); return; }
                target = m.Position + m.Up * 4f + Vector3.Cross(m.Up, Vector3.right).normalized * 5f;
            }
            else if (a.Length >= 4) target = new Vector3(F(a[1], p.x), F(a[2], p.y), F(a[3], p.z));
            else { say("Usage: tp x y z | tp up <m> | tp look | tp base"); return; }
            w.Teleport(target, true);
            say("Teleported to " + V(target));
        }

        /// <summary>Raycast from the camera along the view direction.</summary>
        public static bool Look2(out Vector3 point)
        {
            point = Vector3.zero;
            var cam = Camera.main;
            if (cam == null) return false;
            var physT = R.T("UnityEngine.Physics", "UnityEngine.PhysicsModule");
            var m = physT?.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(x =>
            {
                if (x.Name != "Raycast") return false;
                var ps = x.GetParameters();
                return ps.Length == 4 && ps[0].ParameterType == typeof(Vector3) && ps[2].ParameterType.IsByRef && ps[3].ParameterType == typeof(float);
            });
            if (m == null) return false;
            var hitT = m.GetParameters()[2].ParameterType.GetElementType();
            var origin = cam.transform.position + cam.transform.forward * 0.6f;
            var args = new object[] { origin, cam.transform.forward, Activator.CreateInstance(hitT), 2000f };
            if (!(bool)m.Invoke(null, args)) return false;
            point = (Vector3)hitT.GetProperty("point").GetValue(args[2]);
            return true;
        }

        // ------------------------------------------------------------------ spawn

        static readonly Dictionary<string, Color> Colours = new Dictionary<string, Color>
        {
            ["orange"] = new Color(.91f, .45f, .23f), ["red"] = new Color(.85f, .2f, .15f), ["green"] = new Color(.3f, .8f, .35f),
            ["blue"] = new Color(.25f, .45f, .95f), ["white"] = Color.white, ["black"] = new Color(.08f, .08f, .08f),
            ["yellow"] = new Color(1f, .85f, .2f), ["purple"] = new Color(.6f, .3f, .85f), ["grey"] = new Color(.5f, .5f, .5f),
            ["cyan"] = new Color(.2f, .85f, .9f), ["pink"] = new Color(1f, .5f, .75f),
        };

        static void Spawn(string[] a, Action<string> say)
        {
            var shapes = new Dictionary<string, PrimitiveType>
            {
                ["cube"] = PrimitiveType.Cube, ["sphere"] = PrimitiveType.Sphere, ["cylinder"] = PrimitiveType.Cylinder, ["capsule"] = PrimitiveType.Capsule,
            };
            var shape = PrimitiveType.Cube; string shapeName = "cube";
            float size = 1f; Color col = Colours["orange"]; string where = "look"; bool phys = false;
            var nums = new List<float>();
            for (int i = 1; i < a.Length; i++)
            {
                string t = a[i].ToLowerInvariant();
                if (shapes.TryGetValue(t, out var s)) { shape = s; shapeName = t; }
                else if (Colours.TryGetValue(t, out var c)) col = c;
                else if (t == "look" || t == "here") where = t;
                else if (t == "phys" || t == "physics") phys = true;
                else if (float.TryParse(t, NumberStyles.Float, Inv, out float v)) nums.Add(v);
                else { say($"Didn't understand '{t}'."); return; }
            }
            Vector3 pos;
            if (nums.Count >= 3) { if (nums.Count == 4) size = nums[0]; pos = new Vector3(nums[nums.Count - 3], nums[nums.Count - 2], nums[nums.Count - 1]); }
            else
            {
                if (nums.Count >= 1) size = nums[0];
                var w = FindWalker();
                if (w == null) { say("No player here."); return; }
                if (where == "here")
                {
                    var cam = Camera.main.transform;
                    pos = cam.position + cam.forward * (2f + size);
                }
                else if (!Look2(out pos)) { var cam = Camera.main.transform; pos = cam.position + cam.forward * 5f; }
                pos += pos.normalized * (size * 0.5f);
            }
            say(Blocks.Spawn(shape, shapeName, size, col, pos, phys));
        }

        // ------------------------------------------------------------------ settings

        static PropertyInfo[] SettingProps() => typeof(MoonSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(p => p.CanRead && p.CanWrite && (p.PropertyType == typeof(float) || p.PropertyType == typeof(int) || p.PropertyType == typeof(bool))).ToArray();

        static void ListSettings(Action<string> say)
        {
            var world = FindWorld();
            var s = world?.settings;
            if (s == null) { say("Not in a world."); return; }
            var parts = SettingProps().Select(p => $"{p.Name}={Convert.ToString(p.GetValue(s), Inv)}").ToList();
            for (int i = 0; i < parts.Count; i += 4) say("  " + string.Join("   ", parts.Skip(i).Take(4)));
        }

        static void SetSetting(string[] a, Action<string> say)
        {
            if (a.Length < 3) { say("Usage: set <name> <value>   (see 'settings')"); return; }
            var s = FindWorld()?.settings;
            if (s == null) { say("Not in a world."); return; }
            var p = SettingProps().FirstOrDefault(x => x.Name.Equals(a[1], StringComparison.OrdinalIgnoreCase));
            if (p == null) { say($"No setting '{a[1]}'."); return; }
            object v = p.PropertyType == typeof(bool) ? (object)(a[2] == "1" || a[2].ToLowerInvariant() == "true" || a[2].ToLowerInvariant() == "on")
                     : p.PropertyType == typeof(int) ? (object)int.Parse(a[2], Inv) : F(a[2], 0f);
            object old = p.GetValue(s);
            p.SetValue(s, v);
            say($"{p.Name}: {Convert.ToString(old, Inv)} → {Convert.ToString(v, Inv)}  (live, not saved; the next world load resets it)");
        }

        // ------------------------------------------------------------------ MarsProbe

        static string MakeMarsSlot(string arg)
        {
            if (!int.TryParse(arg, out int slot) || slot < 0 || slot > 9) return "Usage: mars slot <number>, e.g. 'mars slot 0' (slots count from 0, top to bottom).";
            if (MarsState.IsMarsSlot(slot)) return $"Slot {slot} is already a Mars world.";
            bool used = true;
            try { used = MoonSaveSlots.Used(slot); } catch { }
            if (used) return $"Slot {slot} holds a Moon world. Delete it in the menu first; Moon worlds are never turned into Mars.";
            MarsState.MarkMars(slot);
            Patches.Forget(slot);
            return $"Slot {slot} is now a Mars slot. Pick it in the menu and press Play.";
        }

        internal static string Probe(string method)
        {
            var t = Type.GetType("MarsProbe.Plugin, MarsProbe", false);
            var m = t?.GetMethod(method, BindingFlags.Public | BindingFlags.Static);
            if (m == null) return null;
            return m.Invoke(null, null)?.ToString() ?? "ok";
        }
    }

    /// <summary>Test blocks spawned from the terminal. They vanish when you leave the world.</summary>
    internal static class Blocks
    {
        class Block { public GameObject go; public string name; public object body; }
        static readonly List<Block> all = new List<Block>();
        static int counter;
        static MethodInfo addForce;
        static object accelerationMode;

        public static string Spawn(PrimitiveType shape, string shapeName, float size, Color col, Vector3 pos, bool phys)
        {
            var go = GameObject.CreatePrimitive(shape);
            go.name = $"Dev {shapeName} #{++counter}";
            var t = go.transform;
            t.position = pos;
            t.rotation = Quaternion.FromToRotation(Vector3.up, pos.normalized);
            t.localScale = Vector3.one * size;
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            var r = go.GetComponent(Il2CppInterop.Runtime.Il2CppType.Of<Renderer>())?.Cast<Renderer>();
            if (sh != null && r != null)
            {
                var m = new Material(sh);
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
                r.material = m;
            }
            object body = null;
            if (phys)
            {
                var rbT = R.T("UnityEngine.Rigidbody", "UnityEngine.PhysicsModule");
                if (rbT != null)
                {
                    body = R.Add(go, rbT);
                    R.TrySet(body, "useGravity", false); // planet gravity points to the centre; we apply it ourselves
                    R.TrySet(body, "mass", size * size * size * 50f);
                }
            }
            all.Add(new Block { go = go, name = go.name, body = body });
            return $"Spawned {go.name} ({size:0.##} m{(phys ? ", physics" : "")}) at ({pos.x:0.0}, {pos.y:0.0}, {pos.z:0.0}). 'del' removes it.";
        }

        public static void Frame(float dt)
        {
            if (all.Count == 0) return;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                var b = all[i];
                bool alive;
                try { alive = b.go != null && b.go.scene.IsValid(); } catch { alive = false; }
                if (!alive) { all.RemoveAt(i); continue; }
                if (b.body == null) continue;
                try
                {
                    if (addForce == null)
                    {
                        addForce = b.body.GetType().GetMethods().First(m => m.Name == "AddForce" && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == typeof(Vector3));
                        accelerationMode = Enum.ToObject(addForce.GetParameters()[1].ParameterType, 5);
                    }
                    float g = 3.71f;
                    try { g = Look.All<MoonWorld>().First(w => Look.InScene(w.gameObject)).settings.gravity; } catch { }
                    addForce.Invoke(b.body, new object[] { -b.go.transform.position.normalized * g, accelerationMode });
                }
                catch { b.body = null; }
            }
        }

        public static void List(Action<string> say)
        {
            if (all.Count == 0) { say("No blocks."); return; }
            foreach (var b in all)
            {
                try { var p = b.go.transform.position; say($"  {b.name} at ({p.x:0.0}, {p.y:0.0}, {p.z:0.0}){(b.body != null ? " physics" : "")}"); }
                catch { }
            }
        }

        public static string RemoveLast()
        {
            if (all.Count == 0) return "No blocks.";
            var b = all[all.Count - 1];
            all.RemoveAt(all.Count - 1);
            try { UnityEngine.Object.Destroy(b.go); } catch { }
            return "Removed " + b.name;
        }

        public static string Clear()
        {
            int n = all.Count;
            foreach (var b in all) { try { UnityEngine.Object.Destroy(b.go); } catch { } }
            all.Clear();
            return $"Removed {n} block(s).";
        }
    }
}
