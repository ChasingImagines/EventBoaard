using System;
using UnityEngine;

namespace Yasin.EventBoard
{
    /// <summary>
    /// Allows Editor audit and dependency matrix tools to inspect all EventKey&lt;T&gt; fields uniformly.
    /// </summary>
    public interface IEventKey
    {
        string Guid { get; }
        string CachedPath { get; }
        int Id { get; }
        bool IsAssigned { get; }
        Type ValueType { get; }
    }

    /// <summary>
    /// Compile-time type-safe, GUID-backed Blackboard and Event key selected in the Inspector without magic strings.
    /// Survives event renames and group moves seamlessly.
    /// </summary>
    [Serializable]
    public struct EventKey<T> : IEventKey, ISerializationCallbackReceiver, IEquatable<EventKey<T>>
    {
        [SerializeField] private string guid;
        [SerializeField] private string cachedPath;

        [NonSerialized] private int _runtimeId;
        [NonSerialized] private int _cachedVersion;
        [NonSerialized] private EventChannelSlot<T> _cachedSlot;

        public string Guid => guid;
        public string CachedPath => cachedPath;
        public Type ValueType => typeof(T);

        public bool IsAssigned => !string.IsNullOrEmpty(guid);

        /// <summary>
        /// Deterministic O(1) runtime integer ID hashed from the channel's GUID.
        /// </summary>
        public int Id
        {
            get
            {
                if (_runtimeId == 0 && !string.IsNullOrEmpty(guid))
                {
                    _runtimeId = EventBoard.ComputeId(guid);
                }
                return _runtimeId;
            }
        }

        private EventChannelSlot<T> ResolveSlot()
        {
            if (_cachedSlot != null && _cachedVersion == EventBoard.Version)
                return _cachedSlot;

            int id = Id;
            if (id == 0)
                return null;

            _cachedSlot = EventBoard.GetOrCreateSlot<T>(id, cachedPath);
            _cachedVersion = EventBoard.Version;
            return _cachedSlot;
        }

        private EventChannelSlot<T> ResolveExistingSlot()
        {
            if (_cachedSlot != null && _cachedVersion == EventBoard.Version)
                return _cachedSlot;

            int id = Id;
            if (id == 0)
                return null;

            return EventBoard.TryGetExistingSlot<T>(id);
        }

        /// <summary>
        /// Returns the latest Blackboard value (or default value) with zero GC allocation and zero dictionary lookups once cached.
        /// </summary>
        public T Value
        {
            get
            {
                EventChannelSlot<T> slot = ResolveSlot();
                return slot != null ? slot.GetValueOrDefault() : default;
            }
        }

        /// <summary>
        /// Returns whether this channel currently holds a valid Blackboard or default value.
        /// </summary>
        public bool HasValue
        {
            get
            {
                EventChannelSlot<T> slot = ResolveSlot();
                return slot != null && slot.HasValue;
            }
        }

        /// <summary>
        /// Fires this event channel and notifies all active subscribers with the payload.
        /// Alias for Publish.
        /// </summary>
        public void Raise(T payload) => Publish(payload);

        /// <summary>
        /// Writes the payload to the Blackboard and dispatches it to all active subscribers with zero GC allocation.
        /// </summary>
        public void Publish(T payload)
        {
            EventChannelSlot<T> slot = ResolveSlot();
            if (slot == null)
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[EventBoard] Publish called on an unassigned EventKey<{typeof(T).Name}>!");
#endif
                return;
            }

            slot.Publish(payload);
        }

        /// <summary>
        /// Publishes the payload only if it differs from the current Blackboard value (spam guard).
        /// </summary>
        public bool PublishIfChanged(T payload)
        {
            EventChannelSlot<T> slot = ResolveSlot();
            if (slot == null)
                return false;

            return slot.PublishIfChanged(payload);
        }

        /// <summary>
        /// Updates only the Blackboard state silently (or optionally notifies subscribers).
        /// </summary>
        public void SetState(T value, bool notify = false)
        {
            EventChannelSlot<T> slot = ResolveSlot();
            if (slot == null)
                return;

            slot.SetState(value, notify);
        }

        /// <summary>
        /// Updates the Blackboard state only if the value differs from the current value.
        /// </summary>
        public bool SetStateIfChanged(T value, bool notify = false)
        {
            EventChannelSlot<T> slot = ResolveSlot();
            if (slot == null)
                return false;

            return slot.SetStateIfChanged(value, notify);
        }

        /// <summary>
        /// Reads the current Blackboard value (or default value) with zero GC allocation.
        /// </summary>
        public bool TryGet(out T value)
        {
            EventChannelSlot<T> slot = ResolveSlot();
            if (slot == null)
            {
                value = default;
                return false;
            }

            return slot.TryGet(out value);
        }

        /// <summary>
        /// Returns the current Blackboard value, or the specified fallback if unset.
        /// </summary>
        public T GetValueOrDefault(T fallback = default)
        {
            EventChannelSlot<T> slot = ResolveSlot();
            return slot != null ? slot.GetValueOrDefault(fallback) : fallback;
        }

        /// <summary>
        /// Subscribes to this channel. When <paramref name="replayLatest"/> is true, immediately invokes the listener
        /// if a Blackboard or default value is already present (ideal for late-join UI synchronization).
        /// </summary>
        public EventSubscription Subscribe(Action<T> listener, bool replayLatest = false)
        {
            if (!IsAssigned || listener == null)
                return EventSubscription.Empty;

            EventChannelSlot<T> slot = ResolveSlot();
            return slot != null ? slot.Subscribe(listener, null, hasExplicitOwner: false, replayLatest) : EventSubscription.Empty;
        }

        /// <summary>
        /// Subscribes with an explicit owner object; automatically cleans up when the owner is destroyed, even for lambdas/closures.
        /// </summary>
        public EventSubscription Subscribe(UnityEngine.Object owner, Action<T> listener, bool replayLatest = false)
        {
            if (!IsAssigned || listener == null || owner == null)
                return EventSubscription.Empty;

            EventChannelSlot<T> slot = ResolveSlot();
            return slot != null ? slot.Subscribe(listener, owner, hasExplicitOwner: true, replayLatest) : EventSubscription.Empty;
        }

        /// <summary>
        /// Subscribes a parameterless callback without closure allocations.
        /// </summary>
        public EventSubscription Subscribe(Action listener, bool replayLatest = false)
        {
            if (!IsAssigned || listener == null)
                return EventSubscription.Empty;

            EventChannelSlot<T> slot = ResolveSlot();
            return slot != null ? slot.Subscribe(listener, null, hasExplicitOwner: false, replayLatest) : EventSubscription.Empty;
        }

        /// <summary>
        /// Subscribes a parameterless callback with an explicit owner object for automatic cleanup on destruction.
        /// </summary>
        public EventSubscription Subscribe(UnityEngine.Object owner, Action listener, bool replayLatest = false)
        {
            if (!IsAssigned || listener == null || owner == null)
                return EventSubscription.Empty;

            EventChannelSlot<T> slot = ResolveSlot();
            return slot != null ? slot.Subscribe(listener, owner, hasExplicitOwner: true, replayLatest) : EventSubscription.Empty;
        }

        /// <summary>
        /// Unsubscribes a typed listener from this channel.
        /// </summary>
        public void Unsubscribe(Action<T> listener)
        {
            if (!IsAssigned || listener == null)
                return;

            ResolveExistingSlot()?.Unsubscribe(listener);
        }

        /// <summary>
        /// Unsubscribes a parameterless listener from this channel.
        /// </summary>
        public void Unsubscribe(Action listener)
        {
            if (!IsAssigned || listener == null)
                return;

            ResolveExistingSlot()?.Unsubscribe(listener);
        }

        /// <summary>
        /// Clears this channel's Blackboard state (optionally restoring its default value).
        /// </summary>
        public void ClearState(bool restoreDefault = true)
        {
            EventChannelSlot<T> slot = ResolveSlot();
            if (slot == null)
                return;

            slot.ClearState(restoreDefault);
        }

        public void OnBeforeSerialize() { }

        public void OnAfterDeserialize()
        {
            _runtimeId = !string.IsNullOrEmpty(guid) ? EventBoard.ComputeId(guid) : 0;
            _cachedSlot = null;
            _cachedVersion = 0;
        }

        public bool Equals(EventKey<T> other) => Id == other.Id && Id != 0;
        public override bool Equals(object obj) => obj is EventKey<T> other && Equals(other);
        public override int GetHashCode() => Id;
        public override string ToString() => !string.IsNullOrEmpty(cachedPath) ? cachedPath : guid;
    }
}
