using System;
using System.Runtime.InteropServices;

namespace SpeechChem.Game
{
    /// <summary>
    /// The mod's own P/Invoke over the game's SDL2.dll (x86, already loaded in-process, so the module
    /// name resolves to the loaded copy). Two capabilities: reading SDL's internal keyboard state (our
    /// input sensor, independent of what the game consumes) and pushing synthetic events into the
    /// queue the game's event pump (Class184.method_17) drains every frame.
    /// </summary>
    internal static class SdlNative
    {
        private const string Dll = "SDL2.dll";

        public const uint MouseButtonDownEvent = 0x401; // SDL_MOUSEBUTTONDOWN (1025)
        public const uint MouseButtonUpEvent = 0x402;   // SDL_MOUSEBUTTONUP (1026)
        public const uint KeyDownEvent = 0x300;         // SDL_KEYDOWN (768)
        public const uint KeyUpEvent = 0x301;           // SDL_KEYUP (769)

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GetKeyboardState")]
        private static extern IntPtr GetKeyboardStateRaw(out int numkeys);

        /// <summary>Copy SDL's internal held-key array (1 byte per scancode) into
        /// <paramref name="buffer"/> (grown as needed); returns the number of valid entries. SDL updates
        /// the array during its event pump regardless of whether the app consumes the key events.</summary>
        public static int GetKeyboardState(ref byte[] buffer)
        {
            int numkeys;
            IntPtr state = GetKeyboardStateRaw(out numkeys);
            if (state == IntPtr.Zero || numkeys <= 0) return 0;
            if (buffer == null || buffer.Length < numkeys) buffer = new byte[numkeys];
            Marshal.Copy(state, buffer, 0, numkeys);
            return numkeys;
        }

        /// <summary>True while the scancode is held, straight off SDL's state array (one-off query).</summary>
        public static bool ScancodeHeld(int scancode)
        {
            int numkeys;
            IntPtr state = GetKeyboardStateRaw(out numkeys);
            if (state == IntPtr.Zero || scancode < 0 || scancode >= numkeys) return false;
            return Marshal.ReadByte(state, scancode) != 0;
        }

        // SDL_Event is a 56-byte union; SDL copies the full union from the pointer we hand it, so the
        // managed struct must be at least that big (Size=64 on purpose). Fields mirror
        // SDL_MouseButtonEvent.
        [StructLayout(LayoutKind.Sequential, Size = 64)]
        private struct MouseButtonEvent
        {
            public uint Type;
            public uint Timestamp;
            public uint WindowId;
            public uint Which;
            public byte Button;
            public byte State;
            public byte Clicks;
            public byte Padding;
            public int X;
            public int Y;
        }

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_PushEvent")]
        private static extern int PushMouseEventRaw(ref MouseButtonEvent ev);

        /// <summary>Deposit a synthetic LEFT button press or release. The engine maps button 1 to its
        /// left-button handlers and ignores the event's coordinates (see <see cref="SyntheticClick"/>).
        /// True when the event was queued.</summary>
        public static bool PushMouseButton(bool down)
        {
            var ev = new MouseButtonEvent
            {
                Type = down ? MouseButtonDownEvent : MouseButtonUpEvent,
                Button = 1,
                State = down ? (byte)1 : (byte)0,
                Clicks = 1,
            };
            return PushMouseEventRaw(ref ev) >= 1;
        }

        public const uint TextInputEvent = 0x303;       // SDL_TEXTINPUT (771)

        // SDL_TextInputEvent view of the same union: type, timestamp, windowID, char text[32] (UTF-8).
        [StructLayout(LayoutKind.Sequential, Size = 64)]
        private struct TextEvent
        {
            public uint Type;
            public uint Timestamp;
            public uint WindowId;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] Text;
        }

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_PushEvent")]
        private static extern int PushTextEventRaw(ref TextEvent ev);

        /// <summary>Deposit a synthetic SDL_TEXTINPUT carrying <paramref name="text"/> (at most 31 UTF-8
        /// bytes) — the channel the game's pump turns into per-character vmethod_11 calls, exactly as
        /// typing does. Dev/test use (the probe's "type" command).</summary>
        public static bool PushText(string text)
        {
            var bytes = new byte[32];
            var utf8 = System.Text.Encoding.UTF8.GetBytes(text ?? "");
            Array.Copy(utf8, bytes, Math.Min(utf8.Length, 31));
            var ev = new TextEvent { Type = TextInputEvent, WindowId = 1, Text = bytes };
            return PushTextEventRaw(ref ev) >= 1;
        }

        // SDL_KeyboardEvent view of the same union.
        [StructLayout(LayoutKind.Sequential, Size = 64)]
        private struct KeyEvent
        {
            public uint Type;
            public uint Timestamp;
            public uint WindowId;
            public byte State;   // 1 pressed / 0 released
            public byte Repeat;
            public byte Padding2;
            public byte Padding3;
            public int Scancode; // what the game's key sets track (Impeller.Keys values ARE scancodes)
            public int Sym;
            public ushort Mod;
            public uint Unused;
        }

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_PushEvent")]
        private static extern int PushKeyEventRaw(ref KeyEvent ev);

        /// <summary>Deposit a synthetic key event. The game's pump records the SCANCODE in its key
        /// state (Class446), so its just-pressed polls fire as for a real key; it does not route by
        /// window id. Push the UP in a later frame than the DOWN, or both drain in one pump and the
        /// per-frame poll never sees the key held.</summary>
        public static bool PushKey(int scancode, int sym, bool down)
        {
            var ev = new KeyEvent
            {
                Type = down ? KeyDownEvent : KeyUpEvent,
                WindowId = 1,
                State = down ? (byte)1 : (byte)0,
                Scancode = scancode,
                Sym = sym,
            };
            return PushKeyEventRaw(ref ev) >= 1;
        }
    }
}
