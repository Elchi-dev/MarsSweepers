using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MoonCleaners;
using Steamworks;
using UnityEngine;

namespace MarsMod
{
    /// <summary>
    /// The Mars mod's own co-op channel. The game talks over Steam networking messages on channels 0 to 2;
    /// we use channel 7 next to it, through the game's own send function (no hooks) and Steam's receive call.
    /// The host owns the truth: crew members ask, the host decides and tells everyone.
    ///
    /// Message: 'M' 'S' version type payload…
    /// </summary>
    internal static class MarsNet
    {
        public const int Channel = 7;
        const byte Magic1 = (byte)'M', Magic2 = (byte)'S', Version = 1;

        public enum Kind : byte
        {
            Ping = 1, Pong = 2,
            Hello = 3,          // crew → host: I'm in, send me the Mars state
            Snapshot = 4,       // host → crew: everything (compressed)
            Section = 5,        // host → crew: one changed section of the Mars data
            Request = 6,        // crew → host: please do something (feature-specific payload)
            Notice = 7,         // host → one player: a message for their HUD
        }

        static readonly Dictionary<Kind, Action<ulong, BinaryReader>> handlers = new Dictionary<Kind, Action<ulong, BinaryReader>>();
        public static void On(Kind k, Action<ulong, BinaryReader> h) => handlers[k] = h;

        public static bool IsHost => !Lobby.IsClient;
        public static int Received { get; private set; }
        public static int Sent { get; private set; }
        public static string LastError { get; private set; } = "";
        static readonly Dictionary<ulong, float> pingSent = new Dictionary<ulong, float>();
        public static readonly Dictionary<ulong, float> RoundTrip = new Dictionary<ulong, float>();
        static readonly Il2CppStructArray<IntPtr> inbox = new Il2CppStructArray<IntPtr>(32);
        static bool brokenReceive;

        static MarsNet()
        {
            On(Kind.Ping, (from, r) => Send(from, Kind.Pong, w => w.Write(r.ReadSingle())));
            On(Kind.Pong, (from, r) => { float t = r.ReadSingle(); RoundTrip[from] = (Time.realtimeSinceStartup - t) * 1000f; });
        }

        // ------------------------------------------------------------------ sending

        public static bool Send(ulong to, Kind kind, Action<BinaryWriter> body, bool reliable = true)
        {
            try
            {
                var crew = MoonCrewSession.Instance;
                if (crew == null || to == 0) return false;
                var ms = new MemoryStream();
                var w = new BinaryWriter(ms);
                w.Write(Magic1); w.Write(Magic2); w.Write(Version); w.Write((byte)kind);
                body?.Invoke(w);
                w.Flush();
                var bytes = ms.ToArray();
                if (bytes.Length > 500 * 1024) { LastError = $"message {kind} too big ({bytes.Length} bytes)"; return false; }
                bool ok = crew.SendBytes(to, new Il2CppStructArray<byte>(bytes), bytes.Length, Channel, reliable);
                if (ok) Sent++; else LastError = $"send {kind} to {to} failed";
                return ok;
            }
            catch (Exception e) { LastError = (e.InnerException ?? e).Message; Log.Once("net-send", "Mars net send failed: " + LastError); return false; }
        }

        public static int Broadcast(Kind kind, Action<BinaryWriter> body, bool reliable = true)
        {
            int n = 0;
            foreach (var m in Lobby.OtherMembers()) if (Send(m, kind, body, reliable)) n++;
            return n;
        }

        public static void SendToHost(Kind kind, Action<BinaryWriter> body)
        {
            ulong host = Lobby.Owner();
            if (host != 0 && host != Lobby.Me()) Send(host, kind, body);
        }

        public static void PingAll()
        {
            float now = Time.realtimeSinceStartup;
            foreach (var m in Lobby.OtherMembers()) { pingSent[m] = now; Send(m, Kind.Ping, w => w.Write(now)); }
        }

        // ------------------------------------------------------------------ receiving (every frame)

        public static void Poll()
        {
            if (brokenReceive) return;
            try
            {
                if (!MoonSteamLobby.InLobby) return;
                for (int round = 0; round < 8; round++)
                {
                    int n = SteamNetworkingMessages.ReceiveMessagesOnChannel(Channel, inbox, inbox.Length);
                    if (n <= 0) break;
                    for (int i = 0; i < n; i++)
                    {
                        IntPtr msg = inbox[i];
                        try { Handle(msg); }
                        finally { try { SteamNetworkingMessage_t.Release(msg); } catch { } }
                    }
                    if (n < inbox.Length) break;
                }
            }
            catch (Exception e)
            {
                brokenReceive = true;
                LastError = (e.InnerException ?? e).Message;
                Plugin.Log.LogError("Mars net receive failed, co-op sync is off for this session: " + LastError);
            }
        }

        // Native SteamNetworkingMessage_t (64-bit): m_pData @0, m_cbSize @8, m_identityPeer @16 (steam id @ +8).
        static void Handle(IntPtr msg)
        {
            IntPtr data = Marshal.ReadIntPtr(msg, 0);
            int size = Marshal.ReadInt32(msg, 8);
            ulong from = (ulong)Marshal.ReadInt64(msg, 16 + 8);
            if (size < 4 || data == IntPtr.Zero) return;
            var bytes = new byte[size];
            Marshal.Copy(data, bytes, 0, size);
            if (bytes[0] != Magic1 || bytes[1] != Magic2) return;
            if (bytes[2] != Version) { Log.Once("net-ver" + bytes[2], $"Mars net: a crew member runs a different Mars mod version (protocol {bytes[2]}, ours {Version})"); return; }
            var kind = (Kind)bytes[3];
            Received++;
            if (!handlers.TryGetValue(kind, out var h)) return;
            using (var r = new BinaryReader(new MemoryStream(bytes, 4, size - 4)))
            {
                try { h(from, r); }
                catch (Exception e) { Plugin.Log.LogWarning($"Mars net: handling {kind} from {from} failed: {e.Message}"); }
            }
        }

        public static IEnumerable<string> Status()
        {
            yield return $"role {(IsHost ? "HOST" : "crew")}, channel {Channel}, sent {Sent}, received {Received}{(brokenReceive ? " (receive OFF)" : "")}";
            foreach (var m in Lobby.OtherMembers())
            {
                string name; try { name = MoonSteamLobby.PlayerName(m); } catch { name = m.ToString(); }
                string rtt = RoundTrip.TryGetValue(m, out float ms) ? $"{ms:0} ms" : "no answer yet";
                yield return $"  {name}: {rtt}{(m == Lobby.Owner() ? " (host)" : "")}";
            }
            if (!string.IsNullOrEmpty(LastError)) yield return "last error: " + LastError;
        }
    }
}
