using System;
using System.Collections.Generic;
using UnityEngine;

namespace Yasin.EventBoard
{
    /// <summary>
    /// Sıfır çöp (Zero-GC / Zero-Boxing) üreten, O(1) tam sayı kimlikli merkezi Blackboard ve Event Hub motoru.
    /// Değer tipleri (int, float, bool, Vector3, VoidSignal, özel struct) asla object'e dönüştürülmez.
    /// </summary>
    public static class EventBoard
    {
        private static class ChannelBank<T>
        {
            public static readonly Dictionary<int, EventChannelSlot<T>> Slots = new Dictionary<int, EventChannelSlot<T>>(32);
            private static bool _registeredReset;

            public static EventChannelSlot<T> GetOrCreate(int runtimeId, string fallbackPath = null)
            {
                if (!_registeredReset)
                {
                    _registeredReset = true;
                    RegisterBankReset(static () => Slots.Clear());
                }

                if (!Slots.TryGetValue(runtimeId, out EventChannelSlot<T> slot))
                {
                    slot = new EventChannelSlot<T>(runtimeId);
                    if (!string.IsNullOrEmpty(fallbackPath))
                    {
                        slot.Path = fallbackPath;
                    }
                    else if (_idToPath.TryGetValue(runtimeId, out string knownPath))
                    {
                        slot.Path = knownPath;
                    }

                    if (_idToGuid.TryGetValue(runtimeId, out string knownGuid))
                    {
                        slot.Guid = knownGuid;
                    }

                    Slots[runtimeId] = slot;
                    _diagnosticSlots[runtimeId] = slot;
                }
                else if (string.IsNullOrEmpty(slot.Path) && !string.IsNullOrEmpty(fallbackPath))
                {
                    slot.Path = fallbackPath;
                }

                return slot;
            }
        }

        private static readonly Dictionary<int, IEventChannelDiagnostic> _diagnosticSlots = new Dictionary<int, IEventChannelDiagnostic>(64);
        private static readonly Dictionary<int, string> _idToPath = new Dictionary<int, string>(64);
        private static readonly Dictionary<int, string> _idToGuid = new Dictionary<int, string>(64);
        private static readonly List<Action> _bankResetActions = new List<Action>(16);
        private static bool _initialized;
        private static int _version = 1;

        internal static int Version => _version;

        internal static EventChannelSlot<T> GetOrCreateSlot<T>(int runtimeId, string fallbackPath = null)
        {
            if (runtimeId == 0)
                return null;

            EnsureInitialized();
            return ChannelBank<T>.GetOrCreate(runtimeId, fallbackPath);
        }

        public static IReadOnlyCollection<IEventChannelDiagnostic> DiagnosticSlots
        {
            get
            {
                EnsureInitialized();
                return _diagnosticSlots.Values;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void InitializeOnLoad()
        {
            ResetAll();
            EnsureInitialized();
        }

        public static void EnsureInitialized()
        {
            if (_initialized)
                return;

            _initialized = true;
            IReadOnlyList<EventBoardGroup> groups = EventBoardGroup.GetAllGroups();
            if (groups != null && groups.Count > 0)
            {
                HydrateFromGroups(groups);
            }
        }

        /// <summary>
        /// Birden fazla EventBoardGroup SO içerisindeki tüm kanalların varsayılan (DefaultValue) ve kalıcılık (IsPersistent)
        /// ayarlarını ilgili generic kanallara (Zero-Boxing ile) yükler.
        /// </summary>
        public static void HydrateFromGroups(IReadOnlyList<EventBoardGroup> groups)
        {
            if (groups == null)
                return;

            _initialized = true;
            for (int i = 0; i < groups.Count; i++)
            {
                HydrateFromGroup(groups[i]);
            }
        }

        /// <summary>
        /// Tek bir EventBoardGroup SO içerisindeki kanalları merkezi EventBoard motoruna yükler.
        /// </summary>
        public static void HydrateFromGroup(EventBoardGroup group)
        {
            if (group == null)
                return;

            _initialized = true;
            IReadOnlyList<EventBoardGroup.EventEntry> entries = group.Events;
            if (entries == null)
                return;

            for (int i = 0; i < entries.Count; i++)
            {
                EventBoardGroup.EventEntry entry = entries[i];
                if (entry == null || string.IsNullOrEmpty(entry.guid))
                    continue;

                int runtimeId = ComputeId(entry.guid);
                string fullPath = group.GetFullChannelName(entry);

                _idToGuid[runtimeId] = entry.guid;
                _idToPath[runtimeId] = fullPath;

                if (entry.payload != null)
                {
                    entry.payload.ConfigureChannel(runtimeId, entry.guid, fullPath, entry.isPersistent);
                }
            }
        }

        internal static void ConfigureChannel<T>(
            int runtimeId,
            string guid,
            string eventPath,
            T defaultValue,
            bool hasDefaultValue,
            bool isPersistent)
        {
            if (runtimeId == 0)
                return;

            _idToGuid[runtimeId] = guid ?? string.Empty;
            _idToPath[runtimeId] = eventPath ?? string.Empty;

            EventChannelSlot<T> slot = ChannelBank<T>.GetOrCreate(runtimeId, eventPath);
            slot.ConfigureMetadata(guid, eventPath, defaultValue, hasDefaultValue, isPersistent);
        }

        private static void RegisterBankReset(Action resetAction)
        {
            if (resetAction != null && !_bankResetActions.Contains(resetAction))
            {
                _bankResetActions.Add(resetAction);
            }
        }

        /// <summary>
        /// Sabit ve deterministik 32-bit FNV-1a Hash hesaplar (Sıfır GC tahsisi).
        /// Boş olmayan dizgeler için asla 0 döndürmez.
        /// </summary>
        public static int ComputeId(string key)
        {
            if (string.IsNullOrEmpty(key))
                return 0;

            unchecked
            {
                uint hash = 2166136261u;
                for (int i = 0; i < key.Length; i++)
                {
                    char c = key[i];
                    if (c >= 'A' && c <= 'Z')
                        c = (char)(c + 32);

                    hash ^= c;
                    hash *= 16777619u;
                }

                int result = (int)hash;
                return result == 0 ? 1 : result;
            }
        }

        /// <summary>
        /// Kanal değerini Blackboard'a yazar ve tüm aboneleri sıfır çöp (Zero-GC) ile tetikler.
        /// </summary>
        public static void Publish<T>(int runtimeId, T payload, string fallbackPath = null)
        {
            if (runtimeId == 0)
                return;

            EnsureInitialized();
            EventChannelSlot<T> slot = ChannelBank<T>.GetOrCreate(runtimeId, fallbackPath);
            slot.Publish(payload);
        }

        /// <summary>
        /// Değer yalnızca mevcut Blackboard değerinden farklıysa yayınlar (Yüksek frekans / spam koruması).
        /// </summary>
        public static bool PublishIfChanged<T>(int runtimeId, T payload, string fallbackPath = null)
        {
            if (runtimeId == 0)
                return false;

            EnsureInitialized();
            EventChannelSlot<T> slot = ChannelBank<T>.GetOrCreate(runtimeId, fallbackPath);
            return slot.PublishIfChanged(payload);
        }

        /// <summary>
        /// Aboneleri tetiklemeden (veya isteğe bağlı tetikleyerek) Blackboard durumunu günceller.
        /// </summary>
        public static void SetState<T>(int runtimeId, T value, bool notify = false, string fallbackPath = null)
        {
            if (runtimeId == 0)
                return;

            EnsureInitialized();
            EventChannelSlot<T> slot = ChannelBank<T>.GetOrCreate(runtimeId, fallbackPath);
            slot.SetState(value, notify);
        }

        /// <summary>
        /// Değer yalnızca mevcut Blackboard değerinden farklıysa durumu günceller (Sıfır gereksiz yazma).
        /// </summary>
        public static bool SetStateIfChanged<T>(int runtimeId, T value, bool notify = false, string fallbackPath = null)
        {
            if (runtimeId == 0)
                return false;

            EnsureInitialized();
            EventChannelSlot<T> slot = ChannelBank<T>.GetOrCreate(runtimeId, fallbackPath);
            return slot.SetStateIfChanged(value, notify);
        }

        /// <summary>
        /// Blackboard'daki son değeri (veya DefaultValue'yu) sıfır GC ile okur.
        /// </summary>
        public static bool TryGet<T>(int runtimeId, out T value)
        {
            if (runtimeId == 0)
            {
                value = default;
                return false;
            }

            EnsureInitialized();
            if (ChannelBank<T>.Slots.TryGetValue(runtimeId, out EventChannelSlot<T> slot))
            {
                return slot.TryGet(out value);
            }

            value = default;
            return false;
        }

        /// <summary>
        /// Blackboard'daki güncel değeri döndürür; değer yoksa verilen yedek (fallback) değeri döndürür.
        /// </summary>
        public static T GetValueOrDefault<T>(int runtimeId, T fallback = default)
        {
            if (runtimeId == 0)
                return fallback;

            EnsureInitialized();
            if (ChannelBank<T>.Slots.TryGetValue(runtimeId, out EventChannelSlot<T> slot))
            {
                return slot.GetValueOrDefault(fallback);
            }

            return fallback;
        }

        /// <summary>
        /// Kanalda geçerli bir Blackboard değeri olup olmadığını döndürür.
        /// </summary>
        public static bool HasValue<T>(int runtimeId)
        {
            if (runtimeId == 0)
                return false;

            EnsureInitialized();
            if (ChannelBank<T>.Slots.TryGetValue(runtimeId, out EventChannelSlot<T> slot))
            {
                return slot.HasValue || slot.HasDefaultValue;
            }

            return false;
        }

        /// <summary>
        /// Kanala abone olur. <paramref name="replayLatest"/> true ise mevcut Blackboard değeri anında iletilir.
        /// </summary>
        public static void Subscribe<T>(int runtimeId, Action<T> listener, bool replayLatest = false, string fallbackPath = null)
        {
            if (runtimeId == 0 || listener == null)
                return;

            EnsureInitialized();
            EventChannelSlot<T> slot = ChannelBank<T>.GetOrCreate(runtimeId, fallbackPath);
            slot.Subscribe(listener, replayLatest);
        }

        /// <summary>
        /// Kanal aboneliğinden çıkar.
        /// </summary>
        public static void Unsubscribe<T>(int runtimeId, Action<T> listener)
        {
            if (runtimeId == 0 || listener == null)
                return;

            if (ChannelBank<T>.Slots.TryGetValue(runtimeId, out EventChannelSlot<T> slot))
            {
                slot.Unsubscribe(listener);
            }
        }

        /// <summary>
        /// Belirli bir kanalın Blackboard durumunu temizler.
        /// </summary>
        public static void ClearChannel<T>(int runtimeId, bool restoreDefault = true)
        {
            if (runtimeId == 0)
                return;

            if (ChannelBank<T>.Slots.TryGetValue(runtimeId, out EventChannelSlot<T> slot))
            {
                slot.ClearState(restoreDefault);
            }
        }

        public static void RemoveChannel<T>(int runtimeId)
        {
            if (runtimeId == 0)
                return;

            ChannelBank<T>.Slots.Remove(runtimeId);
            _diagnosticSlots.Remove(runtimeId);
            _idToPath.Remove(runtimeId);
            _idToGuid.Remove(runtimeId);
        }

        /// <summary>
        /// Sahne geçişlerinde kalıcı olmayan (IsPersistent == false) tüm kanalların Blackboard değerlerini temizler.
        /// </summary>
        public static void ClearNonPersistent(bool restoreDefaults = true)
        {
            foreach (var kvp in _diagnosticSlots)
            {
                IEventChannelDiagnostic slot = kvp.Value;
                if (slot != null && !slot.IsPersistent)
                {
                    slot.ClearState(restoreDefaults);
                }
            }
        }

        /// <summary>
        /// Tüm abonelikleri ve geçici durumları sıfırlar (Oyun başlangıcı ve testler için).
        /// </summary>
        public static void ResetAll()
        {
            for (int i = 0; i < _bankResetActions.Count; i++)
            {
                _bankResetActions[i]?.Invoke();
            }

            _diagnosticSlots.Clear();
            _idToPath.Clear();
            _idToGuid.Clear();
            _initialized = false;
            _version++;
        }

        public static bool TryGetDiagnosticSlot(int runtimeId, out IEventChannelDiagnostic diagnostic)
        {
            EnsureInitialized();
            return _diagnosticSlots.TryGetValue(runtimeId, out diagnostic);
        }

        public static string GetChannelPath(int runtimeId, string fallback = "")
        {
            if (_idToPath.TryGetValue(runtimeId, out string path) && !string.IsNullOrEmpty(path))
                return path;
            return fallback;
        }
    }
}
