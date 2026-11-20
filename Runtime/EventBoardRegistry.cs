using System;
using System.Collections.Generic;
using UnityEngine;

namespace Yasin.EventBoard
{
    /// <summary>
    /// Projedeki tüm EventBoardGroup ScriptableObject varlıklarını merkezi olarak tutan
    /// ve Unity'nin PreloadedAssets mekanizmasıyla oyun açılışında otomatik yüklenen proje kaydı.
    /// </summary>
    [CreateAssetMenu(fileName = "EventBoardRegistry", menuName = "EventBoard/Registry (Project Singleton)", order = 0)]
    public sealed class EventBoardRegistry : ScriptableObject
    {
        public const string DefaultRegistryAssetPath = "Assets/EventBoard/EventBoardRegistry.asset";
        public const string DefaultGroupFolderPath = "Assets/EventBoard/Groups";

        [Tooltip("Hub veya Quick-Create üzerinden yeni bir Grup SO oluşturulduğunda kaydedilecek varsayılan klasör yolu.")]
        [SerializeField] private string defaultGroupFolder = DefaultGroupFolderPath;

        [Tooltip("Build ortamında otomatik yüklenecek tüm EventBoardGroup varlıkları. Editör tarafından otomatik senkronize edilir.")]
        [SerializeField] private List<EventBoardGroup> groups = new List<EventBoardGroup>();

        private static EventBoardRegistry _active;

        public static EventBoardRegistry Active => _active;

        public IReadOnlyList<EventBoardGroup> Groups => groups;

        public string DefaultGroupFolder => string.IsNullOrWhiteSpace(defaultGroupFolder)
            ? DefaultGroupFolderPath
            : defaultGroupFolder.Trim().TrimEnd('/');

        private void OnEnable()
        {
            _active = this;
            EventBoard.SetRegistry(this);
        }

        private void OnDestroy()
        {
            if (_active == this)
            {
                _active = null;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (groups == null)
            {
                groups = new List<EventBoardGroup>();
            }
            else
            {
                for (int i = groups.Count - 1; i >= 0; i--)
                {
                    if (groups[i] == null)
                    {
                        groups.RemoveAt(i);
                    }
                }
            }

            _active = this;
            EventBoard.SetRegistry(this);
        }

        public bool SyncGroups(IReadOnlyList<EventBoardGroup> discovered)
        {
            if (groups == null)
            {
                groups = new List<EventBoardGroup>();
            }

            List<EventBoardGroup> normalized = new List<EventBoardGroup>(discovered != null ? discovered.Count : 0);
            HashSet<EventBoardGroup> seen = new HashSet<EventBoardGroup>();

            if (discovered != null)
            {
                for (int i = 0; i < discovered.Count; i++)
                {
                    EventBoardGroup g = discovered[i];
                    if (g != null && seen.Add(g))
                    {
                        normalized.Add(g);
                    }
                }
            }

            normalized.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));

            bool changed = groups.Count != normalized.Count;
            if (!changed)
            {
                for (int i = 0; i < normalized.Count; i++)
                {
                    if (groups[i] != normalized[i])
                    {
                        changed = true;
                        break;
                    }
                }
            }

            if (!changed)
                return false;

            groups.Clear();
            groups.AddRange(normalized);
            return true;
        }
#endif
    }
}
