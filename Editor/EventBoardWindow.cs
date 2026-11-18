using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Yasin.EventBoard.Editor
{
    /// <summary>
    /// EventBoard için 3 sekmeli profesyonel mühendislik ve hata ayıklama penceresi:
    /// 1) Canlı Blackboard Monitörü &amp; Test Tetikleyici (Grup SO bazında kırılımlı)
    /// 2) Sahne Bağlantı Matrisi
    /// 3) Statik Proje Denetimi (Audit)
    /// </summary>
    public sealed class EventBoardWindow : EditorWindow
    {
        private sealed class SceneKeyUsage
        {
            public MonoBehaviour Component;
            public string FieldName;
            public string Guid;
            public string Path;
            public Type ValueType;
            public bool IsAssigned;
            public bool IsMissingInGroups;
        }

        private int _selectedTab;
        private readonly string[] _tabLabels =
        {
            "📡 Canlı",
            "🕸️ Bağlantılar",
            "🛡️ Denetim",
            "⚡ Performans"
        };

        private string _searchFilter = string.Empty;
        private string _newGroupAssetName = "NewEvents";
        private Vector2 _scrollPos;
        private int _benchmarkIterations = 100000;
        private string _lastBenchmarkReport = string.Empty;

        // Canlı Test Tetikleyici durumu
        private string _selectedChannelGuid = string.Empty;
        private int _testInt;
        private float _testFloat;
        private bool _testBool;
        private string _testString = "Test";
        private Vector2 _testVector2;
        private Vector3 _testVector3;
        private Color _testColor = Color.cyan;

        // Sahne ve Denetim önbelleği
        private readonly List<SceneKeyUsage> _sceneUsages = new List<SceneKeyUsage>();
        private readonly List<string> _orphanChannelPaths = new List<string>();
        private readonly List<Delegate> _subscriberBuffer = new List<Delegate>(8);
        private bool _matrixBuilt;

        [MenuItem("Tools/EventBoard/Hub", priority = 100)]
        public static void OpenWindow()
        {
            EventBoardWindow window = GetWindow<EventBoardWindow>("EventBoard Hub");
            window.minSize = new Vector2(700f, 440f);
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
                EditorGUILayout.HelpBox("Resources/EventBoard klasöründe henüz bir EventBoardGroup SO varlığı bulunamadı.", MessageType.Warning);
                using (new EditorGUILayout.HorizontalScope())
                {
                    _newGroupAssetName = EditorGUILayout.TextField("Yeni Grup SO", _newGroupAssetName);
                    if (GUILayout.Button("+ Oluştur", GUILayout.Height(24f), GUILayout.Width(100f)))
                    {
                        EventKeyDrawer.CreateGroupAssetInResources(_newGroupAssetName, ping: true);
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

                if (GUILayout.Button("🔄 Yenile", EditorStyles.toolbarButton, GUILayout.Width(68f)))
                {
                    EventBoardGroup.InvalidateGroupCache();
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
                    if (GUILayout.Button("Temizle", EditorStyles.miniButton, GUILayout.Width(75f)))
                    {
                        EventBoard.ClearNonPersistent();
                    }
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
                        EditorGUILayout.LabelField("   (Boş)", EditorStyles.miniLabel);
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
                                string persistBadge = entry.isPersistent ? " 📌" : string.Empty;
                                string title = $"{entry.eventName}{persistBadge}";
                                if (GUILayout.Button(title, EditorStyles.boldLabel))
                                {
                                    _selectedChannelGuid = isSelected ? string.Empty : entry.guid;
                                }

                                GUILayout.FlexibleSpace();

                                string typeBadge = $"[{EventKeyDrawer.GetFriendlyTypeName(entry.PayloadType)}]";
                                EditorGUILayout.LabelField(typeBadge, EditorStyles.miniLabel, GUILayout.Width(80f));

                                string valText = Application.isPlaying && liveSlot != null
                                    ? $"Değer: {liveSlot.GetFormattedValue()}"
                                    : $"Varsayılan: {(entry.payload != null ? entry.payload.GetDefaultValueSummary() : "—")}";

                                EditorGUILayout.LabelField(valText, GUILayout.Width(165f));

                                int subCount = liveSlot != null ? liveSlot.SubscriberCount : 0;
                                int pubCount = liveSlot != null ? liveSlot.PublishCount : 0;
                                EditorGUILayout.LabelField($"Abone: {subCount}  |  Yayın: {pubCount}", EditorStyles.miniLabel, GUILayout.Width(120f));

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
                    EditorGUILayout.LabelField($"Not: {entry.description}", EditorStyles.wordWrappedMiniLabel);
                }

                // Canlı Abone Listesi
                if (Application.isPlaying && liveSlot != null && liveSlot.SubscriberCount > 0)
                {
                    EditorGUILayout.Space(2f);
                    EditorGUILayout.LabelField("Aboneler:", EditorStyles.boldLabel);
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
                                if (GUILayout.Button("🎯 Seç", EditorStyles.miniButton, GUILayout.Width(55f)))
                                {
                                    EditorGUIUtility.PingObject(unityObj);
                                    Selection.activeObject = unityObj;
                                }
                            }
                        }
                    }
                }

                // Play Mode Canlı Test Tetikleyici
                if (Application.isPlaying)
                {
                    EditorGUILayout.Space(4f);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("⚡ Test:", EditorStyles.boldLabel, GUILayout.Width(60f));
                        object payloadToFire = DrawTestPayloadField(entry.PayloadType);

                        GUI.backgroundColor = new Color(0.35f, 0.9f, 0.5f);
                        if (GUILayout.Button("⚡ Tetikle", GUILayout.Width(85f)))
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

            EditorGUILayout.LabelField($"({type.Name} tipi için varsayılan değer gönderilir)", EditorStyles.miniLabel);
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }

        private static void TriggerEditorTestPublish(EventBoardGroup group, EventBoardGroup.EventEntry entry, IEventChannelDiagnostic liveSlot, object boxedPayload)
        {
            if (liveSlot != null && liveSlot.TryPublishFromEditor(boxedPayload))
                return;

            if (entry.payload != null)
            {
                entry.payload.ConfigureChannel(entry.RuntimeId, entry.guid, group.GetFullChannelName(entry), entry.isPersistent);
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

            EditorGUILayout.HelpBox(
                $"Aktif sahnede toplam {_sceneUsages.Count} adet EventKey<T> referansı bulundu.",
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

                    List<SceneKeyUsage> usagesForEntry = new List<SceneKeyUsage>();
                    for (int u = 0; u < _sceneUsages.Count; u++)
                    {
                        SceneKeyUsage usage = _sceneUsages[u];
                        if (string.Equals(usage.Guid, entry.guid, StringComparison.Ordinal))
                        {
                            usagesForEntry.Add(usage);
                        }
                    }

                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.LabelField(
                            $"📦 {fullName}   ({EventKeyDrawer.GetFriendlyTypeName(entry.PayloadType)})   —   Sahne Bağlantısı: {usagesForEntry.Count}",
                            EditorStyles.boldLabel);

                        if (usagesForEntry.Count == 0)
                        {
                            EditorGUILayout.LabelField("   Bu sahnede doğrudan bağlanan bir MonoBehaviour yok.", EditorStyles.miniLabel);
                        }
                        else
                        {
                            for (int k = 0; k < usagesForEntry.Count; k++)
                            {
                                SceneKeyUsage usage = usagesForEntry[k];
                                if (usage.Component == null)
                                    continue;

                                using (new EditorGUILayout.HorizontalScope())
                                {
                                    EditorGUILayout.LabelField(
                                        $"   • {usage.Component.gameObject.name}  [{usage.Component.GetType().Name}.{usage.FieldName}]",
                                        EditorStyles.miniLabel);

                                    if (GUILayout.Button("🎯 Seç", EditorStyles.miniButton, GUILayout.Width(60f)))
                                    {
                                        Selection.activeGameObject = usage.Component.gameObject;
                                        EditorGUIUtility.PingObject(usage.Component.gameObject);
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

            int unassignedCount = 0;
            int missingCount = 0;

            for (int i = 0; i < _sceneUsages.Count; i++)
            {
                if (!_sceneUsages[i].IsAssigned) unassignedCount++;
                else if (_sceneUsages[i].IsMissingInGroups) missingCount++;
            }

            if (unassignedCount == 0 && missingCount == 0)
            {
                EditorGUILayout.HelpBox("✅ Harika! Aktif sahnede atanmamış (Unassigned) veya kırık (Missing) hiçbir EventKey<T> referansı bulunmadı.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    $"⚠️ Sahnede {unassignedCount} atanmamış alan ve {missingCount} Grup SO'larından silinmiş referans tespit edildi:",
                    MessageType.Warning);

                for (int i = 0; i < _sceneUsages.Count; i++)
                {
                    SceneKeyUsage usage = _sceneUsages[i];
                    if (usage.Component == null || (usage.IsAssigned && !usage.IsMissingInGroups))
                        continue;

                    using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                    {
                        string issue = !usage.IsAssigned
                            ? "⚠️ Atanmamış (Boş) Alan"
                            : $"❌ Silinmiş Event ({usage.Path})";

                        EditorGUILayout.LabelField(
                            $"{issue}: {usage.Component.gameObject.name} -> {usage.Component.GetType().Name}.{usage.FieldName} ({EventKeyDrawer.GetFriendlyTypeName(usage.ValueType)})");

                        if (GUILayout.Button("🎯 Seç", GUILayout.Width(65f)))
                        {
                            Selection.activeGameObject = usage.Component.gameObject;
                            EditorGUIUtility.PingObject(usage.Component.gameObject);
                        }
                    }
                }
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField($"👻 Sahnede Kullanılmayan Eventler ({_orphanChannelPaths.Count})", EditorStyles.boldLabel);
            if (_orphanChannelPaths.Count == 0)
            {
                EditorGUILayout.LabelField("Grup SO'larındaki tüm eventler aktif sahnede kullanılıyor.", EditorStyles.miniLabel);
            }
            else
            {
                for (int i = 0; i < _orphanChannelPaths.Count; i++)
                {
                    EditorGUILayout.LabelField($"• {_orphanChannelPaths[i]}", EditorStyles.miniLabel);
                }
            }
        }

        public void RefreshSceneMatrix()
        {
            _sceneUsages.Clear();
            _orphanChannelPaths.Clear();
            _matrixBuilt = true;

            IReadOnlyList<EventBoardGroup> groups = EventBoardGroup.GetAllGroups(forceRefresh: true);
            HashSet<string> usedGuids = new HashSet<string>(StringComparer.Ordinal);

            MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour mb = behaviours[i];
                if (mb == null)
                    continue;

                Type type = mb.GetType();
                FieldInfo[] fields = type.GetFields(flags);

                for (int f = 0; f < fields.Length; f++)
                {
                    FieldInfo field = fields[f];
                    Type fType = field.FieldType;

                    if (typeof(IEventKey).IsAssignableFrom(fType))
                    {
                        if (field.GetValue(mb) is IEventKey keyVal)
                        {
                            RecordUsage(usedGuids, mb, field.Name, keyVal);
                        }
                    }
                    else if (IsEventKeyCollectionType(fType))
                    {
                        if (field.GetValue(mb) is IList list)
                        {
                            for (int idx = 0; idx < list.Count; idx++)
                            {
                                if (list[idx] is IEventKey keyElem)
                                {
                                    RecordUsage(usedGuids, mb, $"{field.Name}[{idx}]", keyElem);
                                }
                            }
                        }
                    }
                }
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

        private void RecordUsage(
            HashSet<string> usedGuids,
            MonoBehaviour mb,
            string fieldName,
            IEventKey keyVal)
        {
            bool isAssigned = keyVal.IsAssigned;
            bool isMissing = false;

            if (isAssigned)
            {
                if (EventBoardGroup.TryFindByGuid(keyVal.Guid, out _, out var entry))
                {
                    usedGuids.Add(entry.guid);
                }
                else
                {
                    isMissing = true;
                }
            }

            _sceneUsages.Add(new SceneKeyUsage
            {
                Component = mb,
                FieldName = fieldName,
                Guid = keyVal.Guid,
                Path = keyVal.CachedPath,
                ValueType = keyVal.ValueType,
                IsAssigned = isAssigned,
                IsMissingInGroups = isMissing
            });
        }

        private static bool IsEventKeyCollectionType(Type fieldType)
        {
            if (fieldType == null)
                return false;

            if (fieldType.IsArray)
            {
                Type elemType = fieldType.GetElementType();
                return elemType != null && typeof(IEventKey).IsAssignableFrom(elemType);
            }

            if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(List<>))
            {
                Type elemType = fieldType.GetGenericArguments()[0];
                return elemType != null && typeof(IEventKey).IsAssignableFrom(elemType);
            }

            return false;
        }

        private void DrawBenchmarkTab()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("⚡ EventBoard Performans & Zero-GC Testi", EditorStyles.boldLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    _benchmarkIterations = Mathf.Clamp(EditorGUILayout.IntField("Tekrar Sayısı", _benchmarkIterations), 1000, 10000000);
                    GUI.backgroundColor = new Color(0.35f, 0.9f, 0.5f);
                    if (GUILayout.Button("⚡ Test Et", GUILayout.Height(22f), GUILayout.Width(95f)))
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

                // JIT Isınma (Warmup)
                EventBoard.Publish(benchIdInt, 1);
                EventBoard.TryGet(benchIdInt, out int _);
                EventBoard.Publish(benchIdVec, Vector3.one);
                EventBoard.PublishIfChanged(benchIdBool, false);
                boolFiredCount = 0;

                // 1) Blackboard TryGet<int> Testi
                long memBeforeRead = GC.GetAllocatedBytesForCurrentThread();
                var swRead = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < iterations; i++)
                {
                    EventBoard.TryGet(benchIdInt, out int val);
                    intSum += val;
                }
                swRead.Stop();
                long allocRead = GC.GetAllocatedBytesForCurrentThread() - memBeforeRead;

                // 2) Publish<int> (4 Abone) Testi
                long memBeforeInt = GC.GetAllocatedBytesForCurrentThread();
                var swInt = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < iterations; i++)
                {
                    EventBoard.Publish(benchIdInt, i);
                }
                swInt.Stop();
                long allocInt = GC.GetAllocatedBytesForCurrentThread() - memBeforeInt;

                // 3) Publish<Vector3> (4 Abone - Struct Zero-Boxing) Testi
                Vector3 sampleVec = new Vector3(1.5f, 2.5f, 3.5f);
                long memBeforeVec = GC.GetAllocatedBytesForCurrentThread();
                var swVec = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < iterations; i++)
                {
                    EventBoard.Publish(benchIdVec, sampleVec);
                }
                swVec.Stop();
                long allocVec = GC.GetAllocatedBytesForCurrentThread() - memBeforeVec;

                // 4) PublishIfChanged<bool> (Yüksek Frekans Spam Koruması - 10 Değişim) Testi
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
                    $"📊 EventBoard Benchmark Sonucu ({iterations:N0} İterasyon | Kontrol: {intSum != 0 && vecSum > 0})\n" +
                    $"──────────────────────────────────────────────────────────\n" +
                    $"1. Blackboard Okuma (TryGet<int>):     {readMs:F2} ms  ({readNs:F1} ns/çağrı)  |  GC: {allocRead} B\n" +
                    $"2. Publish<int> (4 Abone):              {intMs:F2} ms  ({intNs:F1} ns/çağrı)  |  GC: {allocInt} B\n" +
                    $"3. Publish<Vector3> (4 Abone Struct):   {vecMs:F2} ms  ({vecNs:F1} ns/çağrı)  |  GC: {allocVec} B\n" +
                    $"4. PublishIfChanged<bool> (Spam Guard): {guardMs:F2} ms  ({guardNs:F1} ns/çağrı)  |  GC: {allocGuard} B  (Tetiklenen: {boolFiredCount}/{iterations:N0})";
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
