using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SpeechChem.Game;

namespace SpeechChem.Audio
{
    /// <summary>
    /// The mod's sounds, played through the game's own SDL2_mixer (deob Class10). Beeps have
    /// <see cref="Voices"/> channels of their own: the pool grows by that many and they are RESERVED,
    /// so the game's "first free channel" plays (Class4) still have their 8 and can never take ours.
    /// Each play sets the channel's pan and volume, so one mono chunk per pitch is enough; chunks are
    /// generated on first use at the device's rate. A beep that finds no free channel waits in
    /// <see cref="VoiceQueue"/>. Chunks are never freed: they are a few KB, a hot reload may leave
    /// one still playing, and the audio device may already be gone at domain teardown.
    /// </summary>
    internal static class SoundEngine
    {
        public const int Voices = 32;
        private const int GameChannels = 8;

        private static readonly Dictionary<double, IntPtr> Chunks = new Dictionary<double, IntPtr>();
        private static VoiceQueue _queue;
        private static int _sampleRate;
        private static bool _startAttempted;
        private static string _lastError;

        private sealed class Mixer : IVoiceOutput
        {
            public int Count => Voices;

            public bool Busy(int voice) => Class10.Mix_Playing(voice) != 0;

            public bool Start(int voice, VoiceRequest request)
            {
                IntPtr chunk = ChunkFor(request.Frequency);
                if (chunk == IntPtr.Zero) return false;
                Class10.Mix_SetPanning(voice, request.Left, request.Right);
                Class10.Mix_Volume(voice, request.Volume);
                if (Class10.Mix_PlayChannelTimed(voice, chunk, 0, -1) == voice) return true;
                Report("play refused on channel " + voice);
                return false;
            }
        }

        private static void Report(string error)
        {
            if (error == _lastError) return;
            _lastError = error;
            Log.Error("[audio] " + error);
        }

        private static bool Started()
        {
            if (_queue != null) return true;
            if (_startAttempted) return false;
            _startAttempted = true;
            if (Class10.Mix_QuerySpec(out int rate, out ushort _, out int _) == 0)
            {
                Report("the game's audio device is not open; mod sounds are off");
                return false;
            }
            _sampleRate = rate;
            if (Class10.Mix_AllocateChannels(-1) < Voices + GameChannels) Class10.Mix_AllocateChannels(Voices + GameChannels);
            int reserved = Class10.Mix_ReserveChannels(Voices);
            _queue = new VoiceQueue(new Mixer());
            Log.Info("[audio] " + reserved + " channels reserved of " + Class10.Mix_AllocateChannels(-1) + ", " + rate + " Hz");
            return true;
        }

        private static IntPtr ChunkFor(double frequency)
        {
            if (Chunks.TryGetValue(frequency, out IntPtr chunk)) return chunk;
            byte[] wav = Beep.Wav(Beep.Render(frequency, _sampleRate), _sampleRate);
            var pin = GCHandle.Alloc(wav, GCHandleType.Pinned);
            try
            {
                // The mixer copies and converts the WAV to the device format; it closes the stream (1).
                chunk = Class10.Mix_LoadWAV_RW(Class169.SDL_RWFromConstMem(pin.AddrOfPinnedObject(), wav.Length), 1);
            }
            finally { pin.Free(); }
            if (chunk == IntPtr.Zero) { Report("could not load a generated beep"); return chunk; }
            Chunks[frequency] = chunk;
            return chunk;
        }

        /// <summary>Per frame: arm the channels, then start whatever is still waiting.</summary>
        public static void Tick()
        {
            if (Started()) _queue.Pump();
        }

        private static void Play(double frequency, double column, int percent)
        {
            try
            {
                if (percent <= 0 || !Started()) return;
                Beep.PanGains(column, Game.DefenseText.MapColumns, out byte left, out byte right);
                _queue.Add(new VoiceRequest { Frequency = frequency, Left = left, Right = right, Volume = Beep.MixerVolume(percent) });
            }
            catch (Exception ex) { Report("play failed: " + ex.Message); }
        }

        /// <summary>An enemy reached a map cell (1-based): the column pans, the row sets the pitch.</summary>
        public static void EnemyMoved(int column, int row) => Play(Beep.FrequencyOfRow(row), column, SoundSettings.Volume(SoundCue.EnemyMove));

        /// <summary>The Settings dialog's preview at a draft volume: centred, at the base pitch.</summary>
        public static void Preview(int percent) => Play(Beep.BaseFrequency, (Game.DefenseText.MapColumns + 1) / 2.0, percent);

        /// <summary>Dev: a line of the engine's state.</summary>
        public static string Status()
        {
            if (!Started()) return "audio not started";
            int busy = 0;
            for (int i = 0; i < Voices; i++) if (Class10.Mix_Playing(i) != 0) busy++;
            return "started " + _queue.Started + ", waiting " + _queue.Waiting + ", channels busy " + busy + " of " + Voices
                + ", rate " + _sampleRate + ", pitches cached " + Chunks.Count + ", last error " + (_lastError ?? "none");
        }
    }
}
