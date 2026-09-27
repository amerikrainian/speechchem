using System.Linq;
using SpeechChem.Game;
using Xunit;

namespace SpeechChem.Tests
{
    public class HistogramTests
    {
        // The shape of bonding-7's CycleCounts: 200-cycle buckets from 0.
        private const string Data = "0 5000 200 0 7898 1316.33 0 0 1900 2577 7898 3511";

        [Fact]
        public void ParsesHeaderAndCounts()
        {
            var h = Histogram.Parse(Data);
            Assert.NotNull(h);
            Assert.Equal(0, h.XMin);
            Assert.Equal(200, h.Width);
            Assert.Equal(new long[] { 0, 0, 1900, 2577, 7898, 3511 }, h.Counts);
        }

        [Fact]
        public void EmptyBinsAreSkippedAndBarsAreSharesOfAllEngineers()
        {
            var h = Histogram.Parse(Data);
            Assert.True(h.Shares);
            var bins = h.Bins(null, null);
            Assert.Equal(new long[] { 400, 600, 800, 1000 }, bins.Select(b => b.Low));
            Assert.Equal(599, bins[0].High);
            const double total = 1900 + 2577 + 7898 + 3511;
            Assert.Equal((int)System.Math.Round(100 * 7898 / total), bins[2].Percent);
            Assert.Equal((int)System.Math.Round(100 * 1900 / total), bins[0].Percent);
        }

        [Fact]
        public void NonZeroBaselineFallsBackToTheTallestBar()
        {
            // yMin 1000: drawn heights are count − 1000, no longer proportional to counts.
            var h = Histogram.Parse("0 5000 200 1000 7898 1316.33 0 0 1900 2577 7898 3511");
            Assert.False(h.Shares);
            var bins = h.Bins(null, null);
            Assert.Equal(100, bins[2].Percent);
            Assert.Equal((int)System.Math.Round(100.0 * 900 / 6898), bins[0].Percent);
        }

        [Fact]
        public void MarkersTagTheirBucketEvenWhenEmpty()
        {
            var bins = Histogram.Parse(Data).Bins(150, 850);
            var first = bins.First();
            Assert.Equal(0, first.Low);
            Assert.True(first.This);
            Assert.Equal(0, first.Percent);
            Assert.True(bins.Single(b => b.Low == 800).Best);
        }

        [Fact]
        public void MarkersClampToTheDrawnBars()
        {
            var h = Histogram.Parse(Data);
            Assert.Equal(0, h.MarkerBucket(-5));
            Assert.Equal(5, h.MarkerBucket(99999));
            Assert.Equal(1, h.MarkerBucket(399));
        }

        [Fact]
        public void UnitWidthBinsHoldOneValue()
        {
            var bins = Histogram.Parse("1 10 1 0 5 1 4 2 0 1").Bins(1, null);
            Assert.Equal(1, bins[0].Low);
            Assert.Equal(1, bins[0].High);
            Assert.True(bins[0].This);
            Assert.Equal(new long[] { 1, 2, 4 }, bins.Select(b => b.Low));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("1 2 3")]
        [InlineData("a b c d e f 1")]
        public void BadDataIsNull(string data)
        {
            Assert.Null(Histogram.Parse(data));
        }

        [Fact]
        public void ZeroWidthReadsAsOne()
        {
            var h = Histogram.Parse("0 10 0 0 5 1 1 2 3");
            Assert.Equal(1, h.Width);
            Assert.Equal(2, h.MarkerBucket(2));
        }
    }
}
