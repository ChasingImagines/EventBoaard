using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Yasin.EventBoard.Editor
{
    /// <summary>
    /// Custom Inspector for EventBoardRegistry showing PreloadedAssets status and one-click project synchronization.
    /// </summary>
    [CustomEditor(typeof(EventBoardRegistry))]
    public sealed class EventBoardRegistryEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EventBoardRegistry registry = (EventBoardRegistry)target;
            bool isPreloaded = EventBoardRegistryUtility.IsInPreloadedAssets(registry);

            if (isPreloaded)
            {
                EditorGUILayout.HelpBox(
                    "✅ This EventBoardRegistry asset is registered in PlayerSettings > Preloaded Assets.\n" +
                    "All EventBoardGroup assets (regardless of folder) will load automatically in Player Builds.",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "⚠️ This Registry is not yet in PlayerSettings > Preloaded Assets. Add it so groups load automatically in Player Builds.",
                    MessageType.Warning);

                if (GUILayout.Button("📌 Add to Preloaded Assets", GUILayout.Height(26f)))
                {
                    EventBoardRegistryUtility.EnsurePreloaded(registry);
                }
            }

            EditorGUILayout.Space(4f);

            if (GUILayout.Button("🔍 Sync All Groups in Project", GUILayout.Height(30f)))
            {
                EventBoardRegistryUtility.SyncAllGroups(createRegistryIfGroupsExist: true, saveAssets: true);
                serializedObject.Update();
            }

            EditorGUILayout.Space(6f);
            DrawDefaultInspector();

            serializedObject.ApplyModifiedProperties();
        }
    }

    /// <summary>
    /// Manages the EventBoardRegistry asset, maintains PreloadedAssets registration, and repairs cross-group GUID collisions.
    /// </summary>
    public static class EventBoardRegistryUtility
    {
        public static EventBoardRegistry FindRegistry()
        {
            UnityEngine.Object[] preloaded = PlayerSettings.GetPreloadedAssets();
            if (preloaded != null)
            {
                for (int i = 0; i < preloaded.Length; i++)
                {
                    if (preloaded[i] is EventBoardRegistry reg && reg != null)
                        return reg;
                }
            }

            string[] guids = AssetDatabase.FindAssets("t:EventBoardRegistry");
            if (guids != null)
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    EventBoardRegistry reg = AssetDatabase.LoadAssetAtPath<EventBoardRegistry>(path);
                    if (reg != null)
                        return reg;
                }
            }

            return null;
        }

        public static bool IsInPreloadedAssets(EventBoardRegistry registry)
        {
            if (registry == null)
                return false;

            UnityEngine.Object[] preloaded = PlayerSettings.GetPreloadedAssets();
            if (preloaded == null)
                return false;

            for (int i = 0; i < preloaded.Length; i++)
            {
                if (preloaded[i] == registry)
                    return true;
            }

            return false;
        }

        public static void EnsurePreloaded(EventBoardRegistry registry)
        {
            if (registry == null)
                return;

            UnityEngine.Object[] current = PlayerSettings.GetPreloadedAssets();
            bool hasRegistry = false;
            bool hasNull = false;

            if (current != null)
            {
                for (int i = 0; i < current.Length; i++)
                {
                    if (current[i] == null)
                    {
                        hasNull = true;
                    }
                    else if (current[i] == registry)
                    {
                        hasRegistry = true;
                    }
                }
            }

            if (hasRegistry && !hasNull)
                return;

            List<UnityEngine.Object> updated = new List<UnityEngine.Object>(current != null ? current.Length + 1 : 1);
            if (current != null)
            {
                for (int i = 0; i < current.Length; i++)
                {
                    if (current[i] != null)
                    {
                        updated.Add(current[i]);
                    }
                }
            }

            if (!hasRegistry)
            {
                updated.Add(registry);
            }

            PlayerSettings.SetPreloadedAssets(updated.ToArray());
        }

        public static void EnsureFolderRecursive(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
                return;

            string normalized = folderPath.Replace('\\', '/').Trim().TrimEnd('/');
            if (AssetDatabase.IsValidFolder(normalized))
                return;

            string[] parts = normalized.Split('/');
            if (parts.Length == 0 || !string.Equals(parts[0], "Assets", StringComparison.Ordinal))
                return;

            string current = "Assets";
            for (int i = 1; i < parts.Length; i++)
            {
                string part = parts[i];
                if (string.IsNullOrEmpty(part))
                    continue;

                string next = $"{current}/{part}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, part);
                }
                current = next;
            }
        }

        public static EventBoardRegistry EnsureRegistryExists()
        {
            EventBoardRegistry existing = FindRegistry();
            if (existing != null)
            {
                EnsurePreloaded(existing);
                EventBoard.SetRegistry(existing);
                return existing;
            }

            string parentFolder = System.IO.Path.GetDirectoryName(EventBoardRegistry.DefaultRegistryAssetPath)?.Replace('\\', '/');
            EnsureFolderRecursive(parentFolder);

            EventBoardRegistry created = ScriptableObject.CreateInstance<EventBoardRegistry>();
            AssetDatabase.CreateAsset(created, EventBoardRegistry.DefaultRegistryAssetPath);
            EditorUtility.SetDirty(created);
            AssetDatabase.SaveAssets();

            EnsurePreloaded(created);
            EventBoard.SetRegistry(created);
            return created;
        }

        public static EventBoardGroup CreateGroupAsset(string groupAssetName, bool ping = false)
        {
            EventBoardRegistry registry = EnsureRegistryExists();

            string cleanName = EventBoardGroup.CleanEventName(groupAssetName);
            if (string.IsNullOrEmpty(cleanName))
                cleanName = "NewEvents";

            string folderPath = registry != null ? registry.DefaultGroupFolder : EventBoardRegistry.DefaultGroupFolderPath;
            if (string.IsNullOrEmpty(folderPath) || !folderPath.StartsWith("Assets", StringComparison.Ordinal))
            {
                folderPath = EventBoardRegistry.DefaultGroupFolderPath;
            }

            EnsureFolderRecursive(folderPath);

            string assetPath = $"{folderPath}/{cleanName}.asset";
            EventBoardGroup existing = AssetDatabase.LoadAssetAtPath<EventBoardGroup>(assetPath);
            if (existing != null)
            {
                SyncAllGroups(createRegistryIfGroupsExist: true, saveAssets: false);
                if (ping) EditorGUIUtility.PingObject(existing);
                return existing;
            }

            EventBoardGroup created = ScriptableObject.CreateInstance<EventBoardGroup>();
            AssetDatabase.CreateAsset(created, assetPath);
            EditorUtility.SetDirty(created);
            EventBoardGroup.InvalidateGroupCache();
            SyncAllGroups(createRegistryIfGroupsExist: true, saveAssets: true);

            if (ping)
            {
                EditorGUIUtility.PingObject(created);
            }

            return created;
        }

        public static void SyncAllGroups(
            bool createRegistryIfGroupsExist = true,
            bool saveAssets = false,
            HashSet<string> newlyImportedPaths = null)
        {
            string[] guids = AssetDatabase.FindAssets("t:EventBoardGroup");
            List<EventBoardGroup> existingPass = new List<EventBoardGroup>();
            List<EventBoardGroup> newPass = new List<EventBoardGroup>();
            List<EventBoardGroup> allGroups = new List<EventBoardGroup>();

            if (guids != null)
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                    EventBoardGroup group = AssetDatabase.LoadAssetAtPath<EventBoardGroup>(assetPath);
                    if (group == null)
                        continue;

                    allGroups.Add(group);
                    if (newlyImportedPaths != null && newlyImportedPaths.Contains(assetPath))
                    {
                        newPass.Add(group);
                    }
                    else
                    {
                        existingPass.Add(group);
                    }
                }
            }

            // Global GUID collision repair (including assets duplicated via Ctrl+D)
            HashSet<string> globalGuids = new HashSet<string>(StringComparer.Ordinal);
            bool anyDirty = RepairDuplicateGuids(existingPass, globalGuids);
            if (RepairDuplicateGuids(newPass, globalGuids))
            {
                anyDirty = true;
            }

            EventBoardRegistry registry = FindRegistry();
            if (registry == null && createRegistryIfGroupsExist && allGroups.Count > 0)
            {
                registry = EnsureRegistryExists();
                anyDirty = true;
            }

            if (registry != null)
            {
                EnsurePreloaded(registry);
                EventBoard.SetRegistry(registry);
                if (registry.SyncGroups(allGroups))
                {
                    EditorUtility.SetDirty(registry);
                    anyDirty = true;
                }
            }

            if (saveAssets && anyDirty)
            {
                AssetDatabase.SaveAssets();
            }
        }

        private static bool RepairDuplicateGuids(List<EventBoardGroup> groupList, HashSet<string> globalGuids)
        {
            bool anyModified = false;
            for (int g = 0; g < groupList.Count; g++)
            {
                EventBoardGroup group = groupList[g];
                if (group == null || group.Events == null)
                    continue;

                bool modified = false;
                IReadOnlyList<EventBoardGroup.EventEntry> entries = group.Events;
                for (int i = 0; i < entries.Count; i++)
                {
                    EventBoardGroup.EventEntry entry = entries[i];
                    if (entry == null)
                        continue;

                    if (string.IsNullOrWhiteSpace(entry.guid) || !globalGuids.Add(entry.guid))
                    {
                        entry.guid = Guid.NewGuid().ToString("N");
                        globalGuids.Add(entry.guid);
                        modified = true;
                    }
                }

                if (modified)
                {
                    anyModified = true;
                    group.RebuildLookups();
                    EditorUtility.SetDirty(group);
                    EventBoard.HydrateFromGroup(group);
                    Debug.Log($"[EventBoard] Automatically regenerated colliding/duplicated event GUIDs inside '{group.name}'.", group);
                }
            }
            return anyModified;
        }
    }

    [InitializeOnLoad]
    internal static class EventBoardEditorBootstrap
    {
        static EventBoardEditorBootstrap()
        {
            EditorApplication.delayCall += RunInitialSync;
        }

        private static void RunInitialSync()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += RunInitialSync;
                return;
            }

            EventBoardRegistryUtility.SyncAllGroups(createRegistryIfGroupsExist: true, saveAssets: false);
        }
    }

    /// <summary>
    /// Automatically invalidates caches and schedules a delayed EventBoardRegistry sync when EventBoardGroup assets are created, duplicated, deleted, or moved.
    /// </summary>
    public sealed class EventBoardAssetPostprocessor : AssetPostprocessor
    {
        private static readonly HashSet<string> _pendingNewGroupPaths = new HashSet<string>(StringComparer.Ordinal);
        private static bool _syncScheduled;

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            bool affected = false;

            if (importedAssets != null)
            {
                for (int i = 0; i < importedAssets.Length; i++)
                {
                    string path = importedAssets[i];
                    if (!path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                        continue;

                    Type assetType = AssetDatabase.GetMainAssetTypeAtPath(path);
                    if (assetType == typeof(EventBoardGroup))
                    {
                        _pendingNewGroupPaths.Add(path);
                        affected = true;
                    }
                    else if (assetType == typeof(EventBoardRegistry))
                    {
                        affected = true;
                    }
                }
            }

            if (!affected && deletedAssets != null)
            {
                for (int i = 0; i < deletedAssets.Length; i++)
                {
                    if (deletedAssets[i].EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                    {
                        affected = true;
                        break;
                    }
                }
            }

            if (!affected && movedAssets != null)
            {
                for (int i = 0; i < movedAssets.Length; i++)
                {
                    if (movedAssets[i].EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                    {
                        Type assetType = AssetDatabase.GetMainAssetTypeAtPath(movedAssets[i]);
                        if (assetType == typeof(EventBoardGroup) || assetType == typeof(EventBoardRegistry))
                        {
                            affected = true;
                            break;
                        }
                    }
                }
            }

            if (affected)
            {
                EventBoardGroup.InvalidateGroupCache();
                if (!_syncScheduled)
                {
                    _syncScheduled = true;
                    EditorApplication.delayCall += FlushPendingSync;
                }
            }
        }

        private static void FlushPendingSync()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += FlushPendingSync;
                return;
            }

            _syncScheduled = false;
            HashSet<string> importedBatch = _pendingNewGroupPaths.Count > 0
                ? new HashSet<string>(_pendingNewGroupPaths, StringComparer.Ordinal)
                : null;
            _pendingNewGroupPaths.Clear();

            EventBoardRegistryUtility.SyncAllGroups(
                createRegistryIfGroupsExist: true,
                saveAssets: false,
                newlyImportedPaths: importedBatch);
        }
    }

    /// <summary>
    /// Ensures all EventBoardGroup assets are synced into EventBoardRegistry and PreloadedAssets before every Player Build.
    /// </summary>
    public sealed class EventBoardBuildPreprocessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => -100;

        public void OnPreprocessBuild(BuildReport report)
        {
            EventBoardRegistryUtility.SyncAllGroups(createRegistryIfGroupsExist: true, saveAssets: true);

            IReadOnlyList<EventBoardGroup> allGroups = EventBoardGroup.GetAllGroups(forceRefresh: true);
            if (allGroups != null && allGroups.Count > 0)
            {
                EventBoardRegistry registry = EventBoardRegistryUtility.FindRegistry();
                if (registry == null || !EventBoardRegistryUtility.IsInPreloadedAssets(registry))
                {
                    throw new BuildFailedException("[EventBoard] Failed to register EventBoardRegistry in PlayerSettings > Preloaded Assets.");
                }
            }
        }
    }
}
