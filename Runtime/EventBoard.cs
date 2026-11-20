using System;
using System.Collections.Generic;
using UnityEngine;

namespace Yasin.EventBoard
{
    /// <summary>
    /// Zero-GC, zero-boxing, O(1) integer-hashed central Blackboard and Event Hub engine.
    /// Value types (int, float, bool, Vector3, VoidSignal, custom structs) are never boxed.
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
        private static EventBoardRegistry _registry;
        private static readonly List<EventBoardGroup> _dynamicGroups = new List<EventBoardGroup>(4);
        private static readonly List<EventBoardGroup> _registeredGroupsScratch = new List<EventBoardGroup>(16);
        private static readonly HashSet<EventBoardGroup> _registeredGroupsSeen = new HashSet<EventBoardGroup>();
        private static bool _initialized;
        private static int _version = 1;

        internal static int Version => _version;
        public static EventBoardRegistry Registry => _registry;

        internal static EventChannelSlot<T> GetOrCreateSlot<T>(int runtimeId, string fallbackPath = null)
        {
            if (runtimeId == 0)
                return null;

            EnsureInitialized();
            return ChannelBank<T>.GetOrCreate(runtimeId, fallbackPath);
        }

        internal static EventChannelSlot<T> TryGetExistingSlot<T>(int runtimeId)
        {
            if (runtimeId == 0)
                return null;

            return ChannelBank<T>.Slots.TryGetValue(runtimeId, out EventChannelSlot<T> slot) ? slot : null;
        }

        internal static void RemoveFromBankInternal<T>(int runtimeId)
        {
            ChannelBank<T>.Slots.Remove(runtimeId);
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
        private static void OnSubsystemRegistration()
        {
            ResetAll(clearDynamicGroups: true);
            EventBoardGroup.ResetStaticState();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void OnBeforeSceneLoad()
        {
            EnsureInitialized();
        }

        public static void SetRegistry(EventBoardRegistry registry)
        {
            _registry = registry;
            if (registry != null && _initialized)
            {
                HydrateFromGroups(registry.Groups);
            }
        }

        /// <summary>
        /// Dynamically registers an extra EventBoardGroup SO loaded at runtime (e.g., via DLC or Addressables).
        /// </summary>
        public static void RegisterGroup(EventBoardGroup group)
        {
            if (group == null)
                return;

            if (!_dynamicGroups.Contains(group))
            {
                _dynamicGroups.Add(group);
            }

            HydrateFromGroup(group);
        }

        /// <summary>
        /// Unregisters a dynamically registered EventBoardGroup SO.
        /// </summary>
        public static void UnregisterGroup(EventBoardGroup group, bool removeChannels = false)
        {
            if (group == null)
                return;

            _dynamicGroups.Remove(group);

            if (removeChannels && group.Events != null)
            {
                for (int i = 0; i < group.Events.Count; i++)
                {
                    EventBoardGroup.EventEntry entry = group.Events[i];
                    if (entry != null && !string.IsNullOrEmpty(entry.guid))
                    {
                        RemoveChannelById(entry.RuntimeId);
                    }
                }
            }
        }

        public static IReadOnlyList<EventBoardGroup> GetRegisteredGroups()
        {
            _registeredGroupsScratch.Clear();
            _registeredGroupsSeen.Clear();

            if (_registry != null && _registry.Groups != null)
            {
                IReadOnlyList<EventBoardGroup> regGroups = _registry.Groups;
                for (int i = 0; i < regGroups.Count; i++)
                {
                    EventBoardGroup g = regGroups[i];
                    if (g != null && _registeredGroupsSeen.Add(g))
                    {
                        _registeredGroupsScratch.Add(g);
                    }
                }
            }

            for (int i = 0; i < _dynamicGroups.Count; i++)
            {
                EventBoardGroup g = _dynamicGroups[i];
                if (g != null && _registeredGroupsSeen.Add(g))
                {
                    _registeredGroupsScratch.Add(g);
                }
            }

            _registeredGroupsSeen.Clear();
            return _registeredGroupsScratch;
        }

        public static void EnsureInitialized()
        {
            if (_initialized)
                return;

            _initialized = true;

#if UNITY_EDITOR
            if (_registry == null)
            {
                _registry = FindRegistryInEditor();
            }
#endif

            if (_registry != null)
            {
                HydrateFromGroups(_registry.Groups);
            }
#if UNITY_EDITOR
            else
            {
                IReadOnlyList<EventBoardGroup> groups = EventBoardGroup.GetAllGroups();
                if (groups != null && groups.Count > 0)
                {
                    HydrateFromGroups(groups);
                }
            }
#endif

            if (_dynamicGroups.Count > 0)
            {
                HydrateFromGroups(_dynamicGroups);
            }
        }

#if UNITY_EDITOR
        private static EventBoardRegistry FindRegistryInEditor()
        {
            UnityEngine.Object[] preloaded = UnityEditor.PlayerSettings.GetPreloadedAssets();
            if (preloaded != null)
            {
                for (int i = 0; i < preloaded.Length; i++)
                {
                    if (preloaded[i] is EventBoardRegistry reg && reg != null)
                        return reg;
                }
            }

            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:EventBoardRegistry");
            if (guids != null)
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[i]);
                    EventBoardRegistry reg = UnityEditor.AssetDatabase.LoadAssetAtPath<EventBoardRegistry>(path);
                    if (reg != null)
                        return reg;
                }
            }

            return null;
        }
#endif

        /// <summary>
        /// Hydrates default values and saveability settings from multiple EventBoardGroup SOs into their generic channels without boxing.
        /// </summary>
        public static void HydrateFromGroups(IReadOnlyList<EventBoardGroup> groups)
        {
            if (groups == null)
                return;

            for (int i = 0; i < groups.Count; i++)
            {
                HydrateFromGroup(groups[i]);
            }
        }

        /// <summary>
        /// Hydrates all channels from a single EventBoardGroup SO into the EventBoard engine.
        /// </summary>
        public static void HydrateFromGroup(EventBoardGroup group)
        {
            if (group == null)
                return;

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

                if (_idToGuid.TryGetValue(runtimeId, out string existingGuid) &&
                    !string.IsNullOrEmpty(existingGuid) &&
                    !string.Equals(existingGuid, entry.guid, StringComparison.Ordinal))
                {
                    Debug.LogError($"[EventBoard] 32-bit FNV-1a Hash collision detected! ID: {runtimeId} -> Existing GUID: '{existingGuid}', New GUID: '{entry.guid}' ({fullPath}). Please recreate the conflicting event.", group);
                    continue;
                }

                _idToGuid[runtimeId] = entry.guid;
                _idToPath[runtimeId] = fullPath;

                if (entry.payload != null)
                {
                    entry.payload.ConfigureChannel(runtimeId, entry.guid, fullPath, entry.isSaveable);
                }
            }
        }

        internal static void ConfigureChannel<T>(
            int runtimeId,
            string guid,
            string eventPath,
            T defaultValue,
            bool hasDefaultValue,
            bool isSaveable)
        {
            if (runtimeId == 0)
                return;

            if (_diagnosticSlots.TryGetValue(runtimeId, out IEventChannelDiagnostic existing) &&
                existing != null &&
                existing.PayloadType != typeof(T))
            {
                if (existing is IEventChannelInternal internalExisting)
                {
                    internalExisting.RemoveSelfFromBank();
                }
                _diagnosticSlots.Remove(runtimeId);
                _version++;
            }

            _idToGuid[runtimeId] = guid ?? string.Empty;
            _idToPath[runtimeId] = eventPath ?? string.Empty;

            EventChannelSlot<T> slot = ChannelBank<T>.GetOrCreate(runtimeId, eventPath);
            slot.ConfigureMetadata(guid, eventPath, defaultValue, hasDefaultValue, isSaveable);
        }

        private static void RegisterBankReset(Action resetAction)
        {
            if (resetAction != null && !_bankResetActions.Contains(resetAction))
            {
                _bankResetActions.Add(resetAction);
            }
        }

        /// <summary>
        /// Computes a deterministic 32-bit FNV-1a hash with zero GC allocation.
        /// Never returns 0 for non-empty strings.
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
        /// Writes the payload to the Blackboard and dispatches it to all subscribers with zero GC allocation.
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
        /// Publishes the payload only if it differs from the current Blackboard value (spam protection).
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
        /// Updates the Blackboard state silently (or optionally notifies subscribers).
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
        /// Updates the Blackboard state only if the value differs from the current value.
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
        /// Reads the latest Blackboard value (or default value) with zero GC allocation.
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
        /// Returns the current Blackboard value, or the provided fallback if unset.
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
        /// Returns whether this channel currently has a valid runtime or default value.
        /// </summary>
        public static bool HasValue<T>(int runtimeId)
        {
            if (runtimeId == 0)
                return false;

            EnsureInitialized();
            if (ChannelBank<T>.Slots.TryGetValue(runtimeId, out EventChannelSlot<T> slot))
            {
                return slot.HasValue;
            }

            return false;
        }

        /// <summary>
        /// Subscribes to a channel. If <paramref name="replayLatest"/> is true, invokes the listener immediately with the current value.
        /// </summary>
        public static EventSubscription Subscribe<T>(int runtimeId, Action<T> listener, bool replayLatest = false, string fallbackPath = null)
        {
            if (runtimeId == 0 || listener == null)
                return EventSubscription.Empty;

            EnsureInitialized();
            EventChannelSlot<T> slot = ChannelBank<T>.GetOrCreate(runtimeId, fallbackPath);
            return slot.Subscribe(listener, null, hasExplicitOwner: false, replayLatest);
        }

        /// <summary>
        /// Subscribes with an explicit owner object; automatically cleans up when the owner is destroyed, even for lambdas/closures.
        /// </summary>
        public static EventSubscription Subscribe<T>(int runtimeId, UnityEngine.Object owner, Action<T> listener, bool replayLatest = false, string fallbackPath = null)
        {
            if (runtimeId == 0 || listener == null || owner == null)
                return EventSubscription.Empty;

            EnsureInitialized();
            EventChannelSlot<T> slot = ChannelBank<T>.GetOrCreate(runtimeId, fallbackPath);
            return slot.Subscribe(listener, owner, hasExplicitOwner: true, replayLatest);
        }

        /// <summary>
        /// Subscribes a parameterless callback without closure allocations.
        /// </summary>
        public static EventSubscription Subscribe<T>(int runtimeId, Action listener, bool replayLatest = false, string fallbackPath = null)
        {
            if (runtimeId == 0 || listener == null)
                return EventSubscription.Empty;

            EnsureInitialized();
            EventChannelSlot<T> slot = ChannelBank<T>.GetOrCreate(runtimeId, fallbackPath);
            return slot.Subscribe(listener, null, hasExplicitOwner: false, replayLatest);
        }

        /// <summary>
        /// Subscribes a parameterless callback with an explicit owner object for automatic cleanup on destruction.
        /// </summary>
        public static EventSubscription Subscribe<T>(int runtimeId, UnityEngine.Object owner, Action listener, bool replayLatest = false, string fallbackPath = null)
        {
            if (runtimeId == 0 || listener == null || owner == null)
                return EventSubscription.Empty;

            EnsureInitialized();
            EventChannelSlot<T> slot = ChannelBank<T>.GetOrCreate(runtimeId, fallbackPath);
            return slot.Subscribe(listener, owner, hasExplicitOwner: true, replayLatest);
        }

        /// <summary>
        /// Unsubscribes a typed listener from the channel.
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
        /// Unsubscribes a parameterless listener from the channel.
        /// </summary>
        public static void Unsubscribe<T>(int runtimeId, Action listener)
        {
            if (runtimeId == 0 || listener == null)
                return;

            if (ChannelBank<T>.Slots.TryGetValue(runtimeId, out EventChannelSlot<T> slot))
            {
                slot.Unsubscribe(listener);
            }
        }

        /// <summary>
        /// Clears the Blackboard state of a specific channel.
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

            if (ChannelBank<T>.Slots.TryGetValue(runtimeId, out EventChannelSlot<T> slot))
            {
                ChannelBank<T>.Slots.Remove(runtimeId);
                slot.ResetChannelCompletely();
                _diagnosticSlots.Remove(runtimeId);
                _idToPath.Remove(runtimeId);
                _idToGuid.Remove(runtimeId);
                _version++;
            }
        }

        public static void RemoveChannelById(int runtimeId)
        {
            if (runtimeId == 0)
                return;

            if (_diagnosticSlots.TryGetValue(runtimeId, out IEventChannelDiagnostic slot))
            {
                if (slot is IEventChannelInternal internalSlot)
                {
                    internalSlot.RemoveSelfFromBank();
                }
                _diagnosticSlots.Remove(runtimeId);
                _idToPath.Remove(runtimeId);
                _idToGuid.Remove(runtimeId);
                _version++;
            }
        }

        /// <summary>
        /// Clears runtime Blackboard values for all non-saveable channels (e.g., on scene transitions).
        /// </summary>
        public static void ClearNonPersistent(bool restoreDefaults = true)
        {
            foreach (var kvp in _diagnosticSlots)
            {
                IEventChannelDiagnostic slot = kvp.Value;
                if (slot != null && !slot.IsSaveable)
                {
                    slot.ClearState(restoreDefaults);
                }
            }
        }

        /// <summary>
        /// Clears runtime Blackboard values for all channels while keeping existing subscriptions intact.
        /// </summary>
        public static void ClearAllValues(bool restoreDefaults = true)
        {
            foreach (var kvp in _diagnosticSlots)
            {
                kvp.Value?.ClearState(restoreDefaults);
            }
        }

        /// <summary>
        /// Resets all subscriptions and temporary channel states (for startup and tests).
        /// </summary>
        public static void ResetAll(bool clearDynamicGroups = false)
        {
            foreach (var kvp in _diagnosticSlots)
            {
                kvp.Value?.ResetChannelCompletely();
            }

            for (int i = 0; i < _bankResetActions.Count; i++)
            {
                _bankResetActions[i]?.Invoke();
            }

            _diagnosticSlots.Clear();
            _idToPath.Clear();
            _idToGuid.Clear();
            if (clearDynamicGroups)
            {
                _dynamicGroups.Clear();
            }
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

        [Serializable]
        public sealed class BlackboardSnapshot
        {
            public List<Entry> entries = new List<Entry>();

            [Serializable]
            public sealed class Entry
            {
                public string guid;
                public string path;
                public string json;
            }
        }

        /// <summary>
        /// Exports only channels marked as IsSaveable to a clean JSON snapshot string.
        /// Useful for integrating with any save/load or persistence system.
        /// </summary>
        public static string ExportStateJson()
        {
            EnsureInitialized();
            BlackboardSnapshot snapshot = new BlackboardSnapshot();

            foreach (var kvp in _diagnosticSlots)
            {
                IEventChannelDiagnostic slot = kvp.Value;
                if (slot == null || !slot.IsSaveable)
                    continue;

                string valJson = slot.ExportValueJson();
                if (valJson == null)
                    continue;

                snapshot.entries.Add(new BlackboardSnapshot.Entry
                {
                    guid = slot.Guid,
                    path = slot.Path,
                    json = valJson
                });
            }

            return JsonUtility.ToJson(snapshot, prettyPrint: true);
        }

        /// <summary>
        /// Restores state from an ExportStateJson snapshot.
        /// Missing or corrupt channels automatically fall back to ScriptableObject DefaultValues without errors.
        /// </summary>
        /// <param name="json">BlackboardSnapshot JSON string.</param>
        /// <param name="notifyListeners">Whether to trigger callbacks for restored values.</param>
        /// <param name="resetMissingToDefault">Whether missing or invalid saveable channels should restore their default value.</param>
        public static bool ImportStateJson(string json, bool notifyListeners = false, bool resetMissingToDefault = true)
        {
            if (string.IsNullOrWhiteSpace(json))
                return false;

            EnsureInitialized();
            try
            {
                BlackboardSnapshot snapshot = JsonUtility.FromJson<BlackboardSnapshot>(json);
                if (snapshot == null || snapshot.entries == null)
                    return false;

                Dictionary<string, string> byGuid = new Dictionary<string, string>(StringComparer.Ordinal);
                Dictionary<string, string> byPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < snapshot.entries.Count; i++)
                {
                    var e = snapshot.entries[i];
                    if (!string.IsNullOrEmpty(e.guid))
                        byGuid[e.guid] = e.json;
                    if (!string.IsNullOrEmpty(e.path))
                        byPath[e.path] = e.json;
                }

                foreach (var kvp in _diagnosticSlots)
                {
                    IEventChannelDiagnostic slot = kvp.Value;
                    if (slot == null || !slot.IsSaveable)
                        continue;

                    string entryJson = null;
                    if (!string.IsNullOrEmpty(slot.Guid) && byGuid.TryGetValue(slot.Guid, out string gj))
                    {
                        entryJson = gj;
                    }
                    else if (!string.IsNullOrEmpty(slot.Path) && byPath.TryGetValue(slot.Path, out string pj))
                    {
                        entryJson = pj;
                    }

                    bool restored = false;
                    if (entryJson != null)
                    {
                        restored = slot.TryImportValueJson(entryJson, notifyListeners);
                    }

                    if (!restored && resetMissingToDefault)
                    {
                        slot.RestoreDefaultState(notifyListeners);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[EventBoard] ImportStateJson failed: {ex.Message}. Falling back to default values.");
                return false;
            }
        }
    }
}
