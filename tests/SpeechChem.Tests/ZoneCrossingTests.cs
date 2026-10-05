using SpeechChem.UI;
using Xunit;

namespace SpeechChem.Tests
{
    public class ZoneCrossingTests
    {
        // A row of 6 cells: beta input 0-3, the chamber 4-5.
        private static string Region(int x) => x < 4 ? "beta input" : "chamber";

        private static string Step(ZoneCrossing z, int x)
        {
            z.Land(x, 0, Region(x));
            return z.Announce(x, 0, Region(x));
        }

        [Fact]
        public void FirstReadoutNamesItsRegion()
        {
            var z = new ZoneCrossing();
            Assert.Equal("beta input", z.Announce(2, 0, Region(2)));
            Assert.Null(Step(z, 3));
        }

        [Fact]
        public void LeavingAZoneNamesTheRegionBetween()
        {
            var z = new ZoneCrossing();
            z.Announce(3, 0, Region(3));
            Assert.Equal("chamber", Step(z, 4));
            Assert.Null(Step(z, 5));
            Assert.Null(z.Announce(5, 0, Region(5))); // a re-read of the same cell after the landing's
            Assert.Null(Step(z, 4));
            Assert.Equal("beta input", Step(z, 3));
        }

        [Fact]
        public void ReReadingTheCrossingCellRepeatsTheRegion()
        {
            var z = new ZoneCrossing();
            z.Announce(3, 0, Region(3));
            Assert.Equal("chamber", Step(z, 4));
            Assert.Equal("chamber", z.Announce(4, 0, Region(4)));
        }

        [Fact]
        public void ForgetNamesTheRegionOfTheNextLanding()
        {
            var z = new ZoneCrossing();
            z.Announce(1, 0, Region(1));
            z.Forget();
            Assert.Equal("beta input", Step(z, 0));
        }

        [Fact]
        public void ResetNamesTheNextReadout()
        {
            var z = new ZoneCrossing();
            z.Announce(4, 0, Region(4));
            Assert.Null(Step(z, 5));
            z.Reset();
            Assert.Equal("chamber", z.Announce(5, 0, Region(5)));
        }
    }
}
