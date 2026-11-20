using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Yasin.EventBoard.Editor
{
    /// <summary>
    /// 4-tab engineering dashboard and diagnostic window for EventBoard:
    /// 1) Live Blackboard Monitor &amp; Test Trigger (grouped by SO)
    /// 2) Scene &amp; Project Connection Matrix
    /// 3) Static Project Audit
    /// 4) Zero-GC Performance Benchmark
    /// </summary>
    public sealed class EventBoardWindow : EditorWindow
    {
        private sealed class SceneKeyUsage
        {
            public UnityEngine.Object Context;
            public string FieldName;
            public string Guid;
            public string Path;
            public Type ValueType;
            public Type ExpectedPayloadType;
            public bool IsAssigned;
            public bool IsMissingInGroups;
            public bool IsTypeMismatch;
        }

        private int _selectedTab;
        private readonly string[] _tabLabels =
        {
            "📡 Live",
            "🕸️ Links",
            "🛡️ Audit",
            "⚡ Benchmark"
        };

        private string _searchFilter = string.Empty;
        private string _newGroupAssetName = "NewEvents";
        private Vector2 _scrollPos;
        private int _benchmarkIterations = 100000;
        private string _lastBenchmarkReport = string.Empty;
        private bool _includeProjectAssets;

        // Live Test Trigger state
        private string _selectedChannelGuid = string.Empty;
        private int _testInt;
        private float _testFloat;
        private bool _testBool;
        private string _testString = "Test";
        private Vector2 _testVector2;
        private Vector3 _testVector3;
        private Color _testColor = Color.cyan;

        // Scene & Audit cache
        private readonly List<SceneKeyUsage> _sceneUsages = new List<SceneKeyUsage>();
        private readonly List<SceneKeyUsage> _entryUsageBuffer = new List<SceneKeyUsage>(8);
        private readonly List<string> _orphanChannelPaths = new List<string>();
        private readonly List<Delegate> _subscriberBuffer = new List<Delegate>(8);
        private bool _matrixBuilt;

        [MenuItem("Tools/EventBoard/Hub", priority = 100)]
        public static void OpenWindow()
        {
            EventBoardWindow window = GetWindow<EventBoardWindow>("EventBoard Hub");
            window.minSize = new Vector2(720f, 440f);
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            RefreshSceneMatrix();
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            Repaint();
        }

        private void OnInspectorUpdate()
        {
            if (Application.isPlaying && _selectedTab == 0)
            {
                Repaint();
            }
        }

        private void OnGUI()
        {
            IReadOnlyList<EventBoardGroup> groups = EventBoardGroup.GetAllGroups();
            DrawTopToolbar();

            if (groups == null || groups.Count == 0)
            {
                EditorGUILayout.Space(12f);
                EditorGUILayout.HelpBox("No EventBoardGroup SO assets found in the project yet.", MessageType.Warning);
                using (new EditorGUILayout.HorizontalScope())
                {
                    _newGroupAssetName = EditorGUILayout.TextField("New Group SO", _newGroupAssetName);
                    if (GUILayout.Button("+ Create", GUILayout.Height(24f), GUILayout.Width(100f)))
                    {
                        EventBoardRegistryUtility.CreateGroupAsset(_newGroupAssetName, ping: true);
                    }
                }
                return;
            }

            EditorGUILayout.Space(4f);
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            switch (_selectedTab)
            {
                case 0:
                    DrawLiveBlackboardTab(groups);
                    break;
                case 1:
                    DrawSceneMatrixTab(groups);
                    break;
                case 2:
                    DrawStaticAuditTab();
                    break;
                case 3:
                    DrawBenchmarkTab();
                    break;
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawTopToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                int newTab = GUILayout.Toolbar(_selectedTab, _tabLabels, EditorStyles.toolbarButton, GUILayout.Width(390f));
                if (newTab != _selectedTab)
                {
                    _selectedTab = newTab;
                    if (_selectedTab == 1 || _selectedTab == 2)
                    {
                        RefreshSceneMatrix();
                    }
                }

                GUILayout.FlexibleSpace();

                _searchFilter = GUILayout.TextField(_searchFilter, EditorStyles.toolbarSearchField, GUILayout.Width(170f));
                if (!string.IsNullOrEmpty(_searchFilter) && GUILayout.Button("✕", EditorStyles.toolbarButton, GUILayout.Width(22f)))
                {
                    _searchFilter = string.Empty;
                    GUI.FocusControl(null);
                }

                if (GUILayout.Button("⚙️ Registry", EditorStyles.toolbarButton, GUILayout.Width(76f)))
                {
                    EventBoardRegistry reg = EventBoardRegistryUtility.EnsureRegistryExists();
                    if (reg != null)
                    {
                        Selection.activeObject = reg;
                        EditorGUIUtility.PingObject(reg);
                    }
                }

                if (GUILayout.Button("🔄 Refresh", EditorStyles.toolbarButton, GUILayout.Width(72f)))
                {
                    EventBoardGroup.InvalidateGroupCache();
                    EventBoardRegistryUtility.SyncAllGroups(createRegistryIfGroupsExist: true, saveAssets: false);
                    RefreshSceneMatrix();
                }
            }
        }

        private void DrawLiveBlackboardTab(IReadOnlyList<EventBoardGroup> groups)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                string modeStatus = Application.isPlaying ? "🟢 PLAY MODE" : "⚪ EDIT MODE";
                EditorGUILayout.LabelField(modeStatus, EditorStyles.boldLabel);

                if (Application.isPlaying)
                {
                    if (GUILayout.Button("Reset Values", EditorStyles.miniButton, GUILayout.Width(100f)))
                    {
                        EventBoard.ClearAllValues(restoreDefaults: true);
                    }

                    GUI.backgroundColor = new Color(1f, 0.55f, 0.55f);
                    if (GUILayout.Button("Full Reset", EditorStyles.miniButton, GUILayout.Width(85f)))
                    {
                        if (EditorUtility.DisplayDialog(
                                "EventBoard Full Reset",
                                "All runtime Blackboard values AND active subscriptions will be cleared. Continue?",
                                "Yes, Reset",
                                "Cancel"))
                        {
                            EventBoard.ResetAll();
                        }
                    }
                    GUI.backgroundColor = Color.white;
                }
            }

            for (int g = 0; g < groups.Count; g++)
            {
                EventBoardGroup group = groups[g];
                if (group == null)
                    continue;

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField($"📦 {group.GroupName} ({group.Events.Count})", EditorStyles.boldLabel);
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("🎯 SO", EditorStyles.miniButton, GUILayout.Width(55f)))
                        {
                            Selection.activeObject = group;
                            EditorGUIUtility.PingObject(group);
                        }
                    }

                    IReadOnlyList<EventBoardGroup.EventEntry> entries = group.Events;
                    if (entries == null || entries.Count == 0)
                    {
                        EditorGUILayout.LabelField("   (Empty)", EditorStyles.miniLabel);
                        continue;
                    }

                    for (int i = 0; i < entries.Count; i++)
                    {
                        EventBoardGroup.EventEntry entry = entries[i];
                        if (entry == null)
                            continue;

                        string fullChannelName = group.GetFullChannelName(entry);
                        if (!MatchesFilter(fullChannelName, entry.description))
                            continue;

                        EventBoard.TryGetDiagnosticSlot(entry.RuntimeId, out IEventChannelDiagnostic liveSlot);
                        bool isSelected = string.Equals(_selectedChannelGuid, entry.guid, StringComparison.Ordinal);

                        using (new EditorGUILayout.VerticalScope(GUI.skin.box))
                        {
                            using (new EditorGUILayout.HorizontalScope())
                            {
                                string persistBadge = entry.isSaveable ? " 💾" : string.Empty;
                                string title = $"{entry.eventName}{persistBadge}";
                                if (GUILayout.Button(title, EditorStyles.boldLabel))
                                {
                                    _selectedChannelGuid = isSelected ? string.Empty : entry.guid;
                                }

                                GUILayout.FlexibleSpace();

                                string typeBadge = $"[{EventKeyDrawer.GetFriendlyTypeName(entry.PayloadType)}]";
                                EditorGUILayout.LabelField(typeBadge, EditorStyles.miniLabel, GUILayout.Width(80f));

                                string valText = Application.isPlaying && liveSlot != null
                                    ? $"Value: {liveSlot.GetFormattedValue()}"
                                    : $"Default: {(entry.payload != null ? entry.payload.GetDefaultValueSummary() : "—")}";

                                EditorGUILayout.LabelField(valText, GUILayout.Width(165f));

                                int subCount = liveSlot != null ? liveSlot.SubscriberCount : 0;
                                int pubCount = liveSlot != null ? liveSlot.PublishCount : 0;
                                EditorGUILayout.LabelField($"Subs: {subCount}  |  Pubs: {pubCount}", EditorStyles.miniLabel, GUILayout.Width(120f));

                                if (GUILayout.Button(isSelected ? "▲" : "▼ Test", EditorStyles.miniButton, GUILayout.Width(60f)))
                                {
                                    _selectedChannelGuid = isSelected ? string.Empty : entry.guid;
                                }
                            }

                            if (isSelected)
                            {
                                DrawExpandedChannelDetails(group, entry, liveSlot);
                            }
                        }
                    }
                }

                EditorGUILayout.Space(4f);
            }
        }

        private void DrawExpandedChannelDetails(EventBoardGroup group, EventBoardGroup.EventEntry entry, IEventChannelDiagnostic liveSlot)
        {
            EditorGUILayout.Space(4f);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"GUID: {entry.guid}   |   ID: {entry.RuntimeId}", EditorStyles.miniLabel);
                if (!string.IsNullOrEmpty(entry.description))
                {
                    EditorGUILayout.LabelField($"Note: {entry.description}", EditorStyles.wordWrappedMiniLabel);
                }

                // Live Subscriber List
                if (Application.isPlaying && liveSlot != null && liveSlot.SubscriberCount > 0)
                {
                    EditorGUILayout.Space(2f);
                    EditorGUILayout.LabelField("Subscribers:", EditorStyles.boldLabel);
                    liveSlot.CopySubscribersTo(_subscriberBuffer);

                    for (int i = 0; i < _subscriberBuffer.Count; i++)
                    {
                        Delegate del = _subscriberBuffer[i];
                        if (del == null)
                            continue;

                        using (new EditorGUILayout.HorizontalScope())
                        {
                            string targetName = del.Target != null ? del.Target.ToString() : "Static";
                            EditorGUILayout.LabelField($"• {targetName} -> {del.Method.Name}()", EditorStyles.miniLabel);

                            if (del.Target is UnityEngine.Object unityObj && unityObj != null)
                            {
                                if (GUILayout.Button("🎯 Select", EditorStyles.miniButton, GUILayout.Width(65f)))
                                {
                                    EditorGUIUtility.PingObject(unityObj);
                                    Selection.activeObject = unityObj;
                                }
                            }
                        }
                    }
                }

                // Play Mode Live Test Trigger
                if (Application.isPlaying)
                {
                    EditorGUILayout.Space(4f);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("⚡ Test:", EditorStyles.boldLabel, GUILayout.Width(60f));
                        object payloadToFire = DrawTestPayloadField(entry.PayloadType);

                        GUI.backgroundColor = new Color(0.35f, 0.9f, 0.5f);
                        if (GUILayout.Button("⚡ Publish", GUILayout.Width(85f)))
                        {
                            TriggerEditorTestPublish(group, entry, liveSlot, payloadToFire);
                        }
                        GUI.backgroundColor = Color.white;
                    }
                }
            }
        }

        private object DrawTestPayloadField(Type type)
        {
            if (type == typeof(VoidSignal))
            {
                EditorGUILayout.LabelField("void", EditorStyles.miniLabel);
                return VoidSignal.Default;
            }
            if (type == typeof(int))
            {
                _testInt = EditorGUILayout.IntField(_testInt);
                return _testInt;
            }
            if (type == typeof(float))
            {
                _testFloat = EditorGUILayout.FloatField(_testFloat);
                return _testFloat;
            }
            if (type == typeof(bool))
            {
                _testBool = EditorGUILayout.Toggle(_testBool);
                return _testBool;
            }
            if (type == typeof(string))
            {
                _testString = EditorGUILayout.TextField(_testString);
                return _testString;
            }
            if (type == typeof(Vector2))
            {
                _testVector2 = EditorGUILayout.Vector2Field(GUIContent.none, _testVector2);
                return _testVector2;
            }
            if (type == typeof(Vector3))
            {
                _testVector3 = EditorGUILayout.Vector3Field(GUIContent.none, _testVector3);
                return _testVector3;
            }
            if (type == typeof(Color))
            {
                _testColor = EditorGUILayout.ColorField(_testColor);
                return _testColor;
            }

            EditorGUILayout.LabelField($"(Default value will be sent for type '{type.Name}')", EditorStyles.miniLabel);
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }

        private static void TriggerEditorTestPublish(EventBoardGroup group, EventBoardGroup.EventEntry entry, IEventChannelDiagnostic liveSlot, object boxedPayload)
        {
            if (liveSlot != null && liveSlot.TryPublishFromEditor(boxedPayload))
                return;

            if (entry.payload != null)
            {
                entry.payload.ConfigureChannel(entry.RuntimeId, entry.guid, group.GetFullChannelName(entry), entry.isSaveable);
                if (EventBoard.TryGetDiagnosticSlot(entry.RuntimeId, out IEventChannelDiagnostic createdSlot))
                {
                    createdSlot.TryPublishFromEditor(boxedPayload);
                }
            }
        }

        private void DrawSceneMatrixTab(IReadOnlyList<EventBoardGroup> groups)
        {
            if (!_matrixBuilt)
            {
                RefreshSceneMatrix();
            }

            DrawAssetScanToggle();

            EditorGUILayout.HelpBox(
                $"Found {_sceneUsages.Count} EventKey<T> references in the scanned scope.",
                MessageType.Info);

            for (int g = 0; g < groups.Count; g++)
            {
                EventBoardGroup group = groups[g];
                if (group == null || group.Events == null)
                    continue;

                IReadOnlyList<EventBoardGroup.EventEntry> entries = group.Events;
                for (int i = 0; i < entries.Count; i++)
                {
                    EventBoardGroup.EventEntry entry = entries[i];
                    if (entry == null)
                        continue;

                    string fullName = group.GetFullChannelName(entry);
                    if (!MatchesFilter(fullName, entry.description))
                        continue;

                    _entryUsageBuffer.Clear();
                    for (int u = 0; u < _sceneUsages.Count; u++)
                    {
                        SceneKeyUsage usage = _sceneUsages[u];
                        if (string.Equals(usage.Guid, entry.guid, StringComparison.Ordinal))
                        {
                            _entryUsageBuffer.Add(usage);
                        }
                    }

                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.LabelField(
                            $"📦 {fullName}   ({EventKeyDrawer.GetFriendlyTypeName(entry.PayloadType)})   —   Links: {_entryUsageBuffer.Count}",
                            EditorStyles.boldLabel);

                        if (_entryUsageBuffer.Count == 0)
                        {
                            EditorGUILayout.LabelField("   No references bound in this scope.", EditorStyles.miniLabel);
                        }
                        else
                        {
                            for (int k = 0; k < _entryUsageBuffer.Count; k++)
                            {
                                SceneKeyUsage usage = _entryUsageBuffer[k];
                                if (usage.Context == null)
                                    continue;

                                using (new EditorGUILayout.HorizontalScope())
                                {
                                    string ownerLabel = GetUsageContextName(usage.Context);
                                    EditorGUILayout.LabelField(
                                        $"   • {ownerLabel}  [{usage.Context.GetType().Name}.{usage.FieldName}]",
                                        EditorStyles.miniLabel);

                                    if (GUILayout.Button("🎯 Select", EditorStyles.miniButton, GUILayout.Width(65f)))
                                    {
                                        SelectUsageContext(usage.Context);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private void DrawStaticAuditTab()
        {
            if (!_matrixBuilt)
            {
                RefreshSceneMatrix();
            }

            // 1) Registry & PreloadedAssets health check
            EventBoardRegistry registry = EventBoardRegistryUtility.FindRegistry();
            if (registry == null)
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.HelpBox(
                        "⚠️ EventBoardRegistry asset has not been created yet. Create it so groups load automatically in Player Builds.",
                        MessageType.Warning);
                    if (GUILayout.Button("⚙️ Create Registry", GUILayout.Width(140f), GUILayout.Height(36f)))
                    {
                        EventBoardRegistryUtility.SyncAllGroups(createRegistryIfGroupsExist: true, saveAssets: true);
                    }
                }
            }
            else if (!EventBoardRegistryUtility.IsInPreloadedAssets(registry))
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.HelpBox(
                        "⚠️ EventBoardRegistry is not in PlayerSettings > Preloaded Assets. Groups may not load in Player Builds.",
                        MessageType.Warning);
                    if (GUILayout.Button("📌 Add Preloaded", GUILayout.Width(140f), GUILayout.Height(36f)))
                    {
                        EventBoardRegistryUtility.EnsurePreloaded(registry);
                    }
                }
            }
            else
            {
                EditorGUILayout.HelpBox("✅ EventBoardRegistry is active and registered in Preloaded Assets (Build Ready).", MessageType.Info);
            }

            DrawAssetScanToggle();

            int unassignedCount = 0;
            int missingCount = 0;
            int mismatchCount = 0;

            for (int i = 0; i < _sceneUsages.Count; i++)
            {
                if (!_sceneUsages[i].IsAssigned) unassignedCount++;
                else if (_sceneUsages[i].IsMissingInGroups) missingCount++;
                else if (_sceneUsages[i].IsTypeMismatch) mismatchCount++;
            }

            if (unassignedCount == 0 && missingCount == 0 && mismatchCount == 0)
            {
                EditorGUILayout.HelpBox("✅ Healthy! No unassigned, missing, or type-mismatched EventKey<T> references found in the scanned scope.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    $"⚠️ Found {unassignedCount} unassigned fields, {missingCount} missing references, and {mismatchCount} type mismatches:",
                    MessageType.Warning);

                for (int i = 0; i < _sceneUsages.Count; i++)
                {
                    SceneKeyUsage usage = _sceneUsages[i];
                    if (usage.Context == null || (usage.IsAssigned && !usage.IsMissingInGroups && !usage.IsTypeMismatch))
                        continue;

                    using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                    {
                        string issue;
                        if (!usage.IsAssigned)
                        {
                            issue = "⚠️ Unassigned Field";
                        }
                        else if (usage.IsMissingInGroups)
                        {
                            issue = $"❌ Deleted Event ({usage.Path})";
                        }
                        else
                        {
                            issue = $"⚠️ Type Mismatch (Field: {EventKeyDrawer.GetFriendlyTypeName(usage.ValueType)} vs Group: {EventKeyDrawer.GetFriendlyTypeName(usage.ExpectedPayloadType)})";
                        }

                        EditorGUILayout.LabelField(
                            $"{issue}: {GetUsageContextName(usage.Context)} -> {usage.Context.GetType().Name}.{usage.FieldName}");

                        if (GUILayout.Button("🎯 Select", GUILayout.Width(70f)))
                        {
                            SelectUsageContext(usage.Context);
                        }
                    }
                }
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField($"👻 Unused Events ({_orphanChannelPaths.Count})", EditorStyles.boldLabel);
            if (_orphanChannelPaths.Count == 0)
            {
                EditorGUILayout.LabelField("All events defined in Group SOs are referenced in the scanned scope.", EditorStyles.miniLabel);
            }
            else
            {
                for (int i = 0; i < _orphanChannelPaths.Count; i++)
                {
                    EditorGUILayout.LabelField($"• {_orphanChannelPaths[i]}", EditorStyles.miniLabel);
                }
            }
        }

        private void DrawAssetScanToggle()
        {
            bool newInclude = EditorGUILayout.ToggleLeft("📦 Also Scan Project Prefabs & ScriptableObjects", _includeProjectAssets);
            if (newInclude != _includeProjectAssets)
            {
                _includeProjectAssets = newInclude;
                RefreshSceneMatrix();
            }
        }

        public void RefreshSceneMatrix()
        {
            _sceneUsages.Clear();
            _orphanChannelPaths.Clear();
            _matrixBuilt = true;

            IReadOnlyList<EventBoardGroup> groups = EventBoardGroup.GetAllGroups(forceRefresh: true);
            HashSet<string> usedGuids = new HashSet<string>(StringComparer.Ordinal);
            HashSet<object> visited = new HashSet<object>();

#if UNITY_2023_1_OR_NEWER
            MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            MonoBehaviour[] behaviours = FindObjectsOfType<MonoBehaviour>(true);
#endif
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour mb = behaviours[i];
                if (mb != null)
                {
                    visited.Clear();
                    InspectObjectFields(mb, mb, mb.GetType(), string.Empty, usedGuids, visited, 0);
                }
            }

            if (_includeProjectAssets)
            {
                ScanProjectAssets(usedGuids, visited);
            }

            if (groups != null)
            {
                for (int g = 0; g < groups.Count; g++)
                {
                    EventBoardGroup group = groups[g];
                    if (group == null || group.Events == null)
                        continue;

                    for (int i = 0; i < group.Events.Count; i++)
                    {
                        EventBoardGroup.EventEntry entry = group.Events[i];
                        if (entry != null && !usedGuids.Contains(entry.guid))
                        {
                            _orphanChannelPaths.Add($"{group.GetFullChannelName(entry)} ({EventKeyDrawer.GetFriendlyTypeName(entry.PayloadType)})");
                        }
                    }
                }
            }
        }

        private void ScanProjectAssets(HashSet<string> usedGuids, HashSet<object> visited)
        {
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
            if (prefabGuids != null)
            {
                for (int i = 0; i < prefabGuids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null)
                        continue;

                    MonoBehaviour[] prefabBehaviours = prefab.GetComponentsInChildren<MonoBehaviour>(true);
                    for (int b = 0; b < prefabBehaviours.Length; b++)
                    {
                        MonoBehaviour mb = prefabBehaviours[b];
                        if (mb != null)
                        {
                            visited.Clear();
                            InspectObjectFields(mb, mb, mb.GetType(), string.Empty, usedGuids, visited, 0);
                        }
                    }
                }
            }

            string[] soGuids = AssetDatabase.FindAssets("t:ScriptableObject", new[] { "Assets" });
            if (soGuids != null)
            {
                for (int i = 0; i < soGuids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(soGuids[i]);
                    ScriptableObject so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                    if (so == null || so is EventBoardGroup || so is EventBoardRegistry)
                        continue;

                    visited.Clear();
                    InspectObjectFields(so, so, so.GetType(), string.Empty, usedGuids, visited, 0);
                }
            }
        }

        private void InspectObjectFields(
            UnityEngine.Object context,
            object instance,
            Type rootType,
            string prefix,
            HashSet<string> usedGuids,
            HashSet<object> visited,
            int depth)
        {
            if (instance == null || rootType == null)
                return;

            if (!rootType.IsValueType && !visited.Add(instance))
                return;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            for (Type t = rootType;
                 t != null && t != typeof(MonoBehaviour) && t != typeof(ScriptableObject) && t != typeof(UnityEngine.Object) && t != typeof(object);
                 t = t.BaseType)
            {
                FieldInfo[] fields = t.GetFields(flags);
                for (int f = 0; f < fields.Length; f++)
                {
                    FieldInfo field = fields[f];
                    Type fType = field.FieldType;
                    string fieldPath = string.IsNullOrEmpty(prefix) ? field.Name : $"{prefix}.{field.Name}";

                    if (typeof(IEventKey).IsAssignableFrom(fType))
                    {
                        if (field.GetValue(instance) is IEventKey keyVal)
                        {
                            RecordUsage(usedGuids, context, fieldPath, keyVal);
                        }
                    }
                    else if (IsEventKeyCollectionType(fType))
                    {
                        if (field.GetValue(instance) is IList list)
                        {
                            for (int idx = 0; idx < list.Count; idx++)
                            {
                                if (list[idx] is IEventKey keyElem)
                                {
                                    RecordUsage(usedGuids, context, $"{fieldPath}[{idx}]", keyElem);
                                }
                            }
                        }
                    }
                    else if (depth < 4)
                    {
                        Type elemType = GetCollectionElementType(fType);
                        if (elemType != null)
                        {
                            if (IsCustomSerializableContainer(elemType) && field.GetValue(instance) is IList list)
                            {
                                for (int idx = 0; idx < list.Count; idx++)
                                {
                                    object elem = list[idx];
                                    if (elem != null)
                                    {
                                        InspectObjectFields(context, elem, elem.GetType(), $"{fieldPath}[{idx}]", usedGuids, visited, depth + 1);
                                    }
                                }
                            }
                        }
                        else if (IsCustomSerializableContainer(fType))
                        {
                            object nested = field.GetValue(instance);
                            if (nested != null)
                            {
                                InspectObjectFields(context, nested, nested.GetType(), fieldPath, usedGuids, visited, depth + 1);
                            }
                        }
                    }
                }
            }
        }

        private void RecordUsage(
            HashSet<string> usedGuids,
            UnityEngine.Object context,
            string fieldName,
            IEventKey keyVal)
        {
            bool isAssigned = keyVal.IsAssigned;
            bool isMissing = false;
            bool isTypeMismatch = false;
            Type expectedPayloadType = null;

            if (isAssigned)
            {
                if (EventBoardGroup.TryFindByGuid(keyVal.Guid, out _, out var entry))
                {
                    usedGuids.Add(entry.guid);
                    expectedPayloadType = entry.PayloadType;
                    if (keyVal.ValueType != null && expectedPayloadType != null && expectedPayloadType != keyVal.ValueType)
                    {
                        isTypeMismatch = true;
                    }
                }
                else
                {
                    isMissing = true;
                }
            }

            _sceneUsages.Add(new SceneKeyUsage
            {
                Context = context,
                FieldName = fieldName,
                Guid = keyVal.Guid,
                Path = keyVal.CachedPath,
                ValueType = keyVal.ValueType,
                ExpectedPayloadType = expectedPayloadType,
                IsAssigned = isAssigned,
                IsMissingInGroups = isMissing,
                IsTypeMismatch = isTypeMismatch
            });
        }

        private static string GetUsageContextName(UnityEngine.Object context)
        {
            if (context == null)
                return "(Null)";
            return context is MonoBehaviour mb && mb.gameObject != null ? mb.gameObject.name : context.name;
        }

        private static void SelectUsageContext(UnityEngine.Object context)
        {
            if (context == null)
                return;

            UnityEngine.Object target = context is MonoBehaviour mb && mb.gameObject != null
                ? (UnityEngine.Object)mb.gameObject
                : context;
            Selection.activeObject = target;
            EditorGUIUtility.PingObject(target);
        }

        private static bool IsEventKeyCollectionType(Type fieldType)
        {
            Type elemType = GetCollectionElementType(fieldType);
            return elemType != null && typeof(IEventKey).IsAssignableFrom(elemType);
        }

        private static Type GetCollectionElementType(Type fieldType)
        {
            if (fieldType == null)
                return null;

            if (fieldType.IsArray)
                return fieldType.GetElementType();

            if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(List<>))
                return fieldType.GetGenericArguments()[0];

            return null;
        }

        private static bool IsCustomSerializableContainer(Type type)
        {
            if (type == null || type.IsPrimitive || type.IsEnum || type.IsPointer)
                return false;

            if (type == typeof(string) || type == typeof(decimal))
                return false;

            if (typeof(UnityEngine.Object).IsAssignableFrom(type) || typeof(Delegate).IsAssignableFrom(type))
                return false;

            if (type.Namespace != null &&
                (type.Namespace.StartsWith("System", StringComparison.Ordinal) ||
                 type.Namespace.StartsWith("UnityEngine", StringComparison.Ordinal)))
            {
                return false;
            }

            return type.IsValueType || type.IsDefined(typeof(SerializableAttribute), false);
        }

        private void DrawBenchmarkTab()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("⚡ EventBoard Performance & Zero-GC Benchmark", EditorStyles.boldLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    _benchmarkIterations = Mathf.Clamp(EditorGUILayout.IntField("Iterations", _benchmarkIterations), 1000, 10000000);
                    GUI.backgroundColor = new Color(0.35f, 0.9f, 0.5f);
                    if (GUILayout.Button("⚡ Run Benchmark", GUILayout.Height(22f), GUILayout.Width(120f)))
                    {
                        _lastBenchmarkReport = RunBenchmarkReport(_benchmarkIterations);
                    }
                    GUI.backgroundColor = Color.white;
                }
            }

            if (!string.IsNullOrEmpty(_lastBenchmarkReport))
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.HelpBox(_lastBenchmarkReport, MessageType.Info);
            }
        }

        public static string RunBenchmarkReport(int iterations = 100000)
        {
            iterations = Mathf.Clamp(iterations, 1000, 10000000);
            const int benchIdInt = -999991;
            const int benchIdVec = -999992;
            const int benchIdBool = -999993;

            int intSum = 0;
            float vecSum = 0f;
            int boolFiredCount = 0;

            Action<int> intSub1 = v => intSum += v;
            Action<int> intSub2 = v => intSum += v;
            Action<int> intSub3 = v => intSum += v;
            Action<int> intSub4 = v => intSum += v;

            Action<Vector3> vecSub1 = v => vecSum += v.x;
            Action<Vector3> vecSub2 = v => vecSum += v.y;
            Action<Vector3> vecSub3 = v => vecSum += v.z;
            Action<Vector3> vecSub4 = v => vecSum += v.x;

            Action<bool> boolSub = _ => boolFiredCount++;

            try
            {
                EventBoard.Subscribe(benchIdInt, intSub1);
                EventBoard.Subscribe(benchIdInt, intSub2);
                EventBoard.Subscribe(benchIdInt, intSub3);
                EventBoard.Subscribe(benchIdInt, intSub4);

                EventBoard.Subscribe(benchIdVec, vecSub1);
                EventBoard.Subscribe(benchIdVec, vecSub2);
                EventBoard.Subscribe(benchIdVec, vecSub3);
                EventBoard.Subscribe(benchIdVec, vecSub4);

                EventBoard.Subscribe(benchIdBool, boolSub);

                // JIT Warmup
                EventBoard.Publish(benchIdInt, 1);
                EventBoard.TryGet(benchIdInt, out int _);
                EventBoard.Publish(benchIdVec, Vector3.one);
                EventBoard.PublishIfChanged(benchIdBool, false);
                boolFiredCount = 0;

                // 1) Blackboard TryGet<int> Benchmark
                long memBeforeRead = GC.GetAllocatedBytesForCurrentThread();
                var swRead = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < iterations; i++)
                {
                    EventBoard.TryGet(benchIdInt, out int val);
                    intSum += val;
                }
                swRead.Stop();
                long allocRead = GC.GetAllocatedBytesForCurrentThread() - memBeforeRead;

                // 2) Publish<int> (4 Subscribers) Benchmark
                long memBeforeInt = GC.GetAllocatedBytesForCurrentThread();
                var swInt = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < iterations; i++)
                {
                    EventBoard.Publish(benchIdInt, i);
                }
                swInt.Stop();
                long allocInt = GC.GetAllocatedBytesForCurrentThread() - memBeforeInt;

                // 3) Publish<Vector3> (4 Subscribers - Zero-Boxing Struct) Benchmark
                Vector3 sampleVec = new Vector3(1.5f, 2.5f, 3.5f);
                long memBeforeVec = GC.GetAllocatedBytesForCurrentThread();
                var swVec = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < iterations; i++)
                {
                    EventBoard.Publish(benchIdVec, sampleVec);
                }
                swVec.Stop();
                long allocVec = GC.GetAllocatedBytesForCurrentThread() - memBeforeVec;

                // 4) PublishIfChanged<bool> (Spam Guard - 10 Transitions) Benchmark
                int changeStep = Mathf.Max(1, iterations / 10);
                bool curState = false;
                EventBoard.SetState(benchIdBool, false, notify: false);
                boolFiredCount = 0;

                long memBeforeGuard = GC.GetAllocatedBytesForCurrentThread();
                var swGuard = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < iterations; i++)
                {
                    if (i > 0 && i % changeStep == 0)
                        curState = !curState;
                    EventBoard.PublishIfChanged(benchIdBool, curState);
                }
                swGuard.Stop();
                long allocGuard = GC.GetAllocatedBytesForCurrentThread() - memBeforeGuard;

                double readMs = swRead.Elapsed.TotalMilliseconds;
                double intMs = swInt.Elapsed.TotalMilliseconds;
                double vecMs = swVec.Elapsed.TotalMilliseconds;
                double guardMs = swGuard.Elapsed.TotalMilliseconds;

                double readNs = (readMs * 1_000_000.0) / iterations;
                double intNs = (intMs * 1_000_000.0) / iterations;
                double vecNs = (vecMs * 1_000_000.0) / iterations;
                double guardNs = (guardMs * 1_000_000.0) / iterations;

                return
                    $"📊 EventBoard Benchmark Results ({iterations:N0} Iterations | Check: {intSum != 0 && vecSum > 0})\n" +
                    $"──────────────────────────────────────────────────────────\n" +
                    $"1. Blackboard Read (TryGet<int>):       {readMs:F2} ms  ({readNs:F1} ns/call)  |  GC: {allocRead} B\n" +
                    $"2. Publish<int> (4 Subscribers):        {intMs:F2} ms  ({intNs:F1} ns/call)  |  GC: {allocInt} B\n" +
                    $"3. Publish<Vector3> (4 Subs Struct):    {vecMs:F2} ms  ({vecNs:F1} ns/call)  |  GC: {allocVec} B\n" +
                    $"4. PublishIfChanged<bool> (Spam Guard): {guardMs:F2} ms  ({guardNs:F1} ns/call)  |  GC: {allocGuard} B  (Triggered: {boolFiredCount}/{iterations:N0})";
            }
            finally
            {
                EventBoard.RemoveChannel<int>(benchIdInt);
                EventBoard.RemoveChannel<Vector3>(benchIdVec);
                EventBoard.RemoveChannel<bool>(benchIdBool);
            }
        }

        private bool MatchesFilter(string path, string description)
        {
            if (string.IsNullOrEmpty(_searchFilter))
                return true;

            bool matchPath = !string.IsNullOrEmpty(path) && path.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0;
            bool matchDesc = !string.IsNullOrEmpty(description) && description.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0;
            return matchPath || matchDesc;
        }
    }
}
