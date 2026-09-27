using System;
using System.Collections.Generic;
using System.Globalization;

namespace SpeechChem.Game
{
    /// <summary>
    /// The performance histograms' data (scores.json via Class157 → Class426 CycleCounts /
    /// ReactorCounts / SymbolCounts), parsed like Graph.smethod_3: space-separated invariant
    /// numbers "xMin xMax width yMin yMax yPad count0 count1 …", bucket k covering
    /// [xMin + k·width, xMin + (k+1)·width), width 0 read as 1. The game draws only the bars (no
    /// counts, no y axis) and the THIS / BEST markers at clamp(value + 0.5, xMin, xMax); a marker
    /// names the bucket it stands over.
    ///
    /// Bars read as their SHARE OF ENGINEERS (user decision, 2026-09-27): the chart is a filled
    /// step plot of equal-width buckets whose heights are (count − yMin) over a fixed scale, and
    /// yMin is 0 in every shipped histogram — so a bar's share of the total filled area is its
    /// share of the players, which is what the picture shows. The old "percent of the tallest bar"
    /// read like a share and wasn't one (a chart summed to 193%). With a non-zero yMin the heights
    /// stop being proportional to counts, so the bars fall back to relative height
    /// (<see cref="Shares"/> false). BCL-pure; unit-tested.
    /// </summary>
    internal sealed class Histogram
    {
        public double XMin, XMax, Width, YMin;
        public readonly List<long> Counts = new List<long>();

        public sealed class Bin
        {
            public long Low, High;  // the integer scores the bucket holds, inclusive
            public int Percent;     // share of engineers (Shares), else percent of the tallest bar
            public bool Drawn;      // a bar is drawn (non-zero height; may still round to 0%)
            public bool This, Best; // a marker stands over it
        }

        /// <summary>True when bar heights are proportional to player counts (baseline 0), so
        /// <see cref="Bin.Percent"/> is a share of engineers; false = relative to the tallest bar.</summary>
        public bool Shares => YMin == 0;

        public static Histogram Parse(string data)
        {
            if (string.IsNullOrEmpty(data)) return null;
            var parts = data.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 7) return null;
            var h = new Histogram();
            try
            {
                h.XMin = double.Parse(parts[0], CultureInfo.InvariantCulture);
                h.XMax = double.Parse(parts[1], CultureInfo.InvariantCulture);
                h.Width = double.Parse(parts[2], CultureInfo.InvariantCulture);
                h.YMin = double.Parse(parts[3], CultureInfo.InvariantCulture);
                if (h.Width <= 0) h.Width = 1;
                for (int i = 6; i < parts.Length; i++)
                    h.Counts.Add((long)double.Parse(parts[i], CultureInfo.InvariantCulture));
            }
            catch (FormatException) { return null; }
            catch (OverflowException) { return null; }
            return h.Counts.Count > 0 ? h : null;
        }

        /// <summary>The bucket a marker for this value stands over (the game's x clamp, then the
        /// bucket at that x, clamped to the drawn bars).</summary>
        public int MarkerBucket(double value)
        {
            double x = Math.Max(XMin, Math.Min(XMax, value + 0.5));
            int k = (int)Math.Floor((x - XMin) / Width);
            return Math.Max(0, Math.Min(Counts.Count - 1, k));
        }

        /// <summary>The bars as rows: non-empty buckets, plus any bucket under a marker (a marker
        /// over an empty bar still reads).</summary>
        public List<Bin> Bins(double? thisRun, double? best)
        {
            int mine = thisRun != null ? MarkerBucket(thisRun.Value) : -1;
            int prev = best != null ? MarkerBucket(best.Value) : -1;
            double tallest = 0, total = 0;
            foreach (var c in Counts)
            {
                tallest = Math.Max(tallest, c - YMin);
                total += Math.Max(0, c - YMin);
            }
            double scale = Shares ? total : tallest;
            var bins = new List<Bin>();
            for (int k = 0; k < Counts.Count; k++)
            {
                double height = Math.Max(0, Counts[k] - YMin);
                if (height <= 0 && k != mine && k != prev) continue;
                long low = (long)Math.Ceiling(XMin + k * Width);
                long high = (long)Math.Ceiling(XMin + (k + 1) * Width) - 1;
                bins.Add(new Bin
                {
                    Low = low,
                    High = Math.Max(low, high),
                    Percent = scale > 0 ? (int)Math.Round(100 * height / scale) : 0,
                    Drawn = height > 0,
                    This = k == mine,
                    Best = k == prev,
                });
            }
            return bins;
        }
    }
}
