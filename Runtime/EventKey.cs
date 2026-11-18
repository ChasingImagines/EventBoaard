using System;
using UnityEngine;

namespace Yasin.EventBoard
{
    /// <summary>
    /// Editör denetim (Audit) ve bağımlılık matrisi araçlarının tüm EventKey&lt;T&gt; alanlarını
    /// tek tip üzerinden okuyabilmesini sağlayan arayüz.
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
    /// Kod içine hiçbir string gömmeden, yalnızca Inspector'dan seçilen,
    /// isim veya grup SO değişikliklerinde kırılmayan (GUID tabanlı) ve derleme zamanı tip güvenli (Compile-Time Type Safe)
    /// Blackboard &amp; Event anahtarı.
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
        /// Kanalın çalışma zamanındaki benzersiz tam sayı (O(1) GUID Hash) kimliği.
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

        /// <summary>
        /// Blackboard'da bu kanal için kayıtlı son değeri (veya Grup SO içindeki DefaultValue'yu) sıfır GC ve sıfır Dictionary araması ile döndürür.
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
        /// Bu kanalda şu an geçerli bir Blackboard değeri (veya varsayılan değer) olup olmadığını döndürür.
        /// </summary>
        public bool HasValue
        {
            get
            {
                EventChannelSlot<T> slot = ResolveSlot();
                return slot != null && (slot.HasValue || slot.HasDefaultValue);
            }
        }

        /// <summary>
        /// Değeri Blackboard'a yazar ve bu kanalı dinleyen tüm abonelere sıfır çöp (Zero-GC) ile yayınlar.
        /// </summary>
        /// <summary>
        /// Fires this event channel and notifies all active subscribers with the payload.
        /// Alias for Publish.
        /// </summary>
        public void Raise(T payload) => Publish(payload);

        public void Publish(T payload)
        {
            EventChannelSlot<T> slot = ResolveSlot();
            if (slot == null)
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[EventBoard] Inspector'dan atanmamış (Unassigned) bir EventKey<{typeof(T).Name}> üzerinden Publish çağrıldı!");
#endif
                return;
            }

            slot.Publish(payload);
        }

        /// <summary>
        /// Değer yalnızca mevcut Blackboard değerinden farklıysa yayınlar (Yüksek frekans / gereksiz tetikleme koruması).
        /// </summary>
        public bool PublishIfChanged(T payload)
        {
            EventChannelSlot<T> slot = ResolveSlot();
            if (slot == null)
                return false;

            return slot.PublishIfChanged(payload);
        }

        /// <summary>
        /// Aboneleri tetiklemeden (veya isteğe bağlı tetikleyerek) yalnızca Blackboard üzerindeki durumu günceller.
        /// </summary>
        public void SetState(T value, bool notify = false)
        {
            EventChannelSlot<T> slot = ResolveSlot();
            if (slot == null)
                return;

            slot.SetState(value, notify);
        }

        /// <summary>
        /// Değer yalnızca mevcut Blackboard değerinden farklıysa durumu günceller (Sıfır gereksiz yazma).
        /// </summary>
        public bool SetStateIfChanged(T value, bool notify = false)
        {
            EventChannelSlot<T> slot = ResolveSlot();
            if (slot == null)
                return false;

            return slot.SetStateIfChanged(value, notify);
        }

        /// <summary>
        /// Blackboard'daki güncel değeri (veya DefaultValue'yu) sıfır GC ile okur.
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
        /// Blackboard'daki güncel değeri döndürür; değer yoksa verilen yedek (fallback) değeri döndürür.
        /// </summary>
        public T GetValueOrDefault(T fallback = default)
        {
            EventChannelSlot<T> slot = ResolveSlot();
            return slot != null ? slot.GetValueOrDefault(fallback) : fallback;
        }

        /// <summary>
        /// Kanala abone olur. <paramref name="replayLatest"/> true ise, kanalda halihazırda bir Blackboard
        /// değeri varsa abone olur olmaz anında bir kez tetiklenir (Late-Join / UI senkronizasyonu için idealdir).
        /// </summary>
        public void Subscribe(Action<T> listener, bool replayLatest = false)
        {
            if (!IsAssigned)
                return;

            EventChannelSlot<T> slot = ResolveSlot();
            if (slot == null)
                return;

            slot.Subscribe(listener, replayLatest);
        }

        /// <summary>
        /// Kanal aboneliğinden çıkar.
        /// </summary>
        public void Unsubscribe(Action<T> listener)
        {
            if (!IsAssigned || listener == null)
                return;

            EventChannelSlot<T> slot = ResolveSlot();
            if (slot == null)
                return;

            slot.Unsubscribe(listener);
        }

        /// <summary>
        /// Bu kanalın Blackboard hafızasını temizler (veya varsayılan değerine döndürür).
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
