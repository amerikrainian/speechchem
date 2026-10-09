using System.Collections.Generic;
using Impeller;
using SpaceChem;
using SpaceChem.Reactor;
using SpeechChem.Game;
using SpeechChem.Localization;
using SpeechChem.UI;
using ReactorModel = SpaceChem.Reactor.Reactor;

namespace SpeechChem.Screens.Reactor
{
    public sealed partial class ReactorEditorScreen
    {
        // ---- the crash overlay (user request 2026-10-07): closing a Reaction Error box stops the
        // run and wipes the scene, so the moment the box is gone the GRID shows the crash snapshot
        // instead (what the run log's error entry opens), cursor on the error cell — "as if Enter
        // was pressed on the log". Not modal, like pipe drawing: only the grid's cells change (their
        // readout, Shift+Backspace, Ctrl+arrows); every other stop and key works as usual. Escape
        // returns the grid to the live reactor; any edit (the program's members change) or a new
        // run ends it silently, the snapshot no longer matching.
        // EVERY REACTOR (user request 2026-10-09): in production / defense levels the error also
        // freezes the level's other reactors, so each is captured too; switching to one (Ctrl+Tab,
        // Ctrl+1-9, or from the pipeline) shows its own snapshot ("Reactor at cycle N" after its
        // name, no cell moved to — only the failed one has an error cell). Each reactor's ends on
        // its own Escape or edit; a new run (or another level) drops them all. ----

        /// <summary>The reactors as they stood at the last Reaction Error, by reactor; set by
        /// Patches/RunCapture (<see cref="RecordCrash"/>).</summary>
        private static readonly Dictionary<ReactorModel, ReactorSnapshot> CrashSnapshots = new Dictionary<ReactorModel, ReactorSnapshot>();

        /// <summary>The reactor that failed: its grid lands on the error cell once the box is closed
        /// (dropped by the pipeline when the box closes over it instead).</summary>
        internal static ReactorModel PendingCrashReactor;

        private ReactorSnapshot _crash;
        private int _crashSignature;

        public override bool ModalCapturesEscape => _crash != null;

        /// <summary>A Reaction Error opened (the run is paused under it): keep the failed reactor's
        /// snapshot and capture every other reactor of the level's pipeline (research levels have
        /// only the one).</summary>
        internal static void RecordCrash(ReactorModel failed, ReactorSnapshot snapshot, int cycle)
        {
            CrashSnapshots.Clear();
            PendingCrashReactor = failed;
            if (failed != null && snapshot != null) CrashSnapshots[failed] = snapshot;
            if (Class53.smethod_5<Class84>() != null) return;
            var pipeline = Class53.smethod_5<SpaceChem.Pipeline.PipelineEditor>()?.pipeline_0;
            if (pipeline == null) return;
            foreach (var rd in PipelineText.Reactors(pipeline))
            {
                var r = rd.class77_0?.reactor_0;
                if (r == null || CrashSnapshots.ContainsKey(r)) continue;
                try
                {
                    var s = ReactorSnapshot.Capture(r, null, cycle);
                    if (s != null) CrashSnapshots[r] = s;
                }
                catch (System.Exception ex) { Log.Error("[run] crash snapshot of another reactor", ex); }
            }
        }

        /// <summary>A new run or another level: the snapshots no longer apply.</summary>
        internal static void ClearCrashes()
        {
            CrashSnapshots.Clear();
            PendingCrashReactor = null;
        }

        /// <summary>Per frame: show this reactor's snapshot once the box is gone and the run
        /// stopped (the failed one lands on its error cell); end the overlay on an edit or a new
        /// run.</summary>
        private void WatchCrash(Class77 editor)
        {
            var r = editor.reactor_0;
            if (_crash != null)
            {
                if (Live || Signature(r) != _crashSignature)
                {
                    _crash = null;
                    CrashSnapshots.Remove(r);
                }
                return;
            }
            if (Live || r == null) return; // the box's close stops the run; wait for it
            if (!CrashSnapshots.TryGetValue(r, out var s)) return;
            bool failed = ReferenceEquals(PendingCrashReactor, r);
            if (failed) PendingCrashReactor = null;
            // Taken the first time a grid shows it; a later return compares against that.
            if (!s.Signature.HasValue) s.Signature = Signature(r);
            _crash = s;
            _crashSignature = s.Signature.Value;
            _zones.Forget(); // the landing names the zone
            int mx = _cursorX, my = _cursorY;
            bool found = false;
            for (int y = 0; y < s.Height && !found; y++)
                for (int x = 0; x < s.Width && !found; x++)
                    if (s.Marked[x, y]) { mx = x; my = y; found = true; }
            if (!failed)
            {
                // Arriving by a switch: the screen name is spoken and the landing reads the
                // snapshot's cell (the error cell, back in the failed reactor); say what the grid
                // shows in between.
                Speech.Tts.Speak(Loc.T("snapshot.title", new { n = s.Cycle }));
                if (found) FocusCell(mx, my);
                return;
            }
            FocusCell(mx, my, announce: false);
            Speech.Tts.Speak(Loc.T("snapshot.title", new { n = s.Cycle }) + ", " + CrashReadout(mx, my), interrupt: true);
        }

        /// <summary>Research levels: an invalid-molecule box closes over the reactor (the game shows
        /// no marker there). Land the grid cursor on the output zone that fed the refusing output,
        /// then read that output's line ("psi output: Oxygen, O2, 3 of 10").</summary>
        private void WatchInvalidMolecule(Class77 editor)
        {
            var building = Patches.DialogCapture.InvalidAt;
            if (building == null || Live) return; // at once: the old cell's re-announcement is never heard
            Patches.DialogCapture.InvalidAt = null;
            var rd = editor.reactorDraggable_0;
            var r = editor.reactor_0;
            if (rd == null || r == null) return;
            int index = -1;
            foreach (var kv in rd.class485_1)
            {
                if (ReferenceEquals(kv.Value.vmethod_0(), building)) { index = kv.Key; break; }
            }
            if (index < 0) return;
            var size = r.method_1();
            for (int y = 0; y < size.int_1; y++)
                for (int x = 0; x < size.int_0; x++)
                    if (ZoneOf(r, x, y, out bool input, out int zone) && !input && zone == index)
                    {
                        _jumps.Remember(Here());
                        _zones.Forget(); // the landing names the zone
                        FocusCell(x, y);
                        string line = OutputLine(index);
                        if (!string.IsNullOrEmpty(line)) Speech.Tts.Speak(line);
                        return;
                    }
        }

        /// <summary>Escape: back to the live grid, reading the cell as it is now.</summary>
        private void EndCrash()
        {
            if (_crash == null) return;
            _crash = null;
            CrashSnapshots.Remove(_reactor);
            _zones.Forget();
            Speech.Tts.Speak(CellReadout(_cursorX, _cursorY), interrupt: true);
        }

        /// <summary>The program's members and where they sit, as one number: any edit changes it.</summary>
        private static int Signature(ReactorModel r)
        {
            unchecked
            {
                int h = 17;
                if (r?.dictionary_1 == null) return h;
                foreach (var kv in r.dictionary_1)
                {
                    h = h * 31 + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(kv.Key);
                    h = h * 31 + kv.Value.vector2i_0.int_0 * 131 + kv.Value.vector2i_0.int_1 * 17 + (int)kv.Value.enum114_0;
                    if (kv.Key is Instruction i) h = h * 31 + (ReactorText.Label(i) ?? "").GetHashCode();
                }
                return h;
            }
        }

        /// <summary>A cell as it stood at the crash: coordinates, the zone when entered, contents
        /// on every layer with the waldos and atoms, "error here" on the box's marked cells.</summary>
        private string CrashReadout(int x, int y)
        {
            var s = _crash;
            if (s == null || x >= s.Width || y >= s.Height) return null;
            var parts = new List<string>();
            if (IsMarked(x, y)) parts.Add(Loc.T("reactor.marked"));
            parts.Add(Loc.T("reactor.cell", new { x = x + 1, y = y + 1 }));
            string region = _zones.Announce(x, y, s.Zones[x, y]);
            if (region != null) parts.Add(region);
            if (!string.IsNullOrEmpty(s.Contents[x, y])) parts.Add(s.Contents[x, y]);
            if (s.Marked[x, y]) parts.Add(Loc.T("snapshot.marked"));
            return string.Join(", ", parts.ToArray());
        }
    }
}
