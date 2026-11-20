using System;
using System.Collections.Generic;
using UnityEngine;

namespace Yasin.EventBoard
{
    /// <summary>
    /// Type-agnostic channel management interface for Editor diagnostics and scene/reset cleanup.
    /// Not used on the gameplay hot path (Publish/TryGet).
    /// </summary>
    public interface IEventChannelDiagnostic
    {
        int RuntimeId { get; }
        string Guid { get; }
        string Path { get; }
        Type PayloadType { get; }
        bool HasValue { get; }
        bool HasRuntimeValue { get; }
        bool HasDefaultValue { get; }
        bool IsSaveable { get; }
        bool SupportsSaveable { get; }
        int SubscriberCount { get; }
        int PublishCount { get; }
        int LastPublishFrame { get; }
        float LastPublishTime { get; }

        string GetFormattedValue();
        string GetFormattedDefaultValue();
        void CopySubscribersTo(List<Delegate> target);
        void ClearState(bool restoreDefault);
        void RestoreDefaultState(bool notify);
        void ResetChannelCompletely();
        bool TryPublishFromEditor(object boxedValue);
        string ExportValueJson();
        bool TryImportValueJson(string json, bool notify);
    }

    internal interface IEventChannelInternal : IEventChannelDiagnostic, IEventChannelUnsubscriber
    {
        void RemoveSelfFromBank();
    }

    /// <summary>
    /// Zero-GC, zero-boxing, re-entrancy-safe channel slot holding Blackboard state and listeners for type <typeparamref name="T"/>.
    /// </summary>
    internal sealed class EventChannelSlot<T> : IEventChannelInternal
    {
        private const int MaxDispatchDepth = 32;
        private static readonly bool IsReferenceType = !typeof(T).IsValueType;
        private static readonly bool IsUnityObjectType = typeof(UnityEngine.Object).IsAssignableFrom(typeof(T));
        private static readonly bool IsVoidType = typeof(T) == typeof(VoidSignal);

        private struct ListenerEntry
        {
            public Action<T> Callback;
            public Action ParameterlessCallback;
            public UnityEngine.Object Owner;
            public bool HasExplicitOwner;
            public bool Removed;
        }

        public int RuntimeId { get; private set; }
        public string Guid { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public Type PayloadType => typeof(T);

        public T CurrentValue;
        public T DefaultValue;
        public bool HasRuntimeValue { get; private set; }
        public bool HasDefaultValue { get; set; }
        public bool IsSaveable { get; set; }
        public bool SupportsSaveable => !IsUnityObjectType && !IsVoidType;

        private bool _defaultSuppressed;
        private bool EffectiveHasDefault => HasDefaultValue && !_defaultSuppressed;

        public bool HasValue => HasRuntimeValue || EffectiveHasDefault;

        public int PublishCount { get; private set; }
        public int LastPublishFrame { get; private set; } = -1;
        public float LastPublishTime { get; private set; }

        private readonly List<ListenerEntry> _listeners = new List<ListenerEntry>(4);
        private int _activeListenerCount;
        private int _dispatchDepth;
        private bool _hasRemovals;

        public int SubscriberCount => _activeListenerCount;

        public EventChannelSlot(int runtimeId)
        {
            RuntimeId = runtimeId;
        }

        public void ConfigureMetadata(string guid, string path, T defaultValue, bool hasDefault, bool isSaveable)
        {
            if (!string.IsNullOrEmpty(guid))
                Guid = guid;
            if (!string.IsNullOrEmpty(path))
                Path = path;

            DefaultValue = defaultValue;
            HasDefaultValue = hasDefault;
            IsSaveable = isSaveable && SupportsSaveable;
            _defaultSuppressed = false;
        }

        public void SetState(T value, bool notify)
        {
            CurrentValue = value;
            HasRuntimeValue = true;
            _defaultSuppressed = false;

            if (notify)
            {
                RecordPublishMetrics();
                Dispatch(value);
            }
        }

        public bool SetStateIfChanged(T value, bool notify)
        {
            if (TryGet(out T existing) && EqualityComparer<T>.Default.Equals(existing, value))
                return false;

            SetState(value, notify);
            return true;
        }

        public void Publish(T payload)
        {
            CurrentValue = payload;
            HasRuntimeValue = true;
            _defaultSuppressed = false;
            RecordPublishMetrics();

            Dispatch(payload);
        }

        public bool PublishIfChanged(T payload)
        {
            if (TryGet(out T existing) && EqualityComparer<T>.Default.Equals(existing, payload))
                return false;

            Publish(payload);
            return true;
        }

        private void RecordPublishMetrics()
        {
            PublishCount++;
            LastPublishFrame = Time.frameCount;
            LastPublishTime = Time.unscaledTime;
        }

        private void Dispatch(T payload)
        {
            int snapshotCount = _listeners.Count;
            if (snapshotCount == 0)
                return;

            if (_dispatchDepth >= MaxDispatchDepth)
            {
                Debug.LogError($"[EventBoard] Infinite nested Publish loop detected on '{Path}' (#{RuntimeId}) (Depth >= {MaxDispatchDepth}) and aborted!");
                return;
            }

            _dispatchDepth++;
            try
            {
                for (int i = 0; i < snapshotCount && i < _listeners.Count; i++)
                {
                    ListenerEntry entry = _listeners[i];
                    if (entry.Removed || (entry.Callback == null && entry.ParameterlessCallback == null))
                        continue;

                    object delegateTarget = entry.Callback != null
                        ? entry.Callback.Target
                        : entry.ParameterlessCallback.Target;

                    bool isDestroyed = (entry.HasExplicitOwner && entry.Owner == null) ||
                                       (delegateTarget is UnityEngine.Object unityTarget && unityTarget == null);

                    if (isDestroyed)
                    {
                        entry.Removed = true;
                        entry.Callback = null;
                        entry.ParameterlessCallback = null;
                        entry.Owner = null;
                        _listeners[i] = entry;
                        _activeListenerCount--;
                        _hasRemovals = true;
#if UNITY_EDITOR
                        Debug.LogWarning($"[EventBoard] Automatically cleaned up a forgotten subscription belonging to a destroyed object on '{Path}' (#{RuntimeId}). Please call Unsubscribe or Dispose in OnDisable/OnDestroy.");
#endif
                        continue;
                    }

                    try
                    {
                        if (entry.Callback != null)
                        {
                            entry.Callback.Invoke(payload);
                        }
                        else
                        {
                            entry.ParameterlessCallback.Invoke();
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                    }
                }
            }
            finally
            {
                _dispatchDepth--;
                if (_dispatchDepth == 0 && _hasRemovals)
                {
                    CompactRemovedListeners();
                }
            }
        }

        private void CompactRemovedListeners()
        {
            _hasRemovals = false;
            for (int i = _listeners.Count - 1; i >= 0; i--)
            {
                if (_listeners[i].Removed)
                {
                    _listeners.RemoveAt(i);
                }
            }
        }

        public bool TryGet(out T value)
        {
            if (HasRuntimeValue)
            {
                value = CurrentValue;
                return true;
            }

            if (EffectiveHasDefault)
            {
                value = DefaultValue;
                return true;
            }

            value = default;
            return false;
        }

        public T GetValueOrDefault(T fallback = default)
        {
            if (HasRuntimeValue)
                return CurrentValue;
            if (EffectiveHasDefault)
                return DefaultValue;
            return fallback;
        }

        public EventSubscription Subscribe(Action<T> listener, UnityEngine.Object owner, bool hasExplicitOwner, bool replayLatest)
        {
            if (listener == null || (hasExplicitOwner && owner == null))
                return EventSubscription.Empty;

            bool exists = false;
            for (int i = 0; i < _listeners.Count; i++)
            {
                ListenerEntry entry = _listeners[i];
                if (!entry.Removed && entry.Callback == listener)
                {
                    exists = true;
                    break;
                }
            }

            if (!exists)
            {
                _listeners.Add(new ListenerEntry
                {
                    Callback = listener,
                    Owner = owner,
                    HasExplicitOwner = hasExplicitOwner
                });
                _activeListenerCount++;
            }

            if (replayLatest && TryGet(out T current))
            {
                try
                {
                    listener.Invoke(current);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }

            return new EventSubscription(this, listener);
        }

        public EventSubscription Subscribe(Action listener, UnityEngine.Object owner, bool hasExplicitOwner, bool replayLatest)
        {
            if (listener == null || (hasExplicitOwner && owner == null))
                return EventSubscription.Empty;

            bool exists = false;
            for (int i = 0; i < _listeners.Count; i++)
            {
                ListenerEntry entry = _listeners[i];
                if (!entry.Removed && entry.ParameterlessCallback == listener)
                {
                    exists = true;
                    break;
                }
            }

            if (!exists)
            {
                _listeners.Add(new ListenerEntry
                {
                    ParameterlessCallback = listener,
                    Owner = owner,
                    HasExplicitOwner = hasExplicitOwner
                });
                _activeListenerCount++;
            }

            if (replayLatest && HasValue)
            {
                try
                {
                    listener.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }

            return new EventSubscription(this, listener);
        }

        public void Unsubscribe(Action<T> listener)
        {
            if (listener == null)
                return;

            for (int i = _listeners.Count - 1; i >= 0; i--)
            {
                ListenerEntry entry = _listeners[i];
                if (!entry.Removed && entry.Callback == listener)
                {
                    RemoveListenerAt(i, entry);
                    break;
                }
            }
        }

        public void Unsubscribe(Action listener)
        {
            if (listener == null)
                return;

            for (int i = _listeners.Count - 1; i >= 0; i--)
            {
                ListenerEntry entry = _listeners[i];
                if (!entry.Removed && entry.ParameterlessCallback == listener)
                {
                    RemoveListenerAt(i, entry);
                    break;
                }
            }
        }

        public void UnsubscribeUntyped(Delegate listener)
        {
            if (listener == null)
                return;

            if (listener is Action<T> typed)
            {
                Unsubscribe(typed);
            }
            else if (listener is Action parameterless)
            {
                Unsubscribe(parameterless);
            }
        }

        private void RemoveListenerAt(int index, ListenerEntry entry)
        {
            _activeListenerCount--;
            if (_dispatchDepth > 0)
            {
                entry.Removed = true;
                entry.Callback = null;
                entry.ParameterlessCallback = null;
                entry.Owner = null;
                _listeners[index] = entry;
                _hasRemovals = true;
            }
            else
            {
                _listeners.RemoveAt(index);
            }
        }

        public void ClearState(bool restoreDefault)
        {
            CurrentValue = default;
            HasRuntimeValue = false;
            _defaultSuppressed = !restoreDefault;
        }

        public void RestoreDefaultState(bool notify)
        {
            ClearState(restoreDefault: true);
            if (notify && TryGet(out T val))
            {
                RecordPublishMetrics();
                Dispatch(val);
            }
        }

        public void ResetChannelCompletely()
        {
            if (_dispatchDepth > 0)
            {
                for (int i = 0; i < _listeners.Count; i++)
                {
                    ListenerEntry entry = _listeners[i];
                    if (!entry.Removed)
                    {
                        entry.Removed = true;
                        entry.Callback = null;
                        entry.ParameterlessCallback = null;
                        entry.Owner = null;
                        _listeners[i] = entry;
                    }
                }
                _hasRemovals = true;
            }
            else
            {
                _listeners.Clear();
                _hasRemovals = false;
            }

            _activeListenerCount = 0;
            PublishCount = 0;
            LastPublishFrame = -1;
            LastPublishTime = 0f;
            ClearState(restoreDefault: true);
        }

        public void RemoveSelfFromBank()
        {
            EventBoard.RemoveFromBankInternal<T>(RuntimeId);
            ResetChannelCompletely();
        }

        public string GetFormattedValue()
        {
            if (HasRuntimeValue)
                return CurrentValue != null ? CurrentValue.ToString() : "null";

            if (EffectiveHasDefault)
                return DefaultValue != null ? $"{DefaultValue} (Default)" : "null (Default)";

            return "— (Empty)";
        }

        public string GetFormattedDefaultValue()
        {
            if (!HasDefaultValue)
                return "—";
            return DefaultValue != null ? DefaultValue.ToString() : "null";
        }

        public void CopySubscribersTo(List<Delegate> target)
        {
            target.Clear();
            for (int i = 0; i < _listeners.Count; i++)
            {
                ListenerEntry entry = _listeners[i];
                if (entry.Removed)
                    continue;

                Delegate del = (Delegate)entry.Callback ?? entry.ParameterlessCallback;
                if (del != null)
                {
                    target.Add(del);
                }
            }
        }

        public bool TryPublishFromEditor(object boxedValue)
        {
            if (boxedValue is T typedValue)
            {
                Publish(typedValue);
                return true;
            }
            if (boxedValue == null && IsReferenceType)
            {
                Publish(default);
                return true;
            }
            return false;
        }

        public string ExportValueJson()
        {
            if (!SupportsSaveable || !TryGet(out T val))
                return null;

            return EventBoard.Serializer.SerializeValue(val);
        }

        public bool TryImportValueJson(string json, bool notify)
        {
            if (!SupportsSaveable || string.IsNullOrEmpty(json))
                return false;

            if (EventBoard.Serializer.TryDeserializeValue(json, out T imported))
            {
                SetState(imported, notify);
                return true;
            }

            return false;
        }
    }
}
