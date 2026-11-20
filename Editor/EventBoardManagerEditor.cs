using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Yasin.EventBoard.Editor
{
    /// <summary>
    /// EventBoardManager için tek tıkla projedeki tüm EventBoardGroup SO'larını bulup listeye ekleyen
    /// ve referans durumunu görsel olarak gösteren özel Editor arayüzü.
    /// </summary>
    [CustomEditor(typeof(EventBoardManager))]
    public sealed class EventBoardManagerEditor : UnityEditor.Editor
    {
        public override void OnGUI()
        {
            serializedObject.Update();

            EditorGUILayout.HelpBox(
                "EventBoardGroup SO varlıkları projenin herhangi bir klasöründe bulunabilir. " +
                "Player Build ortamında grupların eksiksiz çalışması için aşağıdaki listede doğrudan referanslarının olması tavsiye edilir.",
                MessageType.Info);

            EditorGUILayout.Space(4);

            if (GUILayout.Button("🔍 Projedeki Tüm EventBoardGroup SO'larını Bul ve Ekle", GUILayout.Height(30)))
            {
                EventBoardManager manager = (EventBoardManager)target;
                Undo.RecordObject(manager, "Sync Event Groups");
                manager.CollectAndRegisterAllGroups();
                EditorUtility.SetDirty(manager);
            }

            EditorGUILayout.Space(6);
            DrawDefaultInspector();

            serializedObject.ApplyModifiedProperties();
        }
    }

    /// <summary>
    /// Projede yeni bir EventBoardGroup oluşturulduğunda, silindiğinde veya taşındığında
    /// grup önbelleğini temizleyen ve EventBoard'ı güncel tutan otomatik dinleyici.
    /// </summary>
    public sealed class EventBoardAssetPostprocessor : AssetPostprocessor
    {
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
                    if (importedAssets[i].EndsWith(".asset", System.StringComparison.OrdinalIgnoreCase))
                    {
                        var group = AssetDatabase.LoadAssetAtPath<EventBoardGroup>(importedAssets[i]);
                        if (group != null)
                        {
                            affected = true;
                            break;
                        }
                    }
                }
            }

            if (!affected && deletedAssets != null)
            {
                for (int i = 0; i < deletedAssets.Length; i++)
                {
                    if (deletedAssets[i].EndsWith(".asset", System.StringComparison.OrdinalIgnoreCase))
                    {
                        affected = true;
                        break;
                    }
                }
            }

            if (affected)
            {
                EventBoardGroup.InvalidateGroupCache();

                // Sahnedeki EventBoardManager varsa senkronize et
#if UNITY_2023_1_OR_NEWER
                var manager = Object.FindFirstObjectByType<EventBoardManager>();
#else
                var manager = Object.FindObjectOfType<EventBoardManager>();
#endif
                if (manager != null)
                {
                    manager.SyncGroupsFromFolderInEditor();
                }
            }
        }
    }
}
