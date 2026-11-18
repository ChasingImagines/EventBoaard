using System;
using System.Collections.Generic;
using UnityEngine;

namespace Yasin.EventBoard
{
    /// <summary>
    /// Editör tanılaması ve sahne geçişi temizliği (ClearNonPersistent / Reset) için
    /// tip bağımsız kanal yönetim arayüzü. Oyun içi sıcak yolda (Publish/TryGet) kullanılmaz.
    /// </summary>
    public interface IEventChannelDiagnostic
    {
        int RuntimeId { get; }
        string Guid { get; }
        string Path { get; }
        Type PayloadType { get; }
        bool HasValue { get; }
        bool HasDefaultValue { get; }
        bool IsSaveable { get; }
        int SubscriberCount { get; }
        int PublishCount { get; }
        int LastPublishFrame { get; }
        float LastPublishTime { get; }

        string GetFormattedValue();
        string GetFormattedDefaultValue();
        void CopySubscribersTo(List<Delegate> target);
        void ClearState(bool restoreDefault);
        void ResetChannelCompletely();
        bool TryPublishFromEditor(object boxedValue);
        string ExportValueJson();
        bool TryImportValueJson(string json, bool notify);
    }

    /// <summary>
    /// Belirli bir <typeparamref name="T"/> tipi için sıfır çöp (Zero-GC / Zero-Boxing)
    /// Blackboard durumu ve abone listesi tutan kanal hücresi.
    /// </summary>
    internal sealed class EventChannelSlot<T> : IEventChannelDiagnostic
    {
        public int RuntimeId { get; private set; }
        public string Guid { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public Type PayloadType => typeof(T);

        public T CurrentValue;
        public T DefaultValue;
        public bool HasValue { get; set; }
        public bool HasDefaultValue { get; set; }
        public bool IsSaveable { get; set; }

        public int PublishCount { get; private set; }
        public int LastPublishFrame { get; private set; } = -1;
        public float LastPublishTime { get; private set; }

        private readonly List<Action<T>> _listeners = new List<Action<T>>(4);
        private readonly List<Action<T>> _dispatchBuffer = new List<Action<T>>(4);

        public int SubscriberCount => _listeners.Count;

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
            IsSaveable = isSaveable;

            if (!HasValue && HasDefaultValue)
            {
                CurrentValue = DefaultValue;
                HasValue = true;
            }
        }

        public void SetState(T value, bool notify)
        {
            CurrentValue = value;
            HasValue = true;

            if (notify)
            {
                Dispatch(value);
            }
        }

        public bool SetStateIfChanged(T value, bool notify)
        {
            if (HasValue && EqualityComparer<T>.Default.Equals(CurrentValue, value))
                return false;

            CurrentValue = value;
            HasValue = true;

            if (notify)
            {
                PublishCount++;
                LastPublishFrame = Time.frameCount;
                LastPublishTime = Time.unscaledTime;
                Dispatch(value);
            }

            return true;
        }

        public void Publish(T payload)
        {
            CurrentValue = payload;
            HasValue = true;
            PublishCount++;
            LastPublishFrame = Time.frameCount;
            LastPublishTime = Time.unscaledTime;

            Dispatch(payload);
        }

        public bool PublishIfChanged(T payload)
        {
            if (HasValue && EqualityComparer<T>.Default.Equals(CurrentValue, payload))
                return false;

            Publish(payload);
            return true;
        }

        private void Dispatch(T payload)
        {
            int count = _listeners.Count;
            if (count == 0)
                return;

            _dispatchBuffer.Clear();
            for (int i = 0; i < count; i++)
            {
                _dispatchBuffer.Add(_listeners[i]);
            }

            for (int i = 0; i < _dispatchBuffer.Count; i++)
            {
                Action<T> callback = _dispatchBuffer[i];
                if (callback == null)
                    continue;

                // Yok edilmiş (Destroyed) bir MonoBehaviour/ScriptableObject aboneliği unuttuysa otomatik temizle
                if (callback.Target is UnityEngine.Object unityTarget && unityTarget == null)
                {
                    _listeners.Remove(callback);
#if UNITY_EDITOR
                    Debug.LogWarning($"[EventBoard] '{Path}' (#{RuntimeId}) kanalında yok edilmiş (Destroyed) bir objeye ait unutulmuş abonelik otomatik temizlendi. Lütfen OnDisable/OnDestroy içinde Unsubscribe çağırın.");
#endif
                    continue;
                }

                try
                {
                    callback.Invoke(payload);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }

            _dispatchBuffer.Clear();
        }

        public bool TryGet(out T value)
        {
            if (HasValue)
            {
                value = CurrentValue;
                return true;
            }

            if (HasDefaultValue)
            {
                value = DefaultValue;
                return true;
            }

            value = default;
            return false;
        }

        public T GetValueOrDefault(T fallback = default)
        {
            if (HasValue)
                return CurrentValue;
            if (HasDefaultValue)
                return DefaultValue;
            return fallback;
        }

        public void Subscribe(Action<T> listener, bool replayLatest)
        {
            if (listener == null)
                return;

            if (!_listeners.Contains(listener))
            {
                _listeners.Add(listener);
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
        }

        public void Unsubscribe(Action<T> listener)
        {
            if (listener == null)
                return;

            _listeners.Remove(listener);
        }

        public void ClearState(bool restoreDefault)
        {
            if (restoreDefault && HasDefaultValue)
            {
                CurrentValue = DefaultValue;
                HasValue = true;
            }
            else
            {
                CurrentValue = default;
                HasValue = false;
            }
        }

        public void ResetChannelCompletely()
        {
            _listeners.Clear();
            _dispatchBuffer.Clear();
            PublishCount = 0;
            LastPublishFrame = -1;
            LastPublishTime = 0f;
            ClearState(restoreDefault: true);
        }

        public string GetFormattedValue()
        {
            if (!HasValue)
                return HasDefaultValue ? $"{DefaultValue} (Varsayılan)" : "— (Boş)";

            return CurrentValue != null ? CurrentValue.ToString() : "null";
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
                target.Add(_listeners[i]);
            }
        }

        public bool TryPublishFromEditor(object boxedValue)
        {
            if (boxedValue is T typedValue)
            {
                Publish(typedValue);
                return true;
            }
            if (boxedValue == null && !typeof(T).IsValueType)
            {
                Publish(default);
                return true;
            }
            return false;
        }

        public string ExportValueJson()
        {
            if (!HasValue && !HasDefaultValue)
                return null;
            T val = HasValue ? CurrentValue : DefaultValue;
            if (val == null)
                return "null";
            if (typeof(T) == typeof(string))
                return JsonUtility.ToJson(new StringWrapper { value = val as string });
            if (typeof(T).IsPrimitive)
                return val.ToString();
            return JsonUtility.ToJson(val);
        }

        public bool TryImportValueJson(string json, bool notify)
        {
            if (string.IsNullOrEmpty(json))
                return false;
            try
            {
                if (typeof(T) == typeof(string))
                {
                    StringWrapper w = JsonUtility.FromJson<StringWrapper>(json);
                    SetState((T)(object)w.value, notify);
                    return true;
                }
                if (typeof(T) == typeof(int))
                {
                    if (int.TryParse(json, out int res))
                    {
                        SetState((T)(object)res, notify);
                        return true;
                    }
                }
                else if (typeof(T) == typeof(float))
                {
                    if (float.TryParse(json, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float res))
                    {
                        SetState((T)(object)res, notify);
                        return true;
                    }
                }
                else if (typeof(T) == typeof(bool))
                {
                    if (bool.TryParse(json, out bool res))
                    {
                        SetState((T)(object)res, notify);
                        return true;
                    }
                }
                else
                {
                    T parsed = JsonUtility.FromJson<T>(json);
                    SetState(parsed, notify);
                    return true;
                }
            }
            catch
            {
                // Hatalı JSON durumunda DefaultValue korunur
            }
            return false;
        }

        [Serializable]
        private struct StringWrapper
        {
            public string value;
        }
    }
}

