namespace SpeechChem
{
    /// <summary>
    /// Whether the mod's navigation owns the keyboard. WrathAccess pairs this with the game's own
    /// "keyboard disabled" lever; SpaceChem has no such lever, so for now this is a plain flag that
    /// gates nav dispatch and announcements — ON by default (the game barely uses the keyboard
    /// outside text fields). Its game-side half is Patches/GameKeySuppression, which swallows our
    /// nav keys on modeled screens so the game doesn't also react to them.
    /// </summary>
    public static class FocusMode
    {
        public static bool Active = true;
    }
}
