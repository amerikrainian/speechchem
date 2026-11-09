using System.Collections.Generic;

namespace SpeechChem.Audio
{
    /// <summary>One sound to play: the tone, stereo gains (0-255) and mixer volume (0-128).</summary>
    internal struct VoiceRequest
    {
        public Tone Tone;
        public byte Left, Right;
        public int Volume;
    }

    /// <summary>The voices a <see cref="VoiceQueue"/> plays on (the mixer's reserved channels).</summary>
    internal interface IVoiceOutput
    {
        int Count { get; }
        bool Busy(int voice);

        /// <summary>Start the request on a free voice; false when it did not start.</summary>
        bool Start(int voice, VoiceRequest request);
    }

    /// <summary>
    /// The guarantee that no beep is lost (user rule 2026-11-09): every request waits, in order,
    /// until a voice has really started it. With every voice busy or a start refused, it stays at
    /// the head of the line and <see cref="Pump"/> tries again on the next frame.
    /// </summary>
    internal sealed class VoiceQueue
    {
        private readonly IVoiceOutput _output;
        private readonly Queue<VoiceRequest> _waiting = new Queue<VoiceRequest>();
        private int _next;

        public VoiceQueue(IVoiceOutput output) => _output = output;

        public int Waiting => _waiting.Count;

        /// <summary>Every request ever handed to a voice.</summary>
        public long Started { get; private set; }

        public void Add(VoiceRequest request)
        {
            _waiting.Enqueue(request);
            Pump();
        }

        public void Pump()
        {
            while (_waiting.Count > 0)
            {
                int voice = FreeVoice();
                if (voice < 0 || !_output.Start(voice, _waiting.Peek())) return;
                _waiting.Dequeue();
                Started++;
            }
        }

        private int FreeVoice()
        {
            int count = _output.Count;
            for (int i = 0; i < count; i++)
            {
                int voice = (_next + i) % count;
                if (_output.Busy(voice)) continue;
                _next = (voice + 1) % count;
                return voice;
            }
            return -1;
        }
    }
}
