using System;

namespace Yasin.EventBoard
{
    /// <summary>
    /// Parametresiz (veri taşımayan) olaylar ve tetikleyiciler için sıfır bellek (Zero-GC) birim tipi.
    /// Örnek kullanım: [SerializeField] private EventKey&lt;VoidSignal&gt; onGameSaved;
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
        /// Parametresiz VoidSignal kanalını argüman vermeden tetikler.
        /// </summary>
        public static void Publish(this EventKey<VoidSignal> key)
        {
            key.Publish(VoidSignal.Default);
        }
    }
}

