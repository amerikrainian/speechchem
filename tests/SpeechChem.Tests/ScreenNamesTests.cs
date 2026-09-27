using System;
using System.IO;
using SpeechChem.Localization;
using SpeechChem.UI;
using Xunit;

namespace SpeechChem.Tests
{
    /// <summary>Runs against the REAL locale asset (module/assets/locale, copied to the test output),
    /// so a curated screen label can't drift out of ui.json without a test noticing.</summary>
    public class ScreenNamesTests
    {
        public ScreenNamesTests()
        {
            LocalizationManager.LanguageSource = null;
            LocalizationManager.Initialize(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "locale"));
        }

        [Theory]
        [InlineData("MainMenuEditor", "Main menu")]
        [InlineData("LevelSelectEditor", "Level select")]
        [InlineData("ResearchNetJournalEditor", "ResearchNet journal")]
        public void CuratedScreensReadFromTheLocaleTable(string typeName, string expected)
        {
            Assert.Equal(expected, ScreenNames.Friendly(typeName));
        }

        [Fact]
        public void UnmappedScreensDropSuffixAndDeCamel()
        {
            Assert.Equal("Highscore Browser", ScreenNames.Friendly("HighscoreBrowserScreen"));
        }

        [Fact]
        public void FallbackLabelWorksWithoutAnyLocaleData()
        {
            Assert.Equal("Pause Menu", ScreenNames.FallbackLabel("PauseMenu"));
            Assert.Equal("Highscore Browser", ScreenNames.FallbackLabel("HighscoreBrowserScreen"));
            // SpaceChem's screens are "editors"; the suffix drops the same way.
            Assert.Equal("Level Select", ScreenNames.FallbackLabel("LevelSelectEditor"));
            // A name that IS the suffix stays readable instead of vanishing.
            Assert.Equal("Editor", ScreenNames.FallbackLabel("Editor"));
        }

        [Theory]
        [InlineData("#=qzDwg_bRKgBybN2rYiyLHng==")]
        [InlineData("Has Space")]
        [InlineData("")]
        [InlineData(null)]
        public void ObfuscatedOrInvalidNamesAreDetected(string name)
        {
            Assert.True(ScreenNames.IsObfuscated(name));
        }

        [Theory]
        [InlineData("DesktopScreen")]
        [InlineData("Class5_0")]
        public void CleanIdentifiersAreNotObfuscated(string name)
        {
            Assert.False(ScreenNames.IsObfuscated(name));
        }

        [Fact]
        public void DeCamelSplitsOnlyLowerToUpperBoundaries()
        {
            Assert.Equal("Custom Puzzle", ScreenNames.DeCamel("CustomPuzzle"));
            // An acronym run does NOT get split after itself ("GIFRecorder" stays glued) — which is
            // precisely why oddly-cased screens (ResearchNet*) carry curated locale entries.
            Assert.Equal("GIFRecorder", ScreenNames.DeCamel("GIFRecorder"));
        }
    }
}
