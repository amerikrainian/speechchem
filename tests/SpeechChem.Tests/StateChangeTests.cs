using System;
using System.Collections.Generic;
using System.IO;
using SpeechChem.Localization;
using SpeechChem.Narration;
using Xunit;

namespace SpeechChem.Tests
{
    /// <summary>An enemy state change says only what changed (user rule 2026-10-09).</summary>
    public class StateChangeTests
    {
        public StateChangeTests()
        {
            LocalizationManager.LanguageSource = null;
            LocalizationManager.Initialize(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "locale"));
        }

        private static List<string> L(params string[] parts) => new List<string>(parts);

        [Fact]
        public void OnlyWhatAppearedIsSaid()
        {
            Assert.Equal("shield down", StateChange.Describe(L("badly damaged"), L("badly damaged", "shield down")));
            Assert.Equal("badly damaged", StateChange.Describe(L("damaged", "shield down"), L("badly damaged", "shield down")));
            Assert.Equal("eye open, blue", StateChange.Describe(L("eye open, red"), L("eye open, blue")));
            Assert.Equal("shield down, lightning", StateChange.Describe(null, L("shield down", "lightning")));
        }

        [Fact]
        public void AnEndingIsSaidWhenNothingAppeared()
        {
            Assert.Equal("lightning ended", StateChange.Describe(L("shield down", "lightning"), L("shield down")));
            Assert.Equal("normal", StateChange.Describe(L("shield down"), L()));
            Assert.Null(StateChange.Describe(L("shield down"), L("shield down")));
            Assert.Null(StateChange.Describe(null, null));
        }
    }
}
