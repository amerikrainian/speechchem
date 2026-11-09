using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SpeechChem.Audio
{
    internal enum Waveform { Triangle, Square, Sawtooth, Sine }

    /// <summary>One generated sound: a shape at a pitch, as one or more pulses, after an optional
    /// silence (so two sounds started together play one after the other).</summary>
    internal struct Tone : IEquatable<Tone>
    {
        public Waveform Shape;
        public double Frequency;
        public int Pulses;
        public double PulseSeconds;
        public double LeadSeconds;

        public const double AimSeconds = 0.06, AsteroidPulseSeconds = 0.045, PulseGapSeconds = 0.03;

        /// <summary>Starts after the aim note it is paired with, and a short gap.</summary>
        public const double AsteroidLeadSeconds = AimSeconds + PulseGapSeconds;

        public static Tone EnemyMove(double frequency)
            => new Tone { Shape = Waveform.Triangle, Frequency = frequency, Pulses = 1, PulseSeconds = Beep.LengthSeconds };

        public static Tone Aim(double frequency)
            => new Tone { Shape = Waveform.Sawtooth, Frequency = frequency, Pulses = 1, PulseSeconds = AimSeconds };

        public static Tone Asteroid(double frequency, int pulses)
            => new Tone { Shape = Waveform.Square, Frequency = frequency, Pulses = pulses, PulseSeconds = AsteroidPulseSeconds, LeadSeconds = AsteroidLeadSeconds };

        public bool Equals(Tone other)
            => Shape == other.Shape && Frequency == other.Frequency && Pulses == other.Pulses
               && PulseSeconds == other.PulseSeconds && LeadSeconds == other.LeadSeconds;

        public override bool Equals(object obj) => obj is Tone other && Equals(other);

        public override int GetHashCode() => Frequency.GetHashCode() * 31 + (int)Shape * 7 + Pulses;
    }

    /// <summary>
    /// The generated sounds and where they sit in the stereo and pitch fields (pure maths,
    /// unit-tested). The enemy beep's reference is the user's WAV, fitted to a residual of 0.0001: a
    /// 660 Hz triangle wave in cosine phase, peak 0.22, 80 ms, 6 ms linear fades. Every shape is built
    /// from its harmonics up to Nyquist so high pitches don't alias at the game's 22050 Hz output,
    /// and scaled to the triangle's RMS so the shapes are about equally loud.
    /// </summary>
    internal static class Beep
    {
        public const double BaseFrequency = 660.0;
        public const double LengthSeconds = 0.08;
        public const double FadeSeconds = 0.006;

        /// <summary>The peak at a volume of 100 percent (the reference file's).</summary>
        public const double ReferencePeak = 0.22;

        /// <summary>The rendered peak: twice the reference, so the mixer's 0-128 channel volume
        /// (which can only attenuate) covers 0 to <see cref="MaxPercent"/> percent.</summary>
        public const double RenderPeak = ReferencePeak * 2.0;

        public const int MaxPercent = 200;

        /// <summary>The Hydrogen Laser's five aims are this far apart, and four semitones apart in
        /// pitch, so each aim falls on a whole semitone.</summary>
        public const double DegreesPerAim = 22.5;
        private const double DegreesPerSemitone = DegreesPerAim / 4.0;

        /// <summary>A semitone per row: row 1 is the base pitch, higher row numbers go up.</summary>
        public static double FrequencyOfRow(int row) => FrequencyOfSemitones(row - 1);

        public static double FrequencyOfSemitones(double semitones) => BaseFrequency * Math.Pow(2.0, semitones / 12.0);

        /// <summary>An angle from the laser as a pitch: level is the base pitch, down is higher
        /// (like the rows). Rounded to a quarter semitone (1.4 degrees), which is finer than the ear
        /// can follow in a tick this short and keeps the number of generated sounds bounded.</summary>
        public static double FrequencyOfBearing(double degrees)
            => FrequencyOfSemitones(Math.Round(degrees / DegreesPerSemitone * 4.0) / 4.0);

        /// <summary>The angle of a turret position (Class600.int_3, 0-4; 2 is level, higher is down).</summary>
        public static double AimDegrees(int position) => (position - 2) * DegreesPerAim;

        /// <summary>The mixer's channel volume (0-128) for a volume in percent.</summary>
        public static int MixerVolume(int percent)
            => (int)Math.Round(Math.Max(0, Math.Min(MaxPercent, percent)) * 128.0 / MaxPercent);

        /// <summary>Constant-power left and right gains (0-255, the mixer's panning scale) for a
        /// column: 1 is hard left, <paramref name="columns"/> is hard right.</summary>
        public static void PanGains(double column, int columns, out byte left, out byte right)
        {
            double position = columns <= 1 ? 0.5 : Math.Max(0.0, Math.Min(1.0, (column - 1.0) / (columns - 1.0)));
            double angle = position * Math.PI / 2.0;
            left = (byte)Math.Round(255.0 * Math.Cos(angle));
            right = (byte)Math.Round(255.0 * Math.Sin(angle));
        }

        /// <summary>The enemy beep at a frequency.</summary>
        public static short[] Render(double frequency, int sampleRate) => Render(Tone.EnemyMove(frequency), sampleRate);

        /// <summary>A tone as mono signed 16-bit samples at <paramref name="sampleRate"/>.</summary>
        public static short[] Render(Tone tone, int sampleRate)
        {
            int lead = (int)Math.Round(tone.LeadSeconds * sampleRate);
            int pulse = (int)Math.Round(tone.PulseSeconds * sampleRate);
            int gap = (int)Math.Round(Tone.PulseGapSeconds * sampleRate);
            int fade = Math.Max(1, (int)Math.Round(FadeSeconds * sampleRate));
            var samples = new short[lead + tone.Pulses * pulse + (tone.Pulses - 1) * gap];
            double step = 2.0 * Math.PI * tone.Frequency / sampleRate;
            int top = (int)Math.Floor(sampleRate / 2.0 / tone.Frequency);
            for (int p = 0; p < tone.Pulses; p++)
            {
                int start = lead + p * (pulse + gap);
                for (int i = 0; i < pulse; i++)
                {
                    double envelope = Math.Min(1.0, Math.Min(i / (double)fade, (pulse - i) / (double)fade));
                    samples[start + i] = (short)Math.Round(32767.0 * envelope * Wave(tone.Shape, step * i, top));
                }
            }
            return samples;
        }

        private static double Wave(Waveform shape, double phase, int top)
        {
            double value = 0.0;
            switch (shape)
            {
                case Waveform.Sine:
                    return RenderPeak * Math.Sqrt(2.0 / 3.0) * Math.Sin(phase);
                case Waveform.Triangle:
                    for (int k = 1; k <= top; k += 2) value += Math.Cos(k * phase) / (k * (double)k);
                    return RenderPeak * value * 8.0 / (Math.PI * Math.PI);
                case Waveform.Square:
                    for (int k = 1; k <= top; k += 2) value += Math.Sin(k * phase) / k;
                    return RenderPeak / Math.Sqrt(3.0) * value * 4.0 / Math.PI;
                default:
                    for (int k = 1; k <= top; k++) value += Math.Sin(k * phase) / k;
                    return RenderPeak * value * 2.0 / Math.PI;
            }
        }

        /// <summary>A complete in-memory PCM WAV file holding mono samples.</summary>
        public static byte[] Wav(short[] samples, int sampleRate)
        {
            int bytes = samples.Length * 2;
            using (var stream = new MemoryStream(44 + bytes))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + bytes);
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)1);
                writer.Write(sampleRate);
                writer.Write(sampleRate * 2);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(bytes);
                foreach (short sample in samples) writer.Write(sample);
                writer.Flush();
                return stream.ToArray();
            }
        }

        /// <summary>The lowest pulse count (from 1) nobody holds: a new asteroid's.</summary>
        public static int LowestFreePulses(ICollection<int> taken)
        {
            int pulses = 1;
            while (taken.Contains(pulses)) pulses++;
            return pulses;
        }
    }
}
