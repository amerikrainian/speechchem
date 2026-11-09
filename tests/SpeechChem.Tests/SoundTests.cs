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
                Played.Add(request.Tone.Frequency);
                return true;
            }
        }

        private static VoiceRequest At(double frequency) => new VoiceRequest { Tone = Tone.EnemyMove(frequency) };

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

        private static double Semitones(double frequency) => 12.0 * Math.Log(frequency / Beep.BaseFrequency, 2.0);

        [Theory]
        [InlineData(0, -8.0)]
        [InlineData(1, -4.0)]
        [InlineData(2, 0.0)]
        [InlineData(3, 4.0)]
        [InlineData(4, 8.0)]
        public void Each_laser_aim_falls_on_its_own_whole_semitone(int position, double semitones)
            => Assert.Equal(semitones, Semitones(Beep.FrequencyOfBearing(Beep.AimDegrees(position))), 6);

        [Fact]
        public void A_bearing_equal_to_the_aim_has_the_same_pitch()
            => Assert.Equal(Beep.FrequencyOfBearing(Beep.AimDegrees(4)), Beep.FrequencyOfBearing(45.0), 6);

        [Fact]
        public void Bearings_round_to_quarter_semitones_and_down_is_higher()
        {
            Assert.Equal(7.75, Semitones(Beep.FrequencyOfBearing(43.0)), 6); // 43 / 5.625 = 7.64
            Assert.True(Beep.FrequencyOfBearing(10.0) > Beep.FrequencyOfBearing(-10.0));
        }

        private static double Rms(short[] samples) => Math.Sqrt(samples.Average(s => s * (double)s)) / 32767.0;

        [Theory]
        [InlineData((int)Waveform.Square)]
        [InlineData((int)Waveform.Sawtooth)]
        [InlineData((int)Waveform.Sine)]
        public void Every_shape_is_about_as_loud_as_the_triangle(int shape)
        {
            var tone = new Tone { Shape = (Waveform)shape, Frequency = 660.0, Pulses = 1, PulseSeconds = Beep.LengthSeconds };
            double reference = Rms(Beep.Render(660.0, 22050));
            Assert.InRange(Rms(Beep.Render(tone, 22050)), reference * 0.9, reference * 1.1);
        }

        [Fact]
        public void An_asteroid_tick_waits_for_its_aim_note_then_pulses()
        {
            const int rate = 22050;
            var samples = Beep.Render(Tone.Asteroid(660.0, 3), rate);
            int lead = (int)Math.Round(Tone.AsteroidLeadSeconds * rate);
            int pulse = (int)Math.Round(Tone.AsteroidPulseSeconds * rate);
            int gap = (int)Math.Round(Tone.PulseGapSeconds * rate);
            Assert.Equal(lead + 3 * pulse + 2 * gap, samples.Length);
            Assert.All(samples.Take(lead), s => Assert.Equal(0, s));
            Assert.All(samples.Skip(lead + pulse).Take(gap), s => Assert.Equal(0, s));
            Assert.True(Rms(samples.Skip(lead).Take(pulse).ToArray()) > 0.1);
            Assert.True(Rms(samples.Skip(lead + 2 * (pulse + gap)).Take(pulse).ToArray()) > 0.1);
        }

        [Fact]
        public void Tones_that_differ_are_cached_apart()
        {
            Assert.Equal(Tone.Asteroid(660.0, 2), Tone.Asteroid(660.0, 2));
            Assert.NotEqual(Tone.Asteroid(660.0, 2), Tone.Asteroid(660.0, 3));
            Assert.NotEqual(Tone.Aim(660.0), Tone.EnemyMove(660.0));
        }

        [Fact]
        public void A_new_asteroid_takes_the_lowest_free_pulse_count()
        {
            Assert.Equal(1, Beep.LowestFreePulses(new int[0]));
            Assert.Equal(2, Beep.LowestFreePulses(new[] { 1, 3 }));
            Assert.Equal(4, Beep.LowestFreePulses(new[] { 1, 2, 3 }));
        }

        [Theory]
        [InlineData((int)SoundCue.Asteroid)]
        [InlineData((int)SoundCue.LaserAim)]
        public void Each_cue_has_its_own_volume_under_the_master(int which)
        {
            NarrationStore.BeginEdit();
            var cue = (SoundCue)which;
            SoundSettings.SetVolume(cue, 30);
            NarrationStore.Commit();
            Assert.Equal(30, SoundSettings.Volume(cue));
            Assert.Equal(100, SoundSettings.Volume(SoundCue.EnemyMove));
        }
    }
}
