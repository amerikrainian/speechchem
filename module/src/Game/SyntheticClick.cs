using System;

namespace SpeechChem.Game
{
    /// <summary>
    /// A left mouse click delivered through SDL's own event queue, so the game runs its real click
    /// path. The engine's event pump (Class184.method_17) turns SDL_MOUSEBUTTONDOWN (button 1) into
    /// Class185.vmethod_5 → Class259.bool_0 ("left pressed this frame") + bool_4 ("left held"), and
    /// SDL_MOUSEBUTTONUP into bool_1 + bool_4 = false. The position is NOT taken from the button event
    /// (the game tracks it from motion events), so a click lands wherever the hidden cursor is —
    /// right for the click-anywhere gates this exists for, wrong for hit-tested widgets.
    ///
    /// The UP is held back a few frames: a down and its up drained in the same pump would leave the
    /// frame with "held" already cleared, and screens that act on press-then-release would see
    /// neither. Driven by the module FrameLoop.
    /// </summary>
    internal static class SyntheticClick
    {
        private const int UpDelayFrames = 2;
        private static int _pendingUp = -1;

        /// <summary>Queue a full click. False when SDL refused the event.</summary>
        public static bool Click()
        {
            try
            {
                if (_pendingUp >= 0) return false; // one click in flight at a time
                if (!SdlNative.PushMouseButton(down: true)) return false;
                _pendingUp = UpDelayFrames;
                return true;
            }
            catch (Exception ex) { Log.Error("[click] push failed", ex); return false; }
        }

        /// <summary>FrameLoop step: release the button once the down has been seen.</summary>
        public static void Tick()
        {
            if (_pendingUp < 0) return;
            if (_pendingUp-- > 0) return;
            try { SdlNative.PushMouseButton(down: false); }
            catch (Exception ex) { Log.Error("[click] release failed", ex); }
        }
    }
}
