using System;
using System.Collections.Generic;
using System.IO;
using SpeechChem.Narration;
using Xunit;

namespace SpeechChem.Tests
{
    /// <summary>The narration core: default formats reproduce the original wording; parts, order,
    /// variants and layer overrides; the settings file.</summary>
    [Collection("narration")]
    public class NarrationTests : IDisposable
    {
        private readonly string _file;

        public NarrationTests()
        {
            _file = Path.Combine(Path.GetTempPath(), "speechchem-narration-" + Guid.NewGuid().ToString("N") + ".json");
            NarrationStore.FilePath = _file;
            NarrationStore.ResetForTests();
        }

        public void Dispose()
        {
            NarrationStore.ResetForTests();
            try { File.Delete(_file); } catch { }
        }

        private static readonly object Reactor2 = new object();

        private static NarrationEvent Bond()
        {
            var atoms1 = new Dictionary<string, string> { { "cells", "A at 1, 2 and B at 2, 2" }, { "names", "A and B" } };
            var atoms2 = new Dictionary<string, string> { { "cells", "C at 3, 3 and D at 4, 3" }, { "names", "C and D" } };
            var e = new NarrationEvent("waldo.bond") { Reactor = Reactor2, Colour = 0 };
            e.CommonPart("reactor", "reactor 2", ",").CommonPart("waldo", "red", ":");
            e.NextItem().Part("action", "bonded").Part("atoms", atoms1, atoms1["cells"], ",").Part("result", "single bond");
            e.NextItem().Part("action", "unbonded").Part("atoms", atoms2, atoms2["cells"]);
            return e;
        }

        [Fact]
        public void DefaultFormatIsTheOriginalWording()
        {
            Assert.Equal("reactor 2, red: bonded A at 1, 2 and B at 2, 2, single bond; unbonded C at 3, 3 and D at 4, 3",
                Formatter.Format(Bond(), FormatLayer.Log, null));
            var input = new NarrationEvent("waldo.input");
            input.CommonPart("waldo", "blue", ":").Part("instruction", "in alpha", ",").Part("molecule", "Oxygen, O2").Part("place", "at 2, 1");
            Assert.Equal("blue: in alpha, Oxygen, O2 at 2, 1", Formatter.Format(input, FormatLayer.Speech, null));
            var move = new NarrationEvent("defense.move").Part("enemy", "Isambard MMD").Part("column", "21", ",").Part("row", "5");
            Assert.Equal("Isambard MMD 21, 5", Formatter.Format(move, FormatLayer.Speech, null));
        }

        [Fact]
        public void TheOpenReactorsOwnEventsDropItsNumber()
        {
            Assert.StartsWith("red: bonded", Formatter.Format(Bond(), FormatLayer.Speech, Reactor2));
            Assert.StartsWith("reactor 2, red:", Formatter.Format(Bond(), FormatLayer.Speech, new object()));
        }

        [Fact]
        public void PartsTurnOffReorderAndChangeDetail()
        {
            var kind = EventKinds.Get("waldo.bond");
            NarrationStore.BeginEdit();
            EventSettings.SetPartOn(kind, FormatLayer.Default, "reactor", false);
            EventSettings.SetVariant(kind, FormatLayer.Default, Array.Find(kind.Parts, p => p.Key == "atoms"), "names");
            EventSettings.SetOrder(kind, FormatLayer.Default, new List<string> { "waldo", "result", "action", "atoms", "reactor" });
            NarrationStore.Commit();
            Assert.Equal("red: single bond, bonded A and B; unbonded C and D", Formatter.Format(Bond(), FormatLayer.Log, null));
        }

        [Fact]
        public void LayersInheritTheDefaultUntilOverridden()
        {
            var kind = EventKinds.Get("waldo.bond");
            NarrationStore.BeginEdit();
            EventSettings.SetPartOn(kind, FormatLayer.Default, "waldo", false);
            EventSettings.SetPartOn(kind, FormatLayer.Speech, "reactor", false);
            NarrationStore.Commit();
            Assert.StartsWith("reactor 2, bonded", Formatter.Format(Bond(), FormatLayer.Log, null));   // inherits the default
            Assert.StartsWith("bonded", Formatter.Format(Bond(), FormatLayer.Speech, null));          // its own override too
            Assert.True(EventSettings.Overridden(kind, FormatLayer.Speech, draft: false));
            Assert.False(EventSettings.Overridden(kind, FormatLayer.Log, draft: false));
        }

        [Fact]
        public void AStoredOrderMissingANewPartKeepsItInPlace()
        {
            var kind = EventKinds.Get("waldo.input"); // reactor, waldo, instruction, molecule, place
            var order = EventSettings.MergeOrder("place,reactor,waldo,instruction", kind);
            Assert.Equal(new[] { "place", "reactor", "waldo", "instruction", "molecule" }, order.ToArray());
            order = EventSettings.MergeOrder("instruction,gone,waldo", kind);
            // Missing parts go right after their nearest registry predecessor that is present.
            Assert.Equal(new[] { "reactor", "instruction", "molecule", "place", "waldo" }, order.ToArray());
        }

        [Fact]
        public void CancelDiscardsSaveWritesOnlyChanges()
        {
            var kind = EventKinds.Get("waldo.grab");
            NarrationStore.BeginEdit();
            EventSettings.Set(EventSettings.EventKey(kind, "log"), "false", "true");
            NarrationStore.Discard();
            Assert.True(EventSettings.Log(kind));
            Assert.False(File.Exists(_file));

            NarrationStore.BeginEdit();
            EventSettings.Set(EventSettings.EventKey(kind, "log"), "false", "true");
            EventSettings.SetSpeaksAt(kind, "1", true); // the default: not stored
            NarrationStore.Commit();
            Assert.False(EventSettings.Log(kind));
            string text = File.ReadAllText(_file);
            Assert.Contains("\"event.waldo.grab.log\": \"false\"", text);
            Assert.DoesNotContain("speak", text);
            Assert.Contains("\"version\": \"1\"", text);

            NarrationStore.ResetForTests(); // a restart: read back from the file
            Assert.False(EventSettings.Log(kind));
        }

        [Fact]
        public void SpeakCheckboxesDefaultToTheOriginalLevels()
        {
            var grab = EventKinds.Get("waldo.grab");     // speed 1
            Assert.True(EventSettings.SpeaksAt(grab, "1"));
            Assert.False(EventSettings.SpeaksAt(grab, "2"));
            Assert.False(EventSettings.SpeaksAt(grab, "idle"));
            var move = EventKinds.Get("defense.move");   // speeds 1 to 3
            Assert.True(EventSettings.SpeaksAt(move, "3"));
            Assert.False(EventSettings.SpeaksAt(move, "4"));
            var state = EventKinds.Get("run.state");     // always
            Assert.True(EventSettings.SpeaksAt(state, "4"));
            Assert.True(EventSettings.SpeaksAt(state, "idle"));
            Assert.False(EventSettings.SpeaksAt(EventKinds.Get("run.error"), "1")); // never

            NarrationStore.BeginEdit();
            EventSettings.SetSpeaksAt(grab, "3", true);
            NarrationStore.Commit();
            Assert.True(EventSettings.SpeaksAt(grab, "3"));
            Assert.False(EventSettings.SpeaksAt(grab, "2")); // independent checkboxes
        }

        [Fact]
        public void TheLogInternsEventsByContent()
        {
            var log = new SpeechChem.UI.GroupedLog<int, NarrationEvent>(1000, EventContentComparer.Instance);
            for (int c = 0; c < 100; c++) { log.Add(c, Bond()); log.Add(c, Bond()); }
            Assert.Equal(200, log.EntryCount);
            Assert.Equal(1, log.DistinctValues);           // a looping program: one record

            var other = Bond();
            other.Reactor = new object();                  // another reactor's: its own record
            log.Add(100, other);
            var crash = Bond();
            crash.Payload = new object();                  // a crash snapshot keeps its entry its own
            log.Add(100, crash);
            var reworded = new NarrationEvent("waldo.bond") { Reactor = Reactor2 };
            reworded.CommonPart("reactor", "reactor 2", ",").CommonPart("waldo", "blue", ":");
            reworded.Part("action", "bonded");
            log.Add(100, reworded);
            Assert.Equal(4, log.DistinctValues);
            Assert.Same(crash, log.Entries(100)[1]);
        }

        [Fact]
        public void ACompiledFormatFollowsACommit()
        {
            Assert.StartsWith("reactor 2, red:", Formatter.Format(Bond(), FormatLayer.Log, null));
            var kind = EventKinds.Get("waldo.bond");
            NarrationStore.BeginEdit();
            EventSettings.SetPartOn(kind, FormatLayer.Default, "reactor", false);
            Assert.StartsWith("reactor 2, red:", Formatter.Format(Bond(), FormatLayer.Log, null)); // drafts don't apply
            NarrationStore.Commit();
            Assert.StartsWith("red:", Formatter.Format(Bond(), FormatLayer.Log, null));
        }

        [Fact]
        public void StepKeysDefaultToTheOriginalTwo()
        {
            Assert.True(StepKeys.Assigned("0"));
            Assert.True(StepKeys.Assigned("c0"));
            Assert.False(StepKeys.Assigned("5"));
            Assert.Equal(EventSettings.ScopeOpen, StepKeys.Scope("0"));
            Assert.Equal(EventSettings.ScopeAll, StepKeys.Scope("c0"));
            Assert.True(StepKeys.Stops("0", EventKinds.Get("waldo.grab")));
            Assert.False(StepKeys.Stops("0", EventKinds.Get("run.error")));
            Assert.False(StepKeys.Speaks("0", EventKinds.Get("output.invalid")));
            Assert.Equal(1000, StepKeys.GiveUp("0"));
        }
    }
}
