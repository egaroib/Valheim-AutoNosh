using UnityEngine;

namespace AutoNosh
{
    /// <summary>
    /// Tracks time since the local player last issued a real input, driven by
    /// <see cref="Patches.PlayerSetControlsPatch"/>. Used only to pause auto-eating while
    /// away, never to touch food decay itself.
    /// </summary>
    internal static class AfkTracker
    {
        private static float _lastActiveTime = Time.time;

        internal static float IdleSeconds => Time.time - _lastActiveTime;

        internal static void NoteActivity()
        {
            _lastActiveTime = Time.time;
        }
    }
}
