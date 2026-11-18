using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Yasin.EventBoard.Editor
{
    /// <summary>
    /// EventKey&lt;T&gt; alanlarını Inspector'da otomatik tip algılama, GUID tabanlı kırılmaz bağlama,
    /// Play Mode canlı Blackboard değer gösterimi ve seçilen EventBoardGroup SO içine hızlı [+] olay ekleme ile çizen PropertyDrawer.
    /// Kod içinde hiçbir sabit kanal adı (magic string) barındırmaz.
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

            private int _selectedGroupIndex;
            private bool _createNewGroupSO;
            private string _newGroupSOName = "NewEvents";
            private string _newEventName = "NewEvent";
            private string _description = string.Empty;
            private bool _isSaveable;
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

            public override Vector2 GetWindowSize() => new Vector2(340f, 195f);

            public override void OnGUI(Rect rect)
            {
                GUILayout.Space(6f);
                EditorGUILayout.LabelField($"Yeni Event ({GetFriendlyTypeName(_payloadType)})", EditorStyles.boldLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (!_createNewGroupSO && _groupNames.Length > 0)
                    {
                        _selectedGroupIndex = EditorGUILayout.Popup("Grup", _selectedGroupIndex, _groupNames);
                        if (GUILayout.Button("+ Grup", EditorStyles.miniButton, GUILayout.Width(60f)))
                        {
                            _createNewGroupSO = true;
                        }
                    }
                    else
                    {
                        _newGroupSOName = EditorGUILayout.TextField("Yeni Grup", _newGroupSOName);
                        if (_groupNames.Length > 0 && GUILayout.Button("Seç", EditorStyles.miniButton, GUILayout.Width(50f)))
                        {
                            _createNewGroupSO = false;
                        }
                    }
                }

                GUI.SetNextControlName("QuickEventNameField");
                _newEventName = EditorGUILayout.TextField("Ad", _newEventName);
                _isSaveable = EditorGUILayout.Toggle("💾 Kalıcı", _isSaveable);
                _description = EditorGUILayout.TextField("Not", _description);

                if (!_focused)
                {
                    EditorGUI.FocusTextInControl("QuickEventNameField");
                    _focused = true;
                }

                GUILayout.FlexibleSpace();

                bool enterPressed = Event.current.type == EventType.KeyDown &&
                                    (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("İptal", GUILayout.Height(24f)))
                    {
                        editorWindow.Close();
                    }

                    GUI.backgroundColor = new Color(0.35f, 0.85f, 0.45f);
                    if (GUILayout.Button("Ekle", GUILayout.Height(24f)) || enterPressed)
                    {
                        CreateAndAssign();
                    }
                    GUI.backgroundColor = Color.white;
                }
                GUILayout.Space(4f);
            }

            private void CreateAndAssign()
            {
                string cleanEventName = EventBoardGroup.CleanEventName(_newEventName);
                if (string.IsNullOrEmpty(cleanEventName))
                    return;

                EventBoardGroup targetGroup = null;
                if (_createNewGroupSO || _groups.Count == 0)
                {
                    targetGroup = CreateGroupAssetInResources(_newGroupSOName);
                }
                else if (_selectedGroupIndex >= 0 && _selectedGroupIndex < _groups.Count)
                {
                    targetGroup = _groups[_selectedGroupIndex];
                }

                if (targetGroup == null)
                    return;

                Undo.RecordObject(targetGroup, "Add Event To Group SO");
                EventPayloadDefinition payload = EventBoardGroup.CreatePayloadForType(_payloadType);
                EventBoardGroup.EventEntry entry = targetGroup.AddEntry(cleanEventName, payload, _isSaveable, _description);
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

            IReadOnlyList<EventBoardGroup> groups = EventBoardGroup.GetAllGroups();

            string currentGuid = guidProp != null ? guidProp.stringValue : string.Empty;
            string currentPath = pathProp != null ? pathProp.stringValue : string.Empty;

            EventBoardGroup ownerGroup = null;
            EventBoardGroup.EventEntry resolvedEntry = null;
            bool isAssigned = !string.IsNullOrEmpty(currentGuid);
            bool isMissing = false;
            bool isTypeMismatch = false;

            if (isAssigned)
            {
                if (EventBoardGroup.TryFindByGuid(currentGuid, out ownerGroup, out resolvedEntry))
                {
                    string fullDisplay = ownerGroup.GetFullChannelName(resolvedEntry);
                    if (pathProp != null && !string.Equals(currentPath, fullDisplay, StringComparison.Ordinal))
                    {
                        pathProp.stringValue = fullDisplay;
                        currentPath = fullDisplay;
                    }
                }
                else
                {
                    isMissing = true;
                }

                if (resolvedEntry != null && payloadType != null && resolvedEntry.PayloadType != payloadType)
                {
                    isTypeMismatch = true;
                }
            }

            // Etiket ve İpucu (Tooltip)
            string tooltip = $"Tip: EventKey<{typeName}>";
            if (resolvedEntry != null && ownerGroup != null)
            {
                tooltip += $"\nGrup SO: {ownerGroup.GroupName}\nEvent: {resolvedEntry.eventName}\nKaydedilebilir (Saveable): {(resolvedEntry.isSaveable ? "Evet 💾" : "Hayır")}";
                if (!string.IsNullOrEmpty(resolvedEntry.description))
                    tooltip += $"\nAçıklama: {resolvedEntry.description}";
            }

            GUIContent richLabel = new GUIContent(label.text, tooltip);
            Rect fieldRect = EditorGUI.PrefixLabel(position, richLabel);

            const float addButtonWidth = 24f;
            const float spacing = 3f;
            Rect dropdownRect = new Rect(fieldRect.x, fieldRect.y, Mathf.Max(40f, fieldRect.width - addButtonWidth - spacing), fieldRect.height);
            Rect addBtnRect = new Rect(dropdownRect.xMax + spacing, fieldRect.y, addButtonWidth, fieldRect.height);

            // Buton metni ve rengi
            Color prevBg = GUI.backgroundColor;
            string buttonText;

            if (!isAssigned)
            {
                GUI.backgroundColor = new Color(1f, 0.88f, 0.45f);
                buttonText = $"Seç ({typeName})";
            }
            else if (isMissing)
            {
                GUI.backgroundColor = new Color(1f, 0.45f, 0.45f);
                buttonText = $"❌ Eksik: {currentPath}";
            }
            else if (isTypeMismatch)
            {
                GUI.backgroundColor = new Color(1f, 0.55f, 0.3f);
                buttonText = $"⚠️ Uyumsuz: {ownerGroup.GetFullChannelName(resolvedEntry)}";
            }
            else
            {
                GUI.backgroundColor = new Color(0.75f, 0.95f, 0.82f);
                string badge = resolvedEntry.isSaveable ? " 💾" : string.Empty;
                buttonText = $"{ownerGroup.GroupName} / {resolvedEntry.eventName}{badge}";

                // Play Mode'da canlı Blackboard değerini göster
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

            // [+] Seçilen Grup SO'ya Yeni Event Ekleme Butonu
            if (GUI.Button(addBtnRect, new GUIContent("+", $"Bir EventBoardGroup SO içine yeni '{typeName}' olayı tanımla ve buraya bağla")))
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

        public static EventBoardGroup CreateGroupAssetInResources(string groupAssetName, bool ping = false)
        {
            string cleanName = EventBoardGroup.CleanEventName(groupAssetName);
            if (string.IsNullOrEmpty(cleanName))
                cleanName = "NewEvents";

            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }

            string folderPath = $"Assets/Resources/{EventBoardGroup.DefaultResourcesFolder}";
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                AssetDatabase.CreateFolder("Assets/Resources", EventBoardGroup.DefaultResourcesFolder);
            }

            string assetPath = $"{folderPath}/{cleanName}.asset";
            EventBoardGroup existing = AssetDatabase.LoadAssetAtPath<EventBoardGroup>(assetPath);
            if (existing != null)
            {
                if (ping) EditorGUIUtility.PingObject(existing);
                return existing;
            }

            EventBoardGroup created = ScriptableObject.CreateInstance<EventBoardGroup>();
            AssetDatabase.CreateAsset(created, assetPath);
            EditorUtility.SetDirty(created);
            AssetDatabase.SaveAssets();
            EventBoardGroup.InvalidateGroupCache();

            if (ping)
            {
                EditorGUIUtility.PingObject(created);
            }

            return created;
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
