namespace SpeechChem.UI
{
    /// <summary>
    /// A grid divided into named regions (a reactor's input and output zones and the chamber
    /// between them): the region is spoken in the readout of the first cell, and of the cell
    /// a directional landing reaches after crossing into a different region — leaving a zone
    /// included, so every region needs a name. Staying inside one says nothing.
    /// </summary>
    internal sealed class ZoneCrossing
    {
        private string _last;
        private bool _init;
        private int _x = -1, _y = -1;

        /// <summary>Start over: the next readout names its region.</summary>
        public void Reset()
        {
            _last = null;
            _init = false;
            _x = _y = -1;
        }

        /// <summary>The next landing names its region even when it is the one the cursor is in
        /// (a jump to a zone item).</summary>
        public void Forget() => _last = null;

        /// <summary>A landing on (x, y): note whether it crossed into a different region.</summary>
        public void Land(int x, int y, string region)
        {
            if (region != null && region != _last) { _x = x; _y = y; }
            else _x = _y = -1;
            _last = region;
        }

        /// <summary>The region to speak in (x, y)'s readout, or null.</summary>
        public string Announce(int x, int y, string region)
        {
            if (!_init)
            {
                _init = true;
                _last = region;
                return region;
            }
            return x == _x && y == _y ? region : null;
        }
    }
}
