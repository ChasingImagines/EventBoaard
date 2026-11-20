using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Yasin.EventBoard.Editor
{
    /// <summary>
    /// PropertyDrawer for EventKey&lt;T&gt; featuring automatic type filtering, unbreakable GUID binding,
    /// live Play Mode Blackboard display, and a [+] quick-create popup to define events directly in a Group SO.
    /// </summary>
    [CustomPropertyDrawer(typeof(EventKey<>), true)]
    public sealed class EventKeyDrawer : PropertyDrawer
    {
        private sealed class QuickCreatePopup : PopupWindowContent
        {
            private readonly IReadOnlyList<EventBoardGroup> _groups;
            private readonly string[] _groupNames;
            private readonly Type _payloadType;
            private readonly Action<string, string> _onCreated;
            private readonly EventPayloadDefinition _previewPayload;
            private readonly bool _hasPayloadDefinition;

            private int _selectedGroupIndex;
            private bool _createNewGroupSO;
            private string _newGroupSOName = "NewEvents";
            private string _newEventName = "NewEvent";
            private bool _isSaveable;
            private string _description = string.Empty;
            private bool _focused;

            public QuickCreatePopup(
                IReadOnlyList<EventBoardGroup> groups,
                Type payloadType,
                EventBoardGroup initialGroup,
                Action<string, string> onCreated)
            {
                _groups = groups ?? Array.Empty<EventBoardGroup>();
                _payloadType = payloadType ?? typeof(VoidSignal);
                _onCreated = onCreated;
                _hasPayloadDefinition = EventBoardGroup.TryCreatePayloadForType(_payloadType, out _previewPayload);

                _groupNames = new string[_groups.Count];
                for (int i = 0; i < _groups.Count; i++)
                {
                    _groupNames[i] = _groups[i] != null ? _groups[i].GroupName : "(Null)";
                    if (initialGroup != null && _groups[i] == initialGroup)
                    {
                        _selectedGroupIndex = i;
                    }
                }

                if (_groups.Count == 0)
                {
                    _createNewGroupSO = true;
                }
            }

            public override Vector2 GetWindowSize() => new Vector2(360f, _hasPayloadDefinition ? 195f : 245f);

            public override void OnGUI(Rect rect)
            {
                GUILayout.Space(6f);
                EditorGUILayout.LabelField($"New Event ({GetFriendlyTypeName(_payloadType)})", EditorStyles.boldLabel);

                if (!_hasPayloadDefinition)
                {
                    EditorGUILayout.HelpBox(
                        $"No EventPayloadDefinition<{_payloadType.Name}> subclass is defined for '{_payloadType.Name}'.\n\n" +
                        $"Example:\n[Serializable] public sealed class {_payloadType.Name}Payload : EventPayloadDefinition<{_payloadType.Name}>\n{{\n    public override string DisplayName => \"{_payloadType.Name}\";\n}}",
                        MessageType.Error);
                }

                using (new EditorGUI.DisabledScope(!_hasPayloadDefinition))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (!_createNewGroupSO && _groupNames.Length > 0)
                        {
                            _selectedGroupIndex = EditorGUILayout.Popup("Group", _selectedGroupIndex, _groupNames);
                            if (GUILayout.Button("+ Group", EditorStyles.miniButton, GUILayout.Width(65f)))
                            {
                                _createNewGroupSO = true;
                            }
                        }
                        else
                        {
                            _newGroupSOName = EditorGUILayout.TextField("New Group", _newGroupSOName);
                            if (_groupNames.Length > 0 && GUILayout.Button("Select", EditorStyles.miniButton, GUILayout.Width(55f)))
                            {
                                _createNewGroupSO = false;
                            }
                        }
                    }

                    GUI.SetNextControlName("QuickEventNameField");
                    _newEventName = EditorGUILayout.TextField("Name", _newEventName);

                    bool supportsSaveable = _previewPayload != null && _previewPayload.SupportsSaveable;
                    using (new EditorGUI.DisabledScope(!supportsSaveable))
                    {
                        _isSaveable = supportsSaveable && EditorGUILayout.Toggle("💾 Saveable", _isSaveable);
                    }

                    _description = EditorGUILayout.TextField("Note", _description);

                    if (!_focused)
                    {
                        EditorGUI.FocusTextInControl("QuickEventNameField");
                        _focused = true;
                    }
                }

                GUILayout.FlexibleSpace();

                bool enterPressed = _hasPayloadDefinition &&
                                    Event.current.type == EventType.KeyDown &&
                                    (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Cancel", GUILayout.Height(24f)))
                    {
                        editorWindow.Close();
                    }

                    using (new EditorGUI.DisabledScope(!_hasPayloadDefinition))
                    {
                        GUI.backgroundColor = new Color(0.35f, 0.85f, 0.45f);
                        if (GUILayout.Button("Add", GUILayout.Height(24f)) || enterPressed)
                        {
                            CreateAndAssign();
                        }
                        GUI.backgroundColor = Color.white;
                    }
                }
                GUILayout.Space(4f);
            }

            private void CreateAndAssign()
            {
                if (!EventBoardGroup.TryCreatePayloadForType(_payloadType, out EventPayloadDefinition payload))
                    return;

                string cleanEventName = EventBoardGroup.CleanEventName(_newEventName);
                if (string.IsNullOrEmpty(cleanEventName))
                    return;

                EventBoardGroup targetGroup = null;
                if (_createNewGroupSO || _groups.Count == 0)
                {
                    targetGroup = EventBoardRegistryUtility.CreateGroupAsset(_newGroupSOName);
                }
                else if (_selectedGroupIndex >= 0 && _selectedGroupIndex < _groups.Count)
                {
                    targetGroup = _groups[_selectedGroupIndex];
                }

                if (targetGroup == null)
                    return;

                Undo.RecordObject(targetGroup, "Add Event To Group SO");
                bool saveable = _isSaveable && payload.SupportsSaveable;
                EventBoardGroup.EventEntry entry = targetGroup.AddEntry(cleanEventName, payload, saveable, _description);
                EditorUtility.SetDirty(targetGroup);
                AssetDatabase.SaveAssets();

                _onCreated?.Invoke(entry.guid, targetGroup.GetFullChannelName(entry));
                editorWindow.Close();
            }
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            SerializedProperty guidProp = property.FindPropertyRelative("guid");
            SerializedProperty pathProp = property.FindPropertyRelative("cachedPath");

            Type payloadType = ExtractPayloadType(fieldInfo != null ? fieldInfo.FieldType : null);
            string typeName = GetFriendlyTypeName(payloadType);

            bool hasMultipleValues = guidProp != null && guidProp.hasMultipleDifferentValues;
            string currentGuid = !hasMultipleValues && guidProp != null ? guidProp.stringValue : string.Empty;
            string currentPath = !hasMultipleValues && pathProp != null ? pathProp.stringValue : string.Empty;

            EventBoardGroup ownerGroup = null;
            EventBoardGroup.EventEntry resolvedEntry = null;
            bool isAssigned = !hasMultipleValues && !string.IsNullOrEmpty(currentGuid);
            bool isMissing = false;
            bool isTypeMismatch = false;

            if (isAssigned)
            {
                if (EventBoardGroup.TryFindByGuid(currentGuid, out ownerGroup, out resolvedEntry))
                {
                    if (resolvedEntry != null && payloadType != null && resolvedEntry.PayloadType != payloadType)
                    {
                        isTypeMismatch = true;
                    }
                }
                else
                {
                    isMissing = true;
                }
            }

            // Label & Tooltip
            string tooltip = $"Type: EventKey<{typeName}>";
            if (resolvedEntry != null && ownerGroup != null)
            {
                tooltip += $"\nGroup SO: {ownerGroup.GroupName}\nEvent: {resolvedEntry.eventName}\nSaveable: {(resolvedEntry.isSaveable ? "Yes 💾" : "No")}";
                if (!string.IsNullOrEmpty(resolvedEntry.description))
                    tooltip += $"\nDescription: {resolvedEntry.description}";
            }

            GUIContent richLabel = new GUIContent(label.text, tooltip);
            Rect fieldRect = EditorGUI.PrefixLabel(position, richLabel);

            const float addButtonWidth = 24f;
            const float spacing = 3f;
            Rect dropdownRect = new Rect(fieldRect.x, fieldRect.y, Mathf.Max(40f, fieldRect.width - addButtonWidth - spacing), fieldRect.height);
            Rect addBtnRect = new Rect(dropdownRect.xMax + spacing, fieldRect.y, addButtonWidth, fieldRect.height);

            // Button text & tint
            Color prevBg = GUI.backgroundColor;
            string buttonText;

            if (hasMultipleValues)
            {
                GUI.backgroundColor = new Color(0.95f, 0.9f, 0.7f);
                buttonText = "— (Mixed Values)";
            }
            else if (!isAssigned)
            {
                GUI.backgroundColor = new Color(1f, 0.88f, 0.45f);
                buttonText = $"Select ({typeName})";
            }
            else if (isMissing)
            {
                GUI.backgroundColor = new Color(1f, 0.45f, 0.45f);
                buttonText = $"❌ Missing: {currentPath}";
            }
            else if (isTypeMismatch)
            {
                GUI.backgroundColor = new Color(1f, 0.55f, 0.3f);
                buttonText = $"⚠️ Mismatch: {ownerGroup.GetFullChannelName(resolvedEntry)}";
            }
            else
            {
                GUI.backgroundColor = new Color(0.75f, 0.95f, 0.82f);
                string badge = resolvedEntry.isSaveable ? " 💾" : string.Empty;
                buttonText = $"{ownerGroup.GroupName} / {resolvedEntry.eventName}{badge}";

                // Show live Blackboard value in Play Mode
                if (Application.isPlaying && EventBoard.TryGetDiagnosticSlot(resolvedEntry.RuntimeId, out IEventChannelDiagnostic liveSlot) && liveSlot.HasValue)
                {
                    buttonText += $"  [= {liveSlot.GetFormattedValue()}]";
                }
            }

            if (GUI.Button(dropdownRect, new GUIContent(buttonText, tooltip), EditorStyles.popup))
            {
                SerializedObject targetSO = property.serializedObject;
                string propPath = property.propertyPath;
                IReadOnlyList<EventBoardGroup> refreshedGroups = EventBoardGroup.GetAllGroups(forceRefresh: true);

                EventKeyDropdown dropdown = new EventKeyDropdown(
                    new AdvancedDropdownState(),
                    refreshedGroups,
                    payloadType,
                    (selectedGuid, selectedFullName) =>
                    {
                        targetSO.Update();
                        SerializedProperty rootProp = targetSO.FindProperty(propPath);
                        if (rootProp != null)
                        {
                            SerializedProperty gProp = rootProp.FindPropertyRelative("guid");
                            SerializedProperty pProp = rootProp.FindPropertyRelative("cachedPath");
                            if (gProp != null) gProp.stringValue = selectedGuid;
                            if (pProp != null) pProp.stringValue = selectedFullName;
                            targetSO.ApplyModifiedProperties();
                        }
                    });

                dropdown.Show(dropdownRect);
            }

            GUI.backgroundColor = prevBg;

            // [+] Quick-create event button
            if (GUI.Button(addBtnRect, new GUIContent("+", $"Define a new '{typeName}' event in an EventBoardGroup SO and assign it here")))
            {
                SerializedObject targetSO = property.serializedObject;
                string propPath = property.propertyPath;
                IReadOnlyList<EventBoardGroup> refreshedGroups = EventBoardGroup.GetAllGroups(forceRefresh: true);

                PopupWindow.Show(
                    addBtnRect,
                    new QuickCreatePopup(refreshedGroups, payloadType, ownerGroup, (createdGuid, createdFullName) =>
                    {
                        targetSO.Update();
                        SerializedProperty rootProp = targetSO.FindProperty(propPath);
                        if (rootProp != null)
                        {
                            SerializedProperty gProp = rootProp.FindPropertyRelative("guid");
                            SerializedProperty pProp = rootProp.FindPropertyRelative("cachedPath");
                            if (gProp != null) gProp.stringValue = createdGuid;
                            if (pProp != null) pProp.stringValue = createdFullName;
                            targetSO.ApplyModifiedProperties();
                        }
                    }));
            }

            EditorGUI.EndProperty();
        }

        public static Type ExtractPayloadType(Type fieldType)
        {
            if (fieldType == null)
                return typeof(VoidSignal);

            if (fieldType.IsArray)
            {
                fieldType = fieldType.GetElementType();
            }
            else if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(List<>))
            {
                fieldType = fieldType.GetGenericArguments()[0];
            }

            while (fieldType != null)
            {
                if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(EventKey<>))
                {
                    return fieldType.GetGenericArguments()[0];
                }
                fieldType = fieldType.BaseType;
            }

            return typeof(VoidSignal);
        }

        public static string GetFriendlyTypeName(Type type)
        {
            if (type == null) return "void";
            if (type == typeof(VoidSignal)) return "void";
            if (type == typeof(int)) return "int";
            if (type == typeof(float)) return "float";
            if (type == typeof(bool)) return "bool";
            if (type == typeof(string)) return "string";
            if (type == typeof(Vector2)) return "Vector2";
            if (type == typeof(Vector3)) return "Vector3";
            if (type == typeof(Color)) return "Color";
            return type.Name;
        }
    }
}
