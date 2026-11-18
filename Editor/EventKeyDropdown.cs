using System;
using System.Collections.Generic;
using UnityEditor.IMGUI.Controls;

namespace Yasin.EventBoard.Editor
{
    /// <summary>
    /// Inspector'da EventKey&lt;T&gt; alanları için klasördeki tüm EventBoardGroup SO'larını tarayıp,
    /// yalnızca ilgili &lt;T&gt; veri tipine uyan olayları Grup SO bazında listeleyen AdvancedDropdown menüsü.
    /// </summary>
    public sealed class EventKeyDropdown : AdvancedDropdown
    {
        private sealed class ChannelDropdownItem : AdvancedDropdownItem
        {
            public string Guid { get; }
            public string FullChannelName { get; }
            public bool IsClearOption { get; }

            public ChannelDropdownItem(string displayName, string guid, string fullChannelName, bool isClearOption = false)
                : base(displayName)
            {
                Guid = guid;
                FullChannelName = fullChannelName;
                IsClearOption = isClearOption;
            }
        }

        private readonly IReadOnlyList<EventBoardGroup> _groups;
        private readonly Type _expectedType;
        private readonly Action<string, string> _onSelected;

        public EventKeyDropdown(
            AdvancedDropdownState state,
            IReadOnlyList<EventBoardGroup> groups,
            Type expectedType,
            Action<string, string> onSelected) : base(state)
        {
            _groups = groups;
            _expectedType = expectedType;
            _onSelected = onSelected;
            minimumSize = new UnityEngine.Vector2(310f, 320f);
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            string typeLabel = _expectedType != null ? EventKeyDrawer.GetFriendlyTypeName(_expectedType) : "Tüm Tipler";
            AdvancedDropdownItem root = new AdvancedDropdownItem($"EventBoard Grupları ({typeLabel})");

            // Atamayı sıfırlama seçeneği
            root.AddChild(new ChannelDropdownItem("Temizle (None)", string.Empty, string.Empty, isClearOption: true));

            if (_groups == null || _groups.Count == 0)
            {
                root.AddChild(new AdvancedDropdownItem("(Resources/EventBoard içinde Grup SO bulunamadı)"));
                return root;
            }

            int matchedCount = 0;

            for (int g = 0; g < _groups.Count; g++)
            {
                EventBoardGroup group = _groups[g];
                if (group == null || group.Events == null || group.Events.Count == 0)
                    continue;

                AdvancedDropdownItem groupFolder = null;
                IReadOnlyList<EventBoardGroup.EventEntry> entries = group.Events;

                for (int i = 0; i < entries.Count; i++)
                {
                    EventBoardGroup.EventEntry entry = entries[i];
                    if (entry == null || string.IsNullOrEmpty(entry.eventName))
                        continue;

                    if (_expectedType != null && entry.PayloadType != _expectedType)
                        continue;

                    if (groupFolder == null)
                    {
                        groupFolder = new AdvancedDropdownItem($"📦 {group.GroupName}");
                        root.AddChild(groupFolder);
                    }

                    matchedCount++;
                    string badge = entry.isSaveable ? " 💾" : string.Empty;
                    string displayLabel = $"{entry.eventName}{badge}";
                    groupFolder.AddChild(new ChannelDropdownItem(displayLabel, entry.guid, group.GetFullChannelName(entry)));
                }
            }

            if (matchedCount == 0)
            {
                root.AddChild(new AdvancedDropdownItem($"('{typeLabel}' tipinde kayıtlı kanal bulunamadı — [+] ile Grup SO'ya ekleyin)"));
            }

            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (item is ChannelDropdownItem channelItem)
            {
                if (channelItem.IsClearOption || !string.IsNullOrEmpty(channelItem.Guid))
                {
                    _onSelected?.Invoke(channelItem.Guid, channelItem.FullChannelName);
                }
            }
        }
    }
}
