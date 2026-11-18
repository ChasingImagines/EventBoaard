using System.Collections.Generic;
using UnityEngine;

namespace Yasin.EventBoard
{
    /// <summary>
    /// Belirlenen klasördeki tüm EventBoardGroup ScriptableObject varlıklarını toplayıp
    /// çalışma zamanında (Runtime) içlerindeki olay listelerini merkezi EventBoard motoruna yükleyen yönetici.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class EventBoardManager : MonoBehaviour
    {
        private static EventBoardManager _instance;

        [Header("EventBoard Grup SO Kaynakları")]
        [Tooltip("Resources altında EventBoardGroup SO varlıklarının toplandığı klasör yolu.")]
        [SerializeField] private string resourcesFolder = EventBoardGroup.DefaultResourcesFolder;

        [Tooltip("Bu yönetici tarafından bünyesine toplanan ve runtime'da EventBoard'a yüklenen tüm Grup SO listesi.")]
        [SerializeField] private List<EventBoardGroup> eventGroups = new List<EventBoardGroup>();

        [Tooltip("İşaretlenirse sahne geçişlerinde bu yönetici objesi yok edilmez.")]
        [SerializeField] private bool dontDestroyOnLoad = true;

        public static EventBoardManager Instance => _instance;

        public IReadOnlyList<EventBoardGroup> EventGroups => eventGroups;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            if (dontDestroyOnLoad && transform.parent == null)
            {
                DontDestroyOnLoad(gameObject);
            }

            CollectAndRegisterAllGroups();
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            SyncGroupsFromFolderInEditor();
        }
#endif

        /// <summary>
        /// Klasördeki ve listedeki tüm EventBoardGroup SO'larını bünyesinde toplar ve EventBoard'a kaydeder.
        /// </summary>
        [ContextMenu("Klasördeki Tüm Event Grup SO'larını Topla")]
        public void CollectAndRegisterAllGroups()
        {
            if (eventGroups == null)
            {
                eventGroups = new List<EventBoardGroup>();
            }

            IReadOnlyList<EventBoardGroup> discovered = EventBoardGroup.GetAllGroups(resourcesFolder, forceRefresh: true);
            for (int i = 0; i < discovered.Count; i++)
            {
                EventBoardGroup group = discovered[i];
                if (group != null && !eventGroups.Contains(group))
                {
                    eventGroups.Add(group);
                }
            }

            for (int i = eventGroups.Count - 1; i >= 0; i--)
            {
                if (eventGroups[i] == null)
                {
                    eventGroups.RemoveAt(i);
                }
            }

            EventBoard.HydrateFromGroups(eventGroups);
        }

#if UNITY_EDITOR
        public void SyncGroupsFromFolderInEditor()
        {
            if (eventGroups == null)
            {
                eventGroups = new List<EventBoardGroup>();
            }

            IReadOnlyList<EventBoardGroup> discovered = EventBoardGroup.GetAllGroups(resourcesFolder, forceRefresh: true);
            for (int i = 0; i < discovered.Count; i++)
            {
                EventBoardGroup group = discovered[i];
                if (group != null && !eventGroups.Contains(group))
                {
                    eventGroups.Add(group);
                }
            }

            for (int i = eventGroups.Count - 1; i >= 0; i--)
            {
                if (eventGroups[i] == null)
                {
                    eventGroups.RemoveAt(i);
                }
            }
        }
#endif
    }
}

