using System;
using System.Collections.Generic;
using MoonCleaners;
using Steamworks;

namespace MarsMod
{
    /// <summary>
    /// The host writes "marsmod" into the Steam lobby data, crew members with the mod read it before
    /// their world is built. Format: "mars;&lt;terrainProfile&gt;;&lt;modVersion&gt;" or "moon;0;&lt;modVersion&gt;".
    /// </summary>
    internal static class Lobby
    {
        public const string Key = "marsmod";
        static string lastPublished;
        static bool dumped;

        static bool TryLobby(out CSteamID id)
        {
            id = default;
            try
            {
                if (!MoonSteamLobby.InLobby) return false;
                ulong raw = MoonSteamLobby.LobbyId;
                if (raw == 0) return false;
                id = new CSteamID { m_SteamID = raw };
                return true;
            }
            catch (Exception e) { Plugin.Log.LogWarning("Lobby lookup failed: " + e.Message); return false; }
        }

        public static bool IsClient
        {
            get { try { return MoonSteamLobby.InLobby && !MoonSteamLobby.IsHost; } catch { return false; } }
        }

        public static void Publish(bool mars, int profile)
        {
            try
            {
                if (!MoonSteamLobby.InLobby || !MoonSteamLobby.IsHost) return;
                if (!TryLobby(out var id)) return;
                string v = (mars ? "mars;" + profile : "moon;0") + ";" + Plugin.Version + (mars ? ";" + MarsState.StormSeed : "");
                if (v == lastPublished) return;
                bool ok = SteamMatchmaking.SetLobbyData(id, Key, v);
                lastPublished = v;
                Plugin.Log.LogMessage($"Lobby data {Key}={v} published (ok={ok})");
                DumpOnce(id);
            }
            catch (Exception e) { Plugin.Log.LogWarning("Publishing lobby data failed: " + e.Message); }
        }

        /// <summary>Read what the host announced. Null when the host doesn't run the Mars mod.</summary>
        /// <summary>Storm seed from the last Read() (0 when the host didn't send one).</summary>
        public static int HostStormSeed;

        public static (bool mars, int profile, string hostVersion)? Read()
        {
            try
            {
                if (!TryLobby(out var id)) return null;
                DumpOnce(id);
                string v = SteamMatchmaking.GetLobbyData(id, Key);
                if (string.IsNullOrEmpty(v)) return null;
                var p = v.Split(';');
                bool mars = p[0] == "mars";
                int profile = p.Length > 1 && int.TryParse(p[1], out var pr) ? pr : 1;
                HostStormSeed = p.Length > 3 && int.TryParse(p[3], out var seed) ? seed : 0;
                return (mars, profile, p.Length > 2 ? p[2] : "?");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Reading lobby data failed: " + e.Message); return null; }
        }

        public static void ResetPublished() => lastPublished = null;

        // ------------------------------------------------------------------ keep players without the mod out

        /// <summary>Added to the lobby's "version" while a Mars world is hosted. Players without the mod
        /// then see a different game version and the game keeps them out; players with the mod strip it again.</summary>
        public const string VersionSuffix = " +mars";

        /// <summary>Set from the moment the host picks a Mars slot until the menu is idle again.</summary>
        public static bool HostingMars;

        public static void RepublishVersion()
        {
            try
            {
                if (!MoonSteamLobby.InLobby || !MoonSteamLobby.IsHost || !TryLobby(out var id)) return;
                SteamMatchmaking.SetLobbyData(id, "version", UnityEngine.Application.version); // our prefix adds the suffix on Mars
            }
            catch (Exception e) { Plugin.Log.LogWarning("Version republish failed: " + e.Message); }
        }

        // ------------------------------------------------------------------ who in the crew has the mod

        public const string MemberKey = "marsmod";
        static ulong flaggedLobby;

        /// <summary>Every player with the mod marks themselves in the lobby, so the host can tell who is missing it.</summary>
        public static void FlagSelf()
        {
            try
            {
                if (!TryLobby(out var id) || id.m_SteamID == flaggedLobby) return;
                SteamMatchmaking.SetLobbyMemberData(id, MemberKey, Plugin.Version);
                flaggedLobby = id.m_SteamID;
            }
            catch { }
        }

        /// <summary>Steam ids of everyone in the lobby except us.</summary>
        public static List<ulong> OtherMembers()
        {
            var list = new List<ulong>();
            try
            {
                if (!TryLobby(out var id)) return list;
                ulong me = SteamUser.GetSteamID().m_SteamID;
                int n = SteamMatchmaking.GetNumLobbyMembers(id);
                for (int i = 0; i < n; i++)
                {
                    var m = SteamMatchmaking.GetLobbyMemberByIndex(id, i).m_SteamID;
                    if (m != me && m != 0) list.Add(m);
                }
            }
            catch { }
            return list;
        }

        public static ulong Owner()
        {
            try { return TryLobby(out var id) ? SteamMatchmaking.GetLobbyOwner(id).m_SteamID : 0; } catch { return 0; }
        }

        public static ulong Me()
        {
            try { return SteamUser.GetSteamID().m_SteamID; } catch { return 0; }
        }

        public static List<string> MembersWithoutMod()
        {
            var missing = new List<string>();
            try
            {
                if (!TryLobby(out var id)) return missing;
                int n = SteamMatchmaking.GetNumLobbyMembers(id);
                for (int i = 0; i < n; i++)
                {
                    var m = SteamMatchmaking.GetLobbyMemberByIndex(id, i);
                    if (!string.IsNullOrEmpty(SteamMatchmaking.GetLobbyMemberData(id, m, MemberKey))) continue;
                    string name;
                    try { name = MoonSteamLobby.PlayerName(m.m_SteamID); } catch { name = m.m_SteamID.ToString(); }
                    missing.Add(name);
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Crew check failed: " + e.Message); }
            return missing;
        }

        /// <summary>Diagnostics: log every key the game keeps in its lobby (to find its version check).</summary>
        static void DumpOnce(CSteamID id)
        {
            if (dumped) return;
            dumped = true;
            try
            {
                int n = SteamMatchmaking.GetLobbyDataCount(id);
                var parts = new List<string>();
                for (int i = 0; i < n; i++)
                {
                    if (SteamMatchmaking.GetLobbyDataByIndex(id, i, out string k, 256, out string v, 1024))
                        parts.Add(k + "=" + (v != null && v.Length > 60 ? v.Substring(0, 60) + "…" : v));
                }
                Plugin.Log.LogInfo($"[diag] lobby data ({n} keys): " + string.Join(" | ", parts));
            }
            catch (Exception e) { Plugin.Log.LogInfo("[diag] lobby data dump failed: " + e.Message); }
        }
    }
}
