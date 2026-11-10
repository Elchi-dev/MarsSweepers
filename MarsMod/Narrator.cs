using System;
using System.Collections.Generic;
using System.IO;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace MarsMod
{
    /// <summary>
    /// Optional narrator. Put WAV files into BepInEx/MarsMod/voice/:
    ///   intro_01.wav … intro_06.wav   one per Mars intro slide, played when that slide appears
    ///   welcome.wav                   played on the first visit to a Mars world
    /// 16-bit or 32-bit float PCM, mono or stereo, any sample rate. Missing files are simply skipped.
    /// </summary>
    internal static class Narrator
    {
        static string Dir => Path.Combine(BepInEx.Paths.BepInExRootPath, "MarsMod", "voice");
        static GameObject host;
        static object source;
        static readonly Dictionary<string, object> clips = new Dictionary<string, object>();
        static int lastSlide = -1;

        public static void ResetIntro() => lastSlide = -1;

        /// <summary>Called while a Mars intro runs, with the index of the slide on screen (or -1).</summary>
        public static void Slide(int index)
        {
            if (index < 0 || index == lastSlide) return;
            lastSlide = index;
            Play($"intro_{index + 1:00}.wav");
        }

        public static bool Play(string file)
        {
            try
            {
                string path = Path.Combine(Dir, file);
                if (!File.Exists(path)) return false;
                if (!clips.TryGetValue(file, out var clip)) clips[file] = clip = Load(path);
                if (clip == null) return false;
                if (!EnsureSource()) return false;
                R.Call(source, "Stop");
                R.Set(source, "clip", clip);
                R.Call(source, "Play");
                Plugin.Log.LogInfo("Narrator: " + file);
                return true;
            }
            catch (Exception e) { Log.Once("voice-" + file, $"Narrator could not play {file}: {(e.InnerException ?? e).Message}"); return false; }
        }

        static bool EnsureSource()
        {
            if (source != null) return true;
            var srcT = R.T("UnityEngine.AudioSource", "UnityEngine.AudioModule");
            if (srcT == null) return false;
            host = new GameObject();
            host.name = "Mars narrator";
            UnityEngine.Object.DontDestroyOnLoad(host);
            source = R.Add(host, srcT);
            R.TrySet(source, "spatialBlend", 0f);
            R.TrySet(source, "volume", MarsProfile.VoiceVolume.Value);
            R.TrySet(source, "playOnAwake", false);
            return source != null;
        }

        /// <summary>Read a PCM WAV file into a Unity AudioClip.</summary>
        static object Load(string path)
        {
            byte[] b = File.ReadAllBytes(path);
            if (b.Length < 44 || b[0] != 'R' || b[1] != 'I' || b[2] != 'F' || b[3] != 'F') throw new InvalidDataException("not a WAV file");
            int pos = 12, channels = 1, rate = 44100, bits = 16, format = 1, dataAt = -1, dataLen = 0;
            while (pos + 8 <= b.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(b, pos, 4);
                int len = BitConverter.ToInt32(b, pos + 4);
                if (id == "fmt ")
                {
                    format = BitConverter.ToInt16(b, pos + 8);
                    channels = BitConverter.ToInt16(b, pos + 10);
                    rate = BitConverter.ToInt32(b, pos + 12);
                    bits = BitConverter.ToInt16(b, pos + 22);
                    if (format == -2 && len >= 26) format = BitConverter.ToInt16(b, pos + 32); // WAVE_FORMAT_EXTENSIBLE
                }
                else if (id == "data") { dataAt = pos + 8; dataLen = Math.Min(len, b.Length - dataAt); break; }
                pos += 8 + len + (len & 1);
            }
            if (dataAt < 0) throw new InvalidDataException("no audio data");
            float[] samples;
            if (format == 1 && bits == 16)
            {
                samples = new float[dataLen / 2];
                for (int i = 0; i < samples.Length; i++) samples[i] = BitConverter.ToInt16(b, dataAt + i * 2) / 32768f;
            }
            else if (format == 3 && bits == 32)
            {
                samples = new float[dataLen / 4];
                for (int i = 0; i < samples.Length; i++) samples[i] = BitConverter.ToSingle(b, dataAt + i * 4);
            }
            else if (format == 1 && bits == 24)
            {
                samples = new float[dataLen / 3];
                for (int i = 0; i < samples.Length; i++)
                {
                    int o = dataAt + i * 3;
                    int v = (b[o] | (b[o + 1] << 8) | (b[o + 2] << 16)) << 8 >> 8;
                    samples[i] = v / 8388608f;
                }
            }
            else throw new InvalidDataException($"unsupported WAV ({bits}-bit, format {format}); export 16-bit PCM");

            return MakeClip(Path.GetFileNameWithoutExtension(path), samples, rate, channels);
        }

        /// <summary>Build a Unity AudioClip from samples (-1..1).</summary>
        public static object MakeClip(string name, float[] samples, int rate, int channels = 1)
        {
            var clipT = R.T("UnityEngine.AudioClip", "UnityEngine.AudioModule");
            var create = Array.Find(clipT.GetMethods(), m => m.Name == "Create" && m.GetParameters().Length == 5);
            object clip = create.Invoke(null, new object[] { name, samples.Length / channels, channels, rate, false });
            var data = new Il2CppStructArray<float>(samples);
            R.Call(clip, "SetData", data, 0);
            R.TrySet(clip, "hideFlags", 32); // DontUnloadUnusedAsset: keep it loaded between scenes
            return clip;
        }

        /// <summary>A 2D AudioSource on its own child object.</summary>
        public static object MakeSource(GameObject parent, string name, object clip, bool loop)
        {
            var srcT = R.T("UnityEngine.AudioSource", "UnityEngine.AudioModule");
            var go = new GameObject();
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            var src = R.Add(go, srcT);
            R.TrySet(src, "spatialBlend", 0f);
            R.TrySet(src, "playOnAwake", false);
            R.TrySet(src, "loop", loop);
            R.Set(src, "clip", clip);
            return src;
        }
    }
}
