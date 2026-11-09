using System;
using System.Globalization;
using SpeechChem.Narration;

namespace SpeechChem.Audio
{
    internal enum SoundCue
    {
        EnemyMove,
    }

    /// <summary>
    /// The mod's sound volumes (Guildrun's model): a master percent, and per cue a signed offset from
    /// it, so moving the master moves every sound while their relative levels hold. A cue's shown
    /// volume is master + offset, clamped to 0-200. Stored in the narration settings file ("sound.*",
    /// only when not the default), so the Settings dialog's Save Changes / Cancel apply to them.
    /// </summary>
    internal static class SoundSettings
    {
        public const int DefaultVolume = 100, MaxVolume = Beep.MaxPercent, Step = 10, LargeStep = 50;

        private const string MasterKey = "sound.master";

        private static string OffsetKey(SoundCue cue) => "sound.offset." + cue;

        private static int Read(string key, int fallback, bool draft)
        {
            string text = draft ? NarrationStore.GetDraft(key) : NarrationStore.Get(key);
            return int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value) ? value : fallback;
        }

        private static int Clamp(int value) => Math.Max(0, Math.Min(MaxVolume, value));

        public static int Master(bool draft = false) => Clamp(Read(MasterKey, DefaultVolume, draft));

        public static int Volume(SoundCue cue, bool draft = false) => Clamp(Master(draft) + Read(OffsetKey(cue), 0, draft));

        public static void SetMaster(int value)
        {
            value = Clamp(value);
            NarrationStore.SetDraft(MasterKey, value == DefaultVolume ? null : value.ToString(CultureInfo.InvariantCulture));
        }

        public static void SetVolume(SoundCue cue, int value)
        {
            int offset = Clamp(value) - Master(draft: true);
            NarrationStore.SetDraft(OffsetKey(cue), offset == 0 ? null : offset.ToString("+0;-0", CultureInfo.InvariantCulture));
        }
    }
}
