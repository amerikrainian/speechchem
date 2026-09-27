using System.Collections.Generic;
using Impeller;
using SpaceChem;
using SpaceChem.Reactor;
using SpeechChem.Game;
using SpeechChem.Localization;

namespace SpeechChem.Screens.Reactor
{
    public sealed partial class ReactorEditorScreen
    {
        // ---- waldo readouts (N red, M blue; Shift jumps the grid cursor there). A waldo (Class188)
        // sits on its START marker while the reactor is stopped; during a run: its cell, heading
        // (vector2i_1, the movement it will make), the molecule it holds, and the game's own waiting
        // text (method_2: "WAITING (α)" …), rotating / blocked on sync when so. ----

        private void SpeakWaldo(bool red, bool jump)
        {
            var r = Model;
            if (r == null) return;
            Class188 waldo;
            if (!r.dictionary_2.TryGetValue((Enum114)(red ? ReactorText.Red : ReactorText.Blue), out waldo) || waldo == null) return;

            Vector2i cell = waldo.method_0();
            var parts = new List<string> { Loc.T(red ? "reactor.waldo.red" : "reactor.waldo.blue") };
            if (!Live)
            {
                // Stopped: the waldo is drawn on its START marker.
                StartInstruction start = FindStart(r, red);
                if (start != null)
                {
                    var bin = r.method_19(start);
                    if (bin.HasValue) cell = bin.Value.vector2i_0;
                    parts.Add(Loc.T("reactor.cell", new { x = cell.int_0 + 1, y = cell.int_1 + 1 }));
                    parts.Add(ReactorText.Label(start));
                }
            }
            else
            {
                parts.Add(Loc.T("reactor.cell", new { x = cell.int_0 + 1, y = cell.int_1 + 1 }));
                string heading = Heading(waldo.vector2i_1);
                if (heading != null) parts.Add(Loc.T("reactor.waldo.heading", new { dir = heading }));
                var held = waldo.moleculeSheet_0?.molecule_0;
                parts.Add(held != null ? Loc.T("reactor.waldo.holding", new { molecule = MoleculeText.NameAndFormula(held) }) : Loc.T("reactor.waldo.empty"));
                string waiting = waldo.method_2();
                if (!string.IsNullOrEmpty(waiting)) parts.Add(waiting);
                if (waldo.bool_3) parts.Add(Loc.T("reactor.waldo.rotating"));
                if (waldo.bool_4) parts.Add(Loc.T("reactor.waldo.sync"));
            }
            Speech.Tts.Speak(string.Join(", ", parts.ToArray()), interrupt: true);
            if (jump) FocusCell(cell.int_0, cell.int_1, announce: false); // the waldo line already names the cell
        }

        private static StartInstruction FindStart(SpaceChem.Reactor.Reactor r, bool red)
        {
            foreach (var kv in r)
                if (kv.Key is StartInstruction s && (int)kv.Value.enum114_0 == (red ? ReactorText.Red : ReactorText.Blue)) return s;
            return null;
        }

        /// <summary>A unit movement vector as a direction word (y grows downward).</summary>
        private static string Heading(Vector2i v)
        {
            if (v.int_0 > 0) return Loc.T("dir.right");
            if (v.int_0 < 0) return Loc.T("dir.left");
            if (v.int_1 > 0) return Loc.T("dir.down");
            if (v.int_1 < 0) return Loc.T("dir.up");
            return null;
        }
    }
}
