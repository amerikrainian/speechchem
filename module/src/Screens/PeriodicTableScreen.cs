using System;
using System.Collections.Generic;
using SpaceChem;
using SpeechChem.Game;
using SpeechChem.Localization;
using SpeechChem.UI;
using SpeechChem.UI.Graph;

namespace SpeechChem.Screens
{
    /// <summary>
    /// Class76, the periodic table: the toolbar's reference view (enum123_0 = 0) and the element
    /// PICKER (1) that sensors, inline annotations and the ResearchNet builders open. The game draws
    /// a picture and hit-tests the mouse (method_7 positions / method_8 hit test); here the table is
    /// a grid laid out by GROUP (18 columns, periods 1-7, then the lanthanide and actinide rows under
    /// groups 4-17), with blank cells keeping the columns aligned for Up/Down. The four unidentified
    /// elements (200-203, the "Errata" strip, bool_1) follow as their own row when shown.
    ///
    /// A cell reads name, symbol, atomic number, maximum bonds, and "in this reactor's inputs" for
    /// the elements the game highlights (hashSet_0: the picker opened from a reactor collects its
    /// input molecules' elements); Shift+Backspace adds period and group. In picker mode Enter picks
    /// (the game's click: action_0(new Atom(e)), close, click sound). Escape stays native.
    /// </summary>
    public sealed class PeriodicTableScreen : Screen
    {
        private const int Columns = 18;
        private const string RowKey = "periodic.row";

        public override string Key => "periodic";

        private static Class76 Table => GameApi.TopScreen() as Class76;

        private static bool Picker(Class76 t) => t != null && (int)t.enum123_0 == 1;

        public override string ScreenName
        {
            get
            {
                var t = Table;
                return Picker(t) ? Loc.T("periodic.picker") : Loc.T("toolbar.periodic");
            }
        }

        public override bool IsActive() => Table != null;

        public override void Build(GraphBuilder b)
        {
            var t = Table;
            if (t == null) return;

            var grid = new Element?[9, Columns];
            foreach (Element e in AllElements())
            {
                int row, col;
                if (Place((int)e, out row, out col)) grid[row, col] = e;
            }

            ControlId start = null;
            for (int row = 0; row < 9; row++)
            {
                b.StartRow(RowKey);
                for (int col = 0; col < Columns; col++)
                {
                    var id = ControlId.Structural("periodic.cell." + row + "." + col);
                    var element = grid[row, col];
                    if (element == null)
                    {
                        b.AddItem(id, Blank());
                        continue;
                    }
                    var e = element.Value;
                    if (start == null && t.hashSet_0.Contains(e)) start = id;
                    b.AddItem(id, ElementNode(t, e, row, col));
                }
                b.EndRow();
            }

            // Arrows skip the gaps: each direction jumps to the nearest element that way when there is
            // one (H → He, B ← Be, Sc ↑ nothing: the blank above says so).
            for (int row = 0; row < 9; row++)
                for (int col = 0; col < Columns; col++)
                {
                    var from = ControlId.Structural("periodic.cell." + row + "." + col);
                    SkipTo(b, grid, from, row, col, 0, -1, GraphDir.Left);
                    SkipTo(b, grid, from, row, col, 0, 1, GraphDir.Right);
                    SkipTo(b, grid, from, row, col, -1, 0, GraphDir.Up);
                    SkipTo(b, grid, from, row, col, 1, 0, GraphDir.Down);
                }

            if (t.bool_1)
            {
                b.StartRow("periodic.errata");
                for (int n = 200; n < 204; n++)
                    b.AddItem(ControlId.Structural("periodic.errata." + n), ElementNode(t, (Element)n, -1, -1));
                b.EndRow();
            }

            b.AddItem(ControlId.Structural("periodic.back"), ProfileUi.Button(() => GameText.T("Back"), () => Table?.method_1(null)));
            b.SetStart(start ?? ControlId.Structural("periodic.cell.0.0"));
        }

        private static void SkipTo(GraphBuilder b, Element?[,] grid, ControlId from, int row, int col, int dr, int dc, GraphDir dir)
        {
            for (int r = row + dr, c = col + dc; r >= 0 && r < 9 && c >= 0 && c < Columns; r += dr, c += dc)
                if (grid[r, c] != null)
                {
                    b.Connect(from, dir, ControlId.Structural("periodic.cell." + r + "." + c));
                    return;
                }
        }

        private static NodeVtable Blank() => new NodeVtable
        {
            ControlType = ControlTypes.Text,
            Announcements = new[] { new NodeAnnouncement(() => Loc.T("periodic.blank"), kind: AnnouncementKinds.Label) },
            SpeaksOwnPosition = true,
        };

        private static NodeVtable ElementNode(Class76 t, Element e, int row, int col)
        {
            var vt = new NodeVtable
            {
                ControlType = ControlTypes.Text,
                Announcements = new[]
                {
                    new NodeAnnouncement(() => Readout(e), kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(() => Table != null && Table.hashSet_0.Contains(e) ? Loc.T("periodic.ininputs") : null,
                        kind: AnnouncementKinds.Tooltip),
                },
                SpeaksOwnPosition = true,
                OnTooltip = () => Speech.Tts.Speak(Where(row, col), interrupt: true),
            };
            if (Picker(t)) vt.OnActivate = () => Pick(e);
            return vt;
        }

        private static void Pick(Element e)
        {
            var t = Table;
            if (!Picker(t)) return;
            try
            {
                t.action_0(new Atom(e));
                t.method_1(null);
                Class428.class14_4.vmethod_0();
            }
            catch (Exception ex) { Log.Error("[periodic] pick failed", ex); }
        }

        /// <summary>"Carbon, C, 6, max bonds 4" — the hover panel's facts ("?" for the unidentified).</summary>
        private static string Readout(Element e)
        {
            bool unknown = e.smethod_4();
            return Loc.T("periodic.element", new
            {
                name = e.smethod_1(),
                symbol = e.smethod_2(),
                number = unknown ? "?" : ((int)e).ToString(),
                bonds = unknown ? "?" : e.smethod_3().ToString(),
            });
        }

        private static string Where(int row, int col)
        {
            if (row < 0) return GameText.T("Errata: Unidentified Elements");
            if (row >= 7) return Loc.T("periodic.fblock", new { period = row - 1 });
            return Loc.T("periodic.where", new { period = row + 1, group = col + 1 });
        }

        private static IEnumerable<Element> AllElements()
        {
            for (int n = 1; n <= 109; n++) yield return (Element)n;
        }

        /// <summary>An element's cell by group (0-based column) and period row; rows 7 and 8 are
        /// the lanthanides and actinides under groups 4-17. Mirrors the game's method_7 layout.</summary>
        internal static bool Place(int z, out int row, out int col)
        {
            row = col = -1;
            if (z == 1) { row = 0; col = 0; }
            else if (z == 2) { row = 0; col = 17; }
            else if (z >= 3 && z <= 4) { row = 1; col = z - 3; }
            else if (z >= 5 && z <= 10) { row = 1; col = z - 5 + 12; }
            else if (z >= 11 && z <= 12) { row = 2; col = z - 11; }
            else if (z >= 13 && z <= 18) { row = 2; col = z - 13 + 12; }
            else if (z >= 19 && z <= 36) { row = 3; col = z - 19; }
            else if (z >= 37 && z <= 54) { row = 4; col = z - 37; }
            else if (z >= 55 && z <= 56) { row = 5; col = z - 55; }
            else if (z >= 57 && z <= 70) { row = 7; col = z - 57 + 3; }
            else if (z >= 71 && z <= 86) { row = 5; col = z - 71 + 2; }
            else if (z >= 87 && z <= 88) { row = 6; col = z - 87; }
            else if (z >= 89 && z <= 102) { row = 8; col = z - 89 + 3; }
            else if (z >= 103 && z <= 109) { row = 6; col = z - 103 + 2; }
            return row >= 0;
        }
    }
}
