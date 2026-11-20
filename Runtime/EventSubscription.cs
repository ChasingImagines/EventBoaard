using System;

namespace Yasin.EventBoard
{
    internal interface IEventChannelUnsubscriber
    {
        void UnsubscribeUntyped(Delegate listener);
    }

    /// <summary>
    /// EventBoard aboneliklerini tek bir IDisposable belirteci üzerinden yöneten,
    /// sıfır bellek tahsisli (Zero-GC) abonelik tutamacı.
    /// </summary>
    public readonly struct EventSubscription : IDisposable
    {
        public static readonly EventSubscription Empty = default;

        private readonly IEventChannelUnsubscriber _channel;
        private readonly Delegate _listener;

        internal EventSubscription(IEventChannelUnsubscriber channel, Delegate listener)
        {
            _channel = channel;
            _listener = listener;
        }

        public bool IsValid => _channel != null && _listener != null;

        public void Dispose()
        {
            if (_channel != null && _listener != null)
            {
                _channel.UnsubscribeUntyped(_listener);
            }
        }
    }
}
