using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SpeechChem.Audio;
using SpeechChem.Narration;
using Xunit;

namespace SpeechChem.Tests
{
    /// <summary>The generated beep, its stereo and pitch mapping, the no-drop queue, the volumes.</summary>
    [Collection("narration")]
    public class SoundTests : IDisposable
    {
        private readonly string _file;

        public SoundTests()
        {
            _file = Path.Combine(Path.GetTempPath(), "speechchem-sound-" + Guid.NewGuid().ToString("N") + ".json");
            NarrationStore.FilePath = _file;
            NarrationStore.ResetForTests();
        }

        public void Dispose()
        {
            NarrationStore.ResetForTests();
            try { File.Delete(_file); } catch { }
        }

        [Fact]
        public void Render_matches_the_reference_beep()
        {
            var samples = Beep.Render(660.0, 44100);
            Assert.Equal(3528, samples.Length);
            Assert.Equal(0, samples[0]);
            double peak = samples.Max(s => Math.Abs((int)s)) / 32767.0;
            Assert.InRange(peak, Beep.RenderPeak * 0.97, Beep.RenderPeak * 1.03);
        }

        [Fact]
        public void Render_keeps_every_harmonic_below_nyquist()
        {
            // The top row at the game's rate: a naive triangle would fold its 5th harmonic back.
            double frequency = Beep.FrequencyOfRow(22);
            const int rate = 22050;
            var samples = Beep.Render(frequency, rate);
            double re = 0, im = 0;
            double folded = rate - 5 * frequency;
            for (int i = 0; i < samples.Length; i++)
            {
                re += samples[i] * Math.Cos(2 * Math.PI * folded * i / rate);
                im += samples[i] * Math.Sin(2 * Math.PI * folded * i / rate);
            }
            double foldedAmplitude = Math.Sqrt(re * re + im * im) * 2 / samples.Length / 32767.0;
            Assert.True(foldedAmplitude < 0.005, "folded energy " + foldedAmplitude);
        }

        [Fact]
        public void Rows_rise_a_semitone_from_the_base_pitch()
        {
            Assert.Equal(660.0, Beep.FrequencyOfRow(1), 6);
            Assert.Equal(660.0 * Math.Pow(2, 1 / 12.0), Beep.FrequencyOfRow(2), 6);
            Assert.Equal(1320.0, Beep.FrequencyOfRow(13), 6);
        }

        [Theory]
        [InlineData(1.0, 255, 0)]
        [InlineData(32.0, 0, 255)]
        public void Columns_pan_hard_at_the_edges(double column, int left, int right)
        {
            Beep.PanGains(column, 32, out byte l, out byte r);
            Assert.Equal(left, l);
            Assert.Equal(right, r);
        }

        [Fact]
        public void The_middle_column_is_constant_power()
        {
            Beep.PanGains(16.5, 32, out byte l, out byte r);
            Assert.Equal(l, r);
            Assert.InRange(l * l + r * r, 255 * 255 * 0.99, 255 * 255 * 1.01);
        }

        [Fact]
        public void The_wav_header_describes_the_samples()
        {
            var wav = Beep.Wav(new short[] { 1, -1 }, 22050);
            Assert.Equal(48, wav.Length);
            Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
            Assert.Equal(22050, BitConverter.ToInt32(wav, 24));
            Assert.Equal(4, BitConverter.ToInt32(wav, 40));
        }

        private sealed class FakeVoices : IVoiceOutput
        {
            public bool[] BusyFlags;
            public bool Refuse;
            public readonly List<double> Played = new List<double>();
            public FakeVoices(int count) { BusyFlags = new bool[count]; }
            public int Count => BusyFlags.Length;
            public bool Busy(int voice) => BusyFlags[voice];
            public bool Start(int voice, VoiceRequest request)
            {
                if (Refuse) return false;
                BusyFlags[voice] = true;
                Played.Add(request.Frequency);
                return true;
            }
        }

        private static VoiceRequest At(double frequency) => new VoiceRequest { Frequency = frequency };

        [Fact]
        public void A_beep_waits_while_every_voice_is_busy_and_keeps_its_order()
        {
            var voices = new FakeVoices(2);
            var queue = new VoiceQueue(voices);
            foreach (double f in new[] { 1.0, 2.0, 3.0, 4.0 }) queue.Add(At(f));
            Assert.Equal(new[] { 1.0, 2.0 }, voices.Played);
            Assert.Equal(2, queue.Waiting);

            voices.BusyFlags[0] = false;
            queue.Pump();
            Assert.Equal(new[] { 1.0, 2.0, 3.0 }, voices.Played);

            voices.BusyFlags[0] = voices.BusyFlags[1] = false;
            queue.Pump();
            Assert.Equal(new[] { 1.0, 2.0, 3.0, 4.0 }, voices.Played);
            Assert.Equal(0, queue.Waiting);
            Assert.Equal(4, queue.Started);
        }

        [Fact]
        public void A_refused_start_stays_at_the_head_of_the_line()
        {
            var voices = new FakeVoices(1) { Refuse = true };
            var queue = new VoiceQueue(voices);
            queue.Add(At(1.0));
            queue.Add(At(2.0));
            Assert.Equal(2, queue.Waiting);

            voices.Refuse = false;
            queue.Pump();
            Assert.Equal(new[] { 1.0 }, voices.Played);
            Assert.Equal(1, queue.Waiting);
        }

        [Fact]
        public void Volumes_default_to_the_reference_level()
        {
            Assert.Equal(100, SoundSettings.Master());
            Assert.Equal(100, SoundSettings.Volume(SoundCue.EnemyMove));
            Assert.Equal(64, Beep.MixerVolume(100));
            Assert.Equal(128, Beep.MixerVolume(200));
            Assert.Equal(0, Beep.MixerVolume(0));
        }

        [Fact]
        public void A_cue_keeps_its_offset_from_the_master()
        {
            NarrationStore.BeginEdit();
            SoundSettings.SetVolume(SoundCue.EnemyMove, 60);
            Assert.Equal(60, SoundSettings.Volume(SoundCue.EnemyMove, draft: true));
            Assert.Equal(100, SoundSettings.Volume(SoundCue.EnemyMove)); // not committed yet

            SoundSettings.SetMaster(150);
            Assert.Equal(110, SoundSettings.Volume(SoundCue.EnemyMove, draft: true));
            NarrationStore.Commit();
            Assert.Equal(150, SoundSettings.Master());
            Assert.Equal(110, SoundSettings.Volume(SoundCue.EnemyMove));
        }

        [Fact]
        public void A_cancelled_edit_leaves_the_volumes_alone()
        {
            NarrationStore.BeginEdit();
            SoundSettings.SetMaster(20);
            NarrationStore.Discard();
            Assert.Equal(100, SoundSettings.Master());
        }

        [Fact]
        public void Default_values_are_not_stored()
        {
            NarrationStore.BeginEdit();
            SoundSettings.SetMaster(50);
            SoundSettings.SetMaster(100);
            SoundSettings.SetVolume(SoundCue.EnemyMove, 100);
            Assert.Null(NarrationStore.GetDraft("sound.master"));
            Assert.Null(NarrationStore.GetDraft("sound.offset.EnemyMove"));
            NarrationStore.Discard();
        }

        [Fact]
        public void An_enemy_move_is_logged_and_beeped_not_spoken_by_default()
        {
            var move = EventKinds.Get("defense.move");
            Assert.Equal(SpeakLevel.Off, move.SpeakDefault);
            Assert.False(move.StepSpeaksDefault);
            Assert.True(move.StepStopsDefault);
            Assert.True(move.LogDefault);
            Assert.Equal(SpeakLevel.Speed3, EventKinds.Get("defense.sight").SpeakDefault);
        }
    }
}
