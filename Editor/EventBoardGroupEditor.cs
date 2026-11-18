using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Yasin.EventBoard.Editor
{
    /// <summary>
    /// EventBoardGroup ScriptableObject varlıkları için Inspector'dan kolayca yeni olay (event) tanımlama,
    /// veri tipi seçme, varsayılan Blackboard değeri girme ve kalıcılık ayarlama arayüzü.
    /// </summary>
    [CustomEditor(typeof(EventBoardGroup))]
    public sealed class EventBoardGroupEditor : UnityEditor.Editor
    {
        private sealed class PayloadTypeInfo
        {
            public Type ConcretePayloadClass;
            public Type ValueType;
            public string DisplayName;
        }

        private SerializedProperty _eventsProp;
        private string _searchQuery = string.Empty;
        private int _selectedTypeFilterIndex;
        private string _newEventName = "NewEvent";
        private int _newEventTypeIndex;
        private bool _newEventPersistent;

        private static List<PayloadTypeInfo> _payloadTypes;
        private static string[] _payloadDisplayNames;
        private static string[] _filterDisplayNames;

        private void OnEnable()
        {
            _eventsProp = serializedObject.FindProperty("events");
            EnsurePayloadTypesCached();
        }

        private static void EnsurePayloadTypesCached()
        {
            if (_payloadTypes != null && _payloadTypes.Count > 0)
                return;

            _payloadTypes = new List<PayloadTypeInfo>();
            TypeCache.TypeCollection derivedTypes = TypeCache.GetTypesDerivedFrom<EventPayloadDefinition>();

            foreach (Type t in derivedTypes)
            {
                if (t.IsAbstract || t.IsGenericType)
                    continue;

                try
                {
                    if (Activator.CreateInstance(t) is EventPayloadDefinition instance)
                    {
                        _payloadTypes.Add(new PayloadTypeInfo
                        {
                            ConcretePayloadClass = t,
                            ValueType = instance.PayloadType,
                            DisplayName = instance.DisplayName
                        });
                    }
                }
                catch
                {
                    // Parametresiz kurucusu olmayan sınıfları atla
                }
            }

            _payloadTypes.Sort((a, b) =>
            {
                if (a.ConcretePayloadClass == typeof(VoidPayload)) return -1;
                if (b.ConcretePayloadClass == typeof(VoidPayload)) return 1;
                return string.Compare(a.DisplayName, b.DisplayName, StringComparison.Ordinal);
            });

            _payloadDisplayNames = new string[_payloadTypes.Count];
            _filterDisplayNames = new string[_payloadTypes.Count + 1];
            _filterDisplayNames[0] = "Tümü";

            for (int i = 0; i < _payloadTypes.Count; i++)
            {
                _payloadDisplayNames[i] = _payloadTypes[i].DisplayName;
                _filterDisplayNames[i + 1] = _payloadTypes[i].DisplayName;
            }
        }

        public override void OnInspectorGUI()
        {
            EnsurePayloadTypesCached();
            serializedObject.Update();

            EventBoardGroup group = (EventBoardGroup)target;

            DrawHeaderBanner(group);
            EditorGUILayout.Space(6f);
            DrawQuickAddSection(group);
            EditorGUILayout.Space(6f);
            DrawSearchAndFilterBar();
            EditorGUILayout.Space(6f);
            DrawEventList(group);

            if (serializedObject.ApplyModifiedProperties())
            {
                group.SanitizeEntries();
                group.RebuildLookups();
                EventBoard.HydrateFromGroup(group);
            }
        }

        private void DrawHeaderBanner(EventBoardGroup group)
        {
            int total = _eventsProp != null ? _eventsProp.arraySize : 0;
            int persistentCount = 0;
            if (group.Events != null)
            {
                for (int i = 0; i < group.Events.Count; i++)
                {
                    if (group.Events[i] != null && group.Events[i].isPersistent)
                        persistentCount++;
                }
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"📦 {group.GroupName}", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    GUI.backgroundColor = new Color(0.4f, 0.8f, 1f);
                    if (GUILayout.Button("🌐 EventBoard", GUILayout.Height(22f), GUILayout.Width(105f)))
                    {
                        EventBoardWindow.OpenWindow();
                    }
                    GUI.backgroundColor = Color.white;
                }

                EditorGUILayout.LabelField(
                    $"Event: {total}   |   📌 Kalıcı: {persistentCount}",
                    EditorStyles.miniLabel);
            }
        }

        private void DrawQuickAddSection(EventBoardGroup group)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("➕ Yeni Event", EditorStyles.boldLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    _newEventName = EditorGUILayout.TextField("Ad", _newEventName);
                    _newEventTypeIndex = EditorGUILayout.Popup(_newEventTypeIndex, _payloadDisplayNames, GUILayout.Width(105f));
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    _newEventPersistent = EditorGUILayout.ToggleLeft("📌 Kalıcı", _newEventPersistent, GUILayout.Width(85f));
                    GUILayout.FlexibleSpace();

                    GUI.backgroundColor = new Color(0.35f, 0.88f, 0.48f);
                    if (GUILayout.Button("+ Ekle", GUILayout.Height(22f), GUILayout.Width(85f)))
                    {
                        Undo.RecordObject(group, "Add Event To Group SO");
                        Type payloadClass = _payloadTypes[Mathf.Clamp(_newEventTypeIndex, 0, _payloadTypes.Count - 1)].ConcretePayloadClass;
                        EventPayloadDefinition payloadInstance = (EventPayloadDefinition)Activator.CreateInstance(payloadClass);
                        group.AddEntry(_newEventName, payloadInstance, _newEventPersistent);
                        EditorUtility.SetDirty(group);
                        serializedObject.Update();
                    }
                    GUI.backgroundColor = Color.white;
                }
            }
        }

        private void DrawSearchAndFilterBar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                _searchQuery = EditorGUILayout.TextField("🔍 Ara", _searchQuery);
                if (!string.IsNullOrEmpty(_searchQuery) && GUILayout.Button("✕", GUILayout.Width(24f)))
                {
                    _searchQuery = string.Empty;
                    GUI.FocusControl(null);
                }

                _selectedTypeFilterIndex = EditorGUILayout.Popup(_selectedTypeFilterIndex, _filterDisplayNames, GUILayout.Width(105f));
            }
        }

        private void DrawEventList(EventBoardGroup group)
        {
            if (_eventsProp == null || _eventsProp.arraySize == 0)
            {
                EditorGUILayout.HelpBox("Henüz event yok.", MessageType.Info);
                return;
            }

            Type requiredPayloadClass = _selectedTypeFilterIndex > 0 && _selectedTypeFilterIndex - 1 < _payloadTypes.Count
                ? _payloadTypes[_selectedTypeFilterIndex - 1].ConcretePayloadClass
                : null;

            int deleteIndex = -1;

            for (int i = 0; i < _eventsProp.arraySize; i++)
            {
                SerializedProperty entryProp = _eventsProp.GetArrayElementAtIndex(i);
                SerializedProperty nameProp = entryProp.FindPropertyRelative("eventName");
                SerializedProperty descProp = entryProp.FindPropertyRelative("description");
                string evName = nameProp != null ? nameProp.stringValue : string.Empty;
                string desc = descProp != null ? descProp.stringValue : string.Empty;

                if (!string.IsNullOrEmpty(_searchQuery))
                {
                    bool matchesName = evName.IndexOf(_searchQuery, StringComparison.OrdinalIgnoreCase) >= 0;
                    bool matchesDesc = desc.IndexOf(_searchQuery, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!matchesName && !matchesDesc)
                        continue;
                }

                if (requiredPayloadClass != null && i < group.Events.Count && group.Events[i]?.payload != null)
                {
                    if (group.Events[i].payload.GetType() != requiredPayloadClass)
                        continue;
                }

                if (DrawSingleEventEntry(group, i))
                {
                    deleteIndex = i;
                }
            }

            if (deleteIndex >= 0 && deleteIndex < _eventsProp.arraySize)
            {
                _eventsProp.DeleteArrayElementAtIndex(deleteIndex);
            }
        }

        private bool DrawSingleEventEntry(EventBoardGroup group, int index)
        {
            SerializedProperty entryProp = _eventsProp.GetArrayElementAtIndex(index);
            SerializedProperty guidProp = entryProp.FindPropertyRelative("guid");
            SerializedProperty nameProp = entryProp.FindPropertyRelative("eventName");
            SerializedProperty payloadProp = entryProp.FindPropertyRelative("payload");
            SerializedProperty persistentProp = entryProp.FindPropertyRelative("isPersistent");
            SerializedProperty descProp = entryProp.FindPropertyRelative("description");

            EventBoardGroup.EventEntry rawEntry = index < group.Events.Count ? group.Events[index] : null;
            Type currentPayloadClass = rawEntry?.payload != null ? rawEntry.payload.GetType() : typeof(VoidPayload);
            int currentTypeIdx = 0;
            for (int i = 0; i < _payloadTypes.Count; i++)
            {
                if (_payloadTypes[i].ConcretePayloadClass == currentPayloadClass)
                {
                    currentTypeIdx = i;
                    break;
                }
            }

            using (new EditorGUILayout.VerticalScope(GUI.skin.box))
            {
                // 1. Satır: Event Adı | Veri Tipi Seçici | Kalıcı (📌) | Sil (✕)
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(nameProp, GUIContent.none, GUILayout.MinWidth(140f));

                    int newTypeIdx = EditorGUILayout.Popup(currentTypeIdx, _payloadDisplayNames, GUILayout.Width(105f));
                    if (newTypeIdx != currentTypeIdx && newTypeIdx >= 0 && newTypeIdx < _payloadTypes.Count)
                    {
                        Type targetPayloadClass = _payloadTypes[newTypeIdx].ConcretePayloadClass;
                        payloadProp.managedReferenceValue = Activator.CreateInstance(targetPayloadClass);
                    }

                    bool isPers = persistentProp.boolValue;
                    GUI.backgroundColor = isPers ? new Color(0.4f, 0.85f, 1f) : Color.white;
                    if (GUILayout.Button(new GUIContent("📌", "Kalıcı (Persistent)"), EditorStyles.miniButton, GUILayout.Width(28f)))
                    {
                        persistentProp.boolValue = !isPers;
                    }
                    GUI.backgroundColor = Color.white;

                    GUI.backgroundColor = new Color(1f, 0.45f, 0.45f);
                    if (GUILayout.Button(new GUIContent("✕", $"Sil (GUID: {guidProp.stringValue})"), EditorStyles.miniButton, GUILayout.Width(24f)))
                    {
                        GUI.backgroundColor = Color.white;
                        return true;
                    }
                    GUI.backgroundColor = Color.white;
                }

                // 2. Satır: Varsayılan (DefaultValue) ve Not
                using (new EditorGUILayout.HorizontalScope())
                {
                    SerializedProperty defaultValProp = payloadProp != null ? payloadProp.FindPropertyRelative("defaultValue") : null;
                    bool supportsDefault = rawEntry?.payload != null && rawEntry.payload.SupportsDefaultValue;

                    if (supportsDefault && defaultValProp != null)
                    {
                        EditorGUILayout.PropertyField(defaultValProp, new GUIContent("Varsayılan"), GUILayout.MinWidth(140f));
                    }

                    EditorGUILayout.PropertyField(descProp, new GUIContent("Not"), GUILayout.MinWidth(120f));
                }
            }

            return false;
        }
    }
}
