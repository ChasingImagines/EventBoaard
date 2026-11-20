using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Yasin.EventBoard.Editor
{
    /// <summary>
    /// Custom Inspector for EventBoardGroup ScriptableObjects to define events, select payload types,
    /// configure default Blackboard values, and toggle saveability.
    /// </summary>
    [CustomEditor(typeof(EventBoardGroup))]
    public sealed class EventBoardGroupEditor : UnityEditor.Editor
    {
        private sealed class PayloadTypeInfo
        {
            public Type ConcretePayloadClass;
            public Type ValueType;
            public string DisplayName;
            public bool SupportsSaveable;
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
                            DisplayName = instance.DisplayName,
                            SupportsSaveable = instance.SupportsSaveable
                        });
                    }
                }
                catch
                {
                    // Skip classes without a parameterless constructor
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
            _filterDisplayNames[0] = "All";

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
            int removedRuntimeId = DrawEventList();

            if (serializedObject.ApplyModifiedProperties())
            {
                group.SanitizeEntries();
                group.RebuildLookups();
                if (removedRuntimeId != 0)
                {
                    EventBoard.RemoveChannelById(removedRuntimeId);
                }
                EventBoard.HydrateFromGroup(group);
            }
        }

        private void DrawHeaderBanner(EventBoardGroup group)
        {
            int total = _eventsProp != null ? _eventsProp.arraySize : 0;
            int saveableCount = 0;
            if (group.Events != null)
            {
                for (int i = 0; i < group.Events.Count; i++)
                {
                    if (group.Events[i] != null && group.Events[i].isSaveable)
                        saveableCount++;
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
                    $"Events: {total}   |   💾 Saveable: {saveableCount}",
                    EditorStyles.miniLabel);
            }
        }

        private void DrawQuickAddSection(EventBoardGroup group)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("➕ Add Event", EditorStyles.boldLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    _newEventName = EditorGUILayout.TextField("Name", _newEventName);
                    _newEventTypeIndex = EditorGUILayout.Popup(_newEventTypeIndex, _payloadDisplayNames, GUILayout.Width(105f));
                }

                int clampedIdx = Mathf.Clamp(_newEventTypeIndex, 0, _payloadTypes.Count - 1);
                bool canSaveable = _payloadTypes.Count > 0 && _payloadTypes[clampedIdx].SupportsSaveable;

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!canSaveable))
                    {
                        _newEventPersistent = canSaveable && EditorGUILayout.ToggleLeft("💾 Saveable", _newEventPersistent, GUILayout.Width(95f));
                    }
                    GUILayout.FlexibleSpace();

                    GUI.backgroundColor = new Color(0.35f, 0.88f, 0.48f);
                    if (GUILayout.Button("+ Add", GUILayout.Height(22f), GUILayout.Width(85f)))
                    {
                        serializedObject.ApplyModifiedProperties();
                        Undo.RecordObject(group, "Add Event To Group SO");
                        Type payloadClass = _payloadTypes[clampedIdx].ConcretePayloadClass;
                        EventPayloadDefinition payloadInstance = (EventPayloadDefinition)Activator.CreateInstance(payloadClass);
                        bool saveable = _newEventPersistent && payloadInstance.SupportsSaveable;
                        group.AddEntry(_newEventName, payloadInstance, saveable);
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
                _searchQuery = EditorGUILayout.TextField("🔍 Search", _searchQuery);
                if (!string.IsNullOrEmpty(_searchQuery) && GUILayout.Button("✕", GUILayout.Width(24f)))
                {
                    _searchQuery = string.Empty;
                    GUI.FocusControl(null);
                }

                _selectedTypeFilterIndex = EditorGUILayout.Popup(_selectedTypeFilterIndex, _filterDisplayNames, GUILayout.Width(105f));
            }
        }

        private int DrawEventList()
        {
            if (_eventsProp == null || _eventsProp.arraySize == 0)
            {
                EditorGUILayout.HelpBox("No events defined yet.", MessageType.Info);
                return 0;
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
                SerializedProperty payloadProp = entryProp.FindPropertyRelative("payload");
                string evName = nameProp != null ? nameProp.stringValue : string.Empty;
                string desc = descProp != null ? descProp.stringValue : string.Empty;

                if (!string.IsNullOrEmpty(_searchQuery))
                {
                    bool matchesName = evName.IndexOf(_searchQuery, StringComparison.OrdinalIgnoreCase) >= 0;
                    bool matchesDesc = desc.IndexOf(_searchQuery, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!matchesName && !matchesDesc)
                        continue;
                }

                if (requiredPayloadClass != null)
                {
                    Type actualType = payloadProp != null ? payloadProp.managedReferenceValue?.GetType() : null;
                    if (actualType != requiredPayloadClass)
                        continue;
                }

                if (DrawSingleEventEntry(entryProp))
                {
                    deleteIndex = i;
                }
            }

            int removedRuntimeId = 0;
            if (deleteIndex >= 0 && deleteIndex < _eventsProp.arraySize)
            {
                SerializedProperty delEntry = _eventsProp.GetArrayElementAtIndex(deleteIndex);
                SerializedProperty delGuidProp = delEntry.FindPropertyRelative("guid");
                if (delGuidProp != null && !string.IsNullOrEmpty(delGuidProp.stringValue))
                {
                    removedRuntimeId = EventBoard.ComputeId(delGuidProp.stringValue);
                }

                _eventsProp.DeleteArrayElementAtIndex(deleteIndex);
            }

            return removedRuntimeId;
        }

        private bool DrawSingleEventEntry(SerializedProperty entryProp)
        {
            SerializedProperty guidProp = entryProp.FindPropertyRelative("guid");
            SerializedProperty nameProp = entryProp.FindPropertyRelative("eventName");
            SerializedProperty payloadProp = entryProp.FindPropertyRelative("payload");
            SerializedProperty persistentProp = entryProp.FindPropertyRelative("isSaveable");
            SerializedProperty descProp = entryProp.FindPropertyRelative("description");

            EventPayloadDefinition currentPayload = payloadProp != null
                ? payloadProp.managedReferenceValue as EventPayloadDefinition
                : null;
            Type currentPayloadClass = currentPayload != null ? currentPayload.GetType() : typeof(VoidPayload);

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
                // Row 1: Event Name | Type Selector | Saveable | Delete
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(nameProp, GUIContent.none, GUILayout.MinWidth(140f));

                    int newTypeIdx = EditorGUILayout.Popup(currentTypeIdx, _payloadDisplayNames, GUILayout.Width(105f));
                    if (newTypeIdx != currentTypeIdx && newTypeIdx >= 0 && newTypeIdx < _payloadTypes.Count && payloadProp != null)
                    {
                        Type targetPayloadClass = _payloadTypes[newTypeIdx].ConcretePayloadClass;
                        EventPayloadDefinition newPayload = (EventPayloadDefinition)Activator.CreateInstance(targetPayloadClass);
                        payloadProp.managedReferenceValue = newPayload;
                        currentPayload = newPayload;
                        if (!newPayload.SupportsSaveable)
                        {
                            persistentProp.boolValue = false;
                        }
                    }

                    bool supportsSaveable = currentPayload != null && currentPayload.SupportsSaveable;
                    if (!supportsSaveable && persistentProp.boolValue)
                    {
                        persistentProp.boolValue = false;
                    }

                    using (new EditorGUI.DisabledScope(!supportsSaveable))
                    {
                        bool isPers = persistentProp.boolValue;
                        GUI.backgroundColor = isPers ? new Color(0.4f, 0.85f, 1f) : Color.white;
                        if (GUILayout.Button(new GUIContent("💾", supportsSaveable ? "Saveable (included in JSON snapshots)" : "This payload type does not support JSON Save/Load"), EditorStyles.miniButton, GUILayout.Width(28f)))
                        {
                            persistentProp.boolValue = !isPers;
                        }
                        GUI.backgroundColor = Color.white;
                    }

                    GUI.backgroundColor = new Color(1f, 0.45f, 0.45f);
                    if (GUILayout.Button(new GUIContent("✕", $"Delete (GUID: {guidProp.stringValue})"), EditorStyles.miniButton, GUILayout.Width(24f)))
                    {
                        GUI.backgroundColor = Color.white;
                        return true;
                    }
                    GUI.backgroundColor = Color.white;
                }

                // Row 2: Default Value and Note
                using (new EditorGUILayout.HorizontalScope())
                {
                    SerializedProperty defaultValProp = payloadProp != null ? payloadProp.FindPropertyRelative("defaultValue") : null;
                    bool supportsDefault = currentPayload != null && currentPayload.SupportsDefaultValue;

                    if (supportsDefault && defaultValProp != null)
                    {
                        EditorGUILayout.PropertyField(defaultValProp, new GUIContent("Default"), GUILayout.MinWidth(140f));
                    }

                    EditorGUILayout.PropertyField(descProp, new GUIContent("Note"), GUILayout.MinWidth(120f));
                }
            }

            return false;
        }
    }
}
