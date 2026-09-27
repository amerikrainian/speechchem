using System.Collections.Generic;
using SpeechChem.UI;

namespace SpeechChem.Screens.Reactor
{
    public sealed partial class ReactorEditorScreen
    {
        // Editing lands in the next commit (placement, menus, delete, clipboard, selection).

        private IEnumerable<ElementAction> EditActions() { yield break; }

        private void ResetEditState() { }

        private void ActivateCell(int x, int y) { }
    }
}
