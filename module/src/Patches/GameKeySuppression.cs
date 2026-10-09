using System;
using System.Collections.Generic;
using SpeechChem.Game;
using HarmonyLib;
using Impeller;

namespace SpeechChem.Patches
{
    /// <summary>
    /// The focus-mode key-suppression seam. Game screens read the keyboard through the input facade
    /// Class259, a per-frame snapshot of Class446 key states keyed by Impeller.Keys (= SDL SCANCODES):
    /// smethod_4(key, bool) = just pressed (smethod_3 is its one-argument wrapper), smethod_5(key) =
    /// held. Both are pure reads of the snapshot (decompile-verified; smethod_4 only books a repeat
    /// timestamp), so skipping the original is safe. While focus mode is ON and a MODELED screen is
    /// focused, our navigator owns the keys in <see cref="Keys"/>, so the game must see them as not
    /// pressed or every press double-acts.
    ///
    /// Escape is never swallowed (native back/close paths stay), except while the focused screen
    /// reports a mod-side modal (<see cref="Screens.Screen.ModalCapturesEscape"/>) — latched for the
    /// whole press, since the modal closes before polling screens read the key. Suppression never
    /// applies while the focused screen CapturesRawInput, on unmodeled screens (the game must stay
    /// fully playable), or with focus mode off.
    ///
    /// SECOND SEAM — widget screens. Class54 screens (menus, dialogs, text fields) also get keys
    /// through the engine's key-event path: Class185.vmethod_9 → the top Class54's method_9, which
    /// queues the press for its widget tree (GClass8: the screen's imethod_0 first — Escape cancels,
    /// Enter confirms some dialogs — then the focused widget, e.g. a text field's Enter/Backspace).
    /// A prefix on method_9 drops the same keys before they are queued. Characters travel separately
    /// (SDL text input → method_11) and reach the game only while a text-entry node is focused:
    /// the game's field keeps its keyboard focus whatever the mod's focus is, so typing on a dialog's
    /// button used to change the name silently (the Echopunks rule: a field takes keys only while
    /// armed). The same holds for the paste key (Ctrl+V → Keys.Paste).
    ///
    /// While a text-entry node is focused, Backspace belongs to the game's field (the navigator
    /// stands its own Backspace binding down), so it passes both seams.
    /// </summary>
    internal static class GameKeySuppression
    {
        // Scancodes the navigator's vocabulary claims (ui.* bindings + their numpad aliases).
        private static readonly HashSet<int> Keys = new HashSet<int>
        {
            40, // Return        (ui.activate)
            88, // KP_Enter      (ui.activate)
            43, // Tab           (ui.next/prev)
            42, // Backspace     (ui.secondary)
            79, // Right
            80, // Left
            81, // Down
            82, // Up
            90, // KP_2 (down)
            92, // KP_4 (left)
            94, // KP_6 (right)
            96, // KP_8 (up)
            74, // Home          (ui.home)
            77, // End           (ui.end)
            75, // PageUp
            78, // PageDown
            76, // Delete        (reactor delete; the game's own Delete acts on its mouse selection)
        };

        private const int EscapeScancode = 41;
        private const int Numrow1 = 30, Numrow4 = 33;
        private const int SpaceScancode = 44;

        private static bool CtrlHeld() => Input.SdlKeyboard.Held(224) || Input.SdlKeyboard.Held(228); // LCtrl, RCtrl
        private static bool AltHeld() => Input.SdlKeyboard.Held(226) || Input.SdlKeyboard.Held(230); // LAlt, RAlt
        private const int BackquoteScancode = 53;
        private const int BackspaceScancode = 42;
        private const int PasteKey = 515; // Impeller.Keys.Paste, the engine's Ctrl+V

        // An Escape press a mod-side modal took, held until the key is released. Polling screens
        // (the reactor's Class77, Reactor) read Escape in the game's update, AFTER our tick — by then
        // the Back handler has closed the modal, the reactor is the focused screen again, and an
        // unlatched check would let the same press open the exit prompt.
        private static bool _escapeLatched;

        /// <summary>FrameLoop step, after the keyboard snapshot and before input dispatch: latch
        /// Escape while a modal owns it, release the latch once the key is up.</summary>
        public static void LatchEscape()
        {
            if (!Input.SdlKeyboard.Held(EscapeScancode)) { _escapeLatched = false; return; }
            if (Input.SdlKeyboard.JustPressed(EscapeScancode) && Suppressed(EscapeScancode)) _escapeLatched = true;
        }

        public static void Apply(Harmony harmony)
        {
            try
            {
                harmony.Patch(Expr.MethodOf(() => Class259.smethod_4(default(Keys), false)),
                    prefix: new HarmonyMethod(typeof(GameKeySuppression), nameof(PressedPrefix)));
                harmony.Patch(Expr.MethodOf(() => Class259.smethod_5(default(Keys))),
                    prefix: new HarmonyMethod(typeof(GameKeySuppression), nameof(HeldPrefix)));
                harmony.Patch(Expr.MethodOf(() => default(Class54).method_9(default(Keys), false)),
                    prefix: new HarmonyMethod(typeof(GameKeySuppression), nameof(QueuePrefix)));
                harmony.Patch(Expr.MethodOf(() => default(Class54).method_11(default(char))),
                    prefix: new HarmonyMethod(typeof(GameKeySuppression), nameof(CharacterPrefix)));
                Log.Info("[patch] game key suppression armed");
            }
            catch (Exception ex) { Log.Error("[patch] key suppression failed to apply", ex); }
        }

        /// <summary>True when the game must not see this scancode this frame.</summary>
        internal static bool Suppressed(int scancode)
        {
            try
            {
                if (!FocusMode.Active) return false;
                var cur = Screens.ScreenManager.Current;
                if (cur == null || cur.CapturesRawInput) return false;
                if (scancode == EscapeScancode) return _escapeLatched || cur.ModalCapturesEscape;
                if (scancode == BackspaceScancode && UI.Navigation.TextEntryFocused) return false;
                // Ctrl+1..4 switch reactors (Common/ReactorSwitch): the game reads 1-4 as its play
                // speeds whatever the modifiers.
                if (scancode >= Numrow1 && scancode <= Numrow4 && CtrlHeld()) return true;
                // Alt+1..4 and Alt+` are stop jumps on the reactor and pipeline screens; the toolbar
                // reads 1-4 (speeds) and ` (stop) whatever the modifiers.
                if (((scancode >= Numrow1 && scancode <= Numrow4) || scancode == BackquoteScancode) && AltHeld()
                    && (cur is Screens.Reactor.ReactorEditorScreen || cur is Screens.Pipeline.PipelineEditorScreen)) return true;
                // Shift+Space / Ctrl+Space mark / clear the reactor grid's or the pipeline map's
                // rectangle; the toolbar reads Space as play / pause whatever the modifiers.
                if (scancode == SpaceScancode && (Input.SdlKeyboard.ShiftHeld || CtrlHeld())
                    && (cur is Screens.Reactor.ReactorEditorScreen || cur is Screens.Pipeline.PipelineEditorScreen)) return true;
                return Keys.Contains(scancode) && !cur.PassKeyToGame(scancode);
            }
            catch { return false; }
        }

        // __0 = the key argument (positional injection — shipping parameter names are obfuscated).
        private static bool PressedPrefix(Keys __0, ref bool __result)
        {
            if (!Suppressed((int)__0)) return true;
            __result = false;
            return false;
        }

        private static bool HeldPrefix(Keys __0, ref bool __result)
        {
            if (!Suppressed((int)__0)) return true;
            __result = false;
            return false;
        }

        // Class54.method_9(key, isRepeat): void — skipping it simply never queues the press.
        // Paste only here, never in the polling seams (the reactor's own Ctrl+V polls it).
        private static bool QueuePrefix(Keys __0) => (int)__0 == PasteKey ? CharacterPrefix() : !Suppressed((int)__0);

        /// <summary>Class54.method_11(char): a typed character reaches the game's widgets only while
        /// a text-entry node is focused (on modeled screens, with focus mode on).</summary>
        private static bool CharacterPrefix()
        {
            try
            {
                if (!FocusMode.Active) return true;
                var cur = Screens.ScreenManager.Current;
                if (cur == null || cur.CapturesRawInput) return true;
                return UI.Navigation.TextEntryFocused;
            }
            catch { return true; }
        }
    }
}
