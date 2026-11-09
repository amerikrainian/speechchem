using System;
using System.IO;
using System.Text;

namespace SpeechChem.Audio
{
    /// <summary>
    /// The generated beep and where it sits in the stereo and pitch fields (pure maths, unit-tested).
    /// The reference is the user's beep.wav, fitted to a residual of 0.0001: a 660 Hz triangle wave
    /// in cosine phase, peak 0.22, 80 ms, 6 ms linear fades. It is built from the triangle's odd
    /// harmonics up to Nyquist so the high rows don't alias at the game's 22050 Hz output.
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

        /// <summary>A semitone per row: row 1 is the base pitch, higher row numbers go up.</summary>
        public static double FrequencyOfRow(int row) => BaseFrequency * Math.Pow(2.0, (row - 1) / 12.0);

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

        /// <summary>The beep at a frequency: mono signed 16-bit samples at
        /// <paramref name="sampleRate"/>.</summary>
        public static short[] Render(double frequency, int sampleRate)
        {
            int count = (int)Math.Round(LengthSeconds * sampleRate);
            int fade = Math.Max(1, (int)Math.Round(FadeSeconds * sampleRate));
            var samples = new short[count];
            double step = 2.0 * Math.PI * frequency / sampleRate;
            int top = (int)Math.Floor(sampleRate / 2.0 / frequency);
            for (int i = 0; i < count; i++)
            {
                double value = 0.0;
                for (int k = 1; k <= top; k += 2) value += Math.Cos(step * k * i) / (k * (double)k);
                double envelope = Math.Min(1.0, Math.Min(i / (double)fade, (count - i) / (double)fade));
                samples[i] = (short)Math.Round(32767.0 * RenderPeak * envelope * value * 8.0 / (Math.PI * Math.PI));
            }
            return samples;
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
    }
}
