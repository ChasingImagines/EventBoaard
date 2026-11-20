using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Yasin.EventBoard
{
    /// <summary>
    /// ScriptableObject holding a categorized list of EventBoard channels.
    /// Can be placed in any folder under Assets/; automatically synchronized with EventBoardRegistry by the Editor.
    /// </summary>
    [CreateAssetMenu(fileName = "NewEventGroup", menuName = "EventBoard/Event Group (SO)", order = 10)]
    public sealed class EventBoardGroup : ScriptableObject
    {
        [Serializable]
        public sealed class EventEntry
        {
            [Tooltip("Immutable unique identifier (GUID). Scene and prefab references stay intact even if the event is renamed or moved to another group.")]
            public string guid;

            [FormerlySerializedAs("eventPath")]
            [Tooltip("Event name within this group (e.g., IsPlacementActive, OnObjectPlaced).")]
            public string eventName = "NewEvent";

            [SerializeReference]
            public EventPayloadDefinition payload = new VoidPayload();

            [FormerlySerializedAs("isPersistent")]
            [Tooltip("When enabled, this channel's Blackboard value is included in EventBoard.ExportStateJson() snapshots.")]
            public bool isSaveable;

            [Tooltip("Developer note describing when this channel is triggered and what it represents.")]
            public string description = string.Empty;

            public int RuntimeId => EventBoard.ComputeId(guid);

            public Type PayloadType => payload != null ? payload.PayloadType : typeof(VoidSignal);

            public string PayloadDisplayName => payload != null ? payload.DisplayName : "Undefined";
        }

        [SerializeField]
        private List<EventEntry> events = new List<EventEntry>();

        private readonly Dictionary<string, EventEntry> _byGuid = new Dictionary<string, EventEntry>(StringComparer.Ordinal);
        private bool _lookupBuilt;

#if UNITY_EDITOR
        private static readonly List<EventBoardGroup> _cachedGroups = new List<EventBoardGroup>();
        private static bool _groupsLoaded;
#endif

        public string GroupName => name;

        public IReadOnlyList<EventEntry> Events => events;

        public string GetFullChannelName(EventEntry entry)
        {
            if (entry == null)
                return GroupName;
            return $"{GroupName}/{entry.eventName}";
        }

        internal static void ResetStaticState()
        {
#if UNITY_EDITOR
            _cachedGroups.Clear();
            _groupsLoaded = false;
#endif
        }

        /// <summary>
        /// Returns all EventBoardGroup SOs in the project (from AssetDatabase in Editor, or from EventBoardRegistry and dynamic registrations in Player Builds).
        /// </summary>
        public static IReadOnlyList<EventBoardGroup> GetAllGroups(bool forceRefresh = false)
        {
#if UNITY_EDITOR
            if (_groupsLoaded && !forceRefresh)
            {
                bool hasNull = false;
                for (int i = 0; i < _cachedGroups.Count; i++)
                {
                    if (_cachedGroups[i] == null)
                    {
                        hasNull = true;
                        break;
                    }
                }
                if (!hasNull)
                    return _cachedGroups;
            }

            _cachedGroups.Clear();
            HashSet<EventBoardGroup> uniqueSet = new HashSet<EventBoardGroup>();

            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:EventBoardGroup");
            if (guids != null)
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    string assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[i]);
                    EventBoardGroup g = UnityEditor.AssetDatabase.LoadAssetAtPath<EventBoardGroup>(assetPath);
                    if (g != null && uniqueSet.Add(g))
                    {
                        g.EnsureLookups();
                        _cachedGroups.Add(g);
                    }
                }
            }

            _cachedGroups.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            _groupsLoaded = true;
            return _cachedGroups;
#else
            return EventBoard.GetRegisteredGroups();
#endif
        }

        public static void InvalidateGroupCache()
        {
#if UNITY_EDITOR
            _groupsLoaded = false;
#endif
        }

        /// <summary>
        /// Finds the event entry and owning EventBoardGroup SO for the given GUID across all project groups.
        /// </summary>
        public static bool TryFindByGuid(string guid, out EventBoardGroup ownerGroup, out EventEntry entry)
        {
            ownerGroup = null;
            entry = null;

            if (string.IsNullOrEmpty(guid))
                return false;

            IReadOnlyList<EventBoardGroup> groups = GetAllGroups();
            for (int i = 0; i < groups.Count; i++)
            {
                EventBoardGroup g = groups[i];
                if (g != null && g.TryGetByGuid(guid, out entry))
                {
                    ownerGroup = g;
                    return true;
                }
            }

            return false;
        }

        private void OnEnable()
        {
            RebuildLookups();
            InvalidateGroupCache();
        }

        private void OnValidate()
        {
            SanitizeEntries();
            RebuildLookups();
            InvalidateGroupCache();
            EventBoard.HydrateFromGroup(this);
        }

        public void SanitizeEntries()
        {
            if (events == null)
                events = new List<EventEntry>();

            HashSet<string> seenGuids = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < events.Count; i++)
            {
                EventEntry entry = events[i];
                if (entry == null)
                {
                    entry = new EventEntry();
                    events[i] = entry;
                }

                // Deduplicate empty or intra-group duplicated GUIDs
                if (string.IsNullOrWhiteSpace(entry.guid) || !seenGuids.Add(entry.guid))
                {
                    entry.guid = Guid.NewGuid().ToString("N");
                    seenGuids.Add(entry.guid);
                }

                if (string.IsNullOrWhiteSpace(entry.eventName))
                {
                    entry.eventName = $"Event_{i}";
                }
                else
                {
                    entry.eventName = CleanEventName(entry.eventName);
                }

                if (entry.payload == null)
                {
                    entry.payload = new VoidPayload();
                }

                if (entry.isSaveable && !entry.payload.SupportsSaveable)
                {
                    entry.isSaveable = false;
                }
            }
        }

        public static string CleanEventName(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName))
                return "NewEvent";

            string cleaned = rawName.Trim();
            int lastSlash = cleaned.LastIndexOf('/');
            if (lastSlash >= 0 && lastSlash < cleaned.Length - 1)
            {
                cleaned = cleaned.Substring(lastSlash + 1).Trim();
            }
            return string.IsNullOrEmpty(cleaned) ? "NewEvent" : cleaned;
        }

        public void EnsureLookups()
        {
            if (!_lookupBuilt)
            {
                RebuildLookups();
            }
        }

        public void RebuildLookups()
        {
            _byGuid.Clear();

            if (events != null)
            {
                for (int i = 0; i < events.Count; i++)
                {
                    EventEntry entry = events[i];
                    if (entry != null && !string.IsNullOrEmpty(entry.guid))
                    {
                        _byGuid[entry.guid] = entry;
                    }
                }
            }

            _lookupBuilt = true;
        }

        public bool TryGetByGuid(string guid, out EventEntry entry)
        {
            EnsureLookups();
            if (string.IsNullOrEmpty(guid))
            {
                entry = null;
                return false;
            }
            return _byGuid.TryGetValue(guid, out entry);
        }

        public EventEntry AddEntry(
            string eventName,
            EventPayloadDefinition payload,
            bool isSaveable = false,
            string description = "")
        {
            string cleanName = CleanEventName(eventName);
            EventPayloadDefinition resolvedPayload = payload ?? new VoidPayload();

            EventEntry created = new EventEntry
            {
                guid = Guid.NewGuid().ToString("N"),
                eventName = cleanName,
                payload = resolvedPayload,
                isSaveable = isSaveable && resolvedPayload.SupportsSaveable,
                description = description ?? string.Empty
            };

            if (events == null)
                events = new List<EventEntry>();

            events.Add(created);
            RebuildLookups();
            EventBoard.HydrateFromGroup(this);
            return created;
        }

        public bool RemoveByGuid(string guid)
        {
            if (string.IsNullOrEmpty(guid) || events == null)
                return false;

            for (int i = events.Count - 1; i >= 0; i--)
            {
                if (events[i] != null && string.Equals(events[i].guid, guid, StringComparison.Ordinal))
                {
                    int removedId = events[i].RuntimeId;
                    events.RemoveAt(i);
                    RebuildLookups();
                    EventBoard.RemoveChannelById(removedId);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Attempts to find and instantiate an EventPayloadDefinition matching <paramref name="targetType"/>.
        /// Returns false instead of silently falling back to VoidPayload when none is defined.
        /// </summary>
        public static bool TryCreatePayloadForType(Type targetType, out EventPayloadDefinition payload)
        {
            if (targetType == null || targetType == typeof(VoidSignal))
            {
                payload = new VoidPayload();
                return true;
            }
            if (targetType == typeof(int))
            {
                payload = new IntPayload();
                return true;
            }
            if (targetType == typeof(float))
            {
                payload = new FloatPayload();
                return true;
            }
            if (targetType == typeof(bool))
            {
                payload = new BoolPayload();
                return true;
            }
            if (targetType == typeof(string))
            {
                payload = new StringPayload();
                return true;
            }
            if (targetType == typeof(Vector2))
            {
                payload = new Vector2Payload();
                return true;
            }
            if (targetType == typeof(Vector3))
            {
                payload = new Vector3Payload();
                return true;
            }
            if (targetType == typeof(Color))
            {
                payload = new ColorPayload();
                return true;
            }
            if (targetType == typeof(GameObject))
            {
                payload = new GameObjectPayload();
                return true;
            }
            if (targetType == typeof(Transform))
            {
                payload = new TransformPayload();
                return true;
            }

            Type genericBase = typeof(EventPayloadDefinition<>).MakeGenericType(targetType);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch
                {
                    continue;
                }

                for (int i = 0; i < types.Length; i++)
                {
                    Type t = types[i];
                    if (!t.IsAbstract && genericBase.IsAssignableFrom(t))
                    {
                        try
                        {
                            payload = (EventPayloadDefinition)Activator.CreateInstance(t);
                            if (payload != null)
                                return true;
                        }
                        catch
                        {
                            // Skip types without a parameterless constructor
                        }
                    }
                }
            }

            payload = null;
            return false;
        }
    }
}
