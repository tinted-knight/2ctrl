using System.Collections.Generic;

namespace TwoCtrl
{
    // No OS dependencies: only physical keyboard transitions enter this state machine.
    public sealed class TapDetector
    {
        public const int LeftControl = 0xA2, RightControl = 0xA3;
        private readonly HashSet<int> held = new HashSet<int>();
        private int candidate;
        public void Cancel() { candidate = 0; }
        public void Seed(int key) { held.Add(key); Cancel(); }
        public int Process(int key, bool down, bool injected)
        {
            if (injected) { Cancel(); return 0; }
            if (down)
            {
                if (held.Contains(key)) return 0;
                bool solo = held.Count == 0;
                held.Add(key);
                candidate = solo && (key == LeftControl || key == RightControl) ? key : 0;
                return 0;
            }
            held.Remove(key);
            int result = candidate == key && held.Count == 0 ? key : 0;
            Cancel();
            return result;
        }
    }
}
