using System;

namespace Yasin.EventBoard
{
    /// <summary>
    /// Zero-GC unit type for parameterless (data-free) events and triggers.
    /// Example: [SerializeField] private EventKey&lt;VoidSignal&gt; onGameSaved;
    /// </summary>
    [Serializable]
    public readonly struct VoidSignal : IEquatable<VoidSignal>
    {
        public static readonly VoidSignal Default = default;

        public bool Equals(VoidSignal other) => true;
        public override bool Equals(object obj) => obj is VoidSignal;
        public override int GetHashCode() => 0;
        public override string ToString() => "Void";
    }

    public static class VoidSignalExtensions
    {
        /// <summary>
        /// Fires a parameterless VoidSignal channel without needing to pass an argument.
        /// </summary>
        public static void Publish(this EventKey<VoidSignal> key)
        {
            key.Publish(VoidSignal.Default);
        }

        /// <summary>
        /// Fires this parameterless VoidSignal channel.
        /// </summary>
        public static void Raise(this EventKey<VoidSignal> key)
        {
            key.Publish(VoidSignal.Default);
        }
    }
}
