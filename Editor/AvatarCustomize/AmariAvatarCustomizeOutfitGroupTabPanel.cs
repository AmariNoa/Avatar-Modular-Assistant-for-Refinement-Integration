using System.Collections.Generic;
using UnityEditor;
using com.amari_noa.avatar_modular_assistant.runtime;
using UnityEngine;
using UnityEngine.UIElements;

// ReSharper disable once CheckNamespace
namespace com.amari_noa.avatar_modular_assistant.editor
{
    public partial class AmariAvatarCustomizeWindow
    {
        private void BuildOutfitGroupTabPanel(VisualElement root)
        {
            var outfitTabScrollView = root.Q<ScrollView>("OutfitGroupTabListView");
            var outfitTabItemAddButton = root.Q<Button>("NewOutfitTabGroupButton");

            if (outfitTabScrollView == null || outfitTabItemAddButton == null || _avatarSettings == null)
            {
                return;
            }

            SetupTabScrollView(outfitTabScrollView);
            RefreshOutfitGroupTabs(outfitTabScrollView);

            outfitTabItemAddButton.clicked += () =>
            {
                AddOutfitGroup(outfitTabScrollView);
            };
        }

        private static void SetupTabScrollView(ScrollView scrollView)
        {
            scrollView.mode = ScrollViewMode.Horizontal;
            scrollView.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            scrollView.horizontalScrollerVisibility = ScrollerVisibility.Auto;
            scrollView.contentContainer.style.flexDirection = FlexDirection.Row;
            scrollView.contentContainer.style.alignItems = Align.Center;
        }

        private void RefreshOutfitGroupTabs(ScrollView tabScrollView)
        {
            if (_avatarSettings?.OutfitListGroupItems == null || tabScrollView == null)
            {
                return;
            }

            tabScrollView.contentContainer.Clear();

            for (var index = 0; index < _avatarSettings.OutfitListGroupItems.Count; index++)
            {
                var group = _avatarSettings.OutfitListGroupItems[index];
                if (group == null)
                {
                    group = new AmariOutfitGroupListItem
                    {
                        groupName = GetUnusedOutfitGroupName(),
                        outfitListItems = new List<AmariOutfitListItem>(),
                        scaleMultiply = 1f
                    };
                    _avatarSettings.OutfitListGroupItems[index] = group;
                    MarkSettingsDirty();
                }

                if (string.IsNullOrWhiteSpace(group.groupName))
                {
                    group.groupName = GetUnusedOutfitGroupName();
                    MarkSettingsDirty();
                }

                var tabElement = outfitGroupTabItemAsset.Instantiate();
                var nameButton = tabElement.Q<Button>("OutfitGroupNameTabButton");
                if (nameButton != null)
                {
                    nameButton.text = group.groupName;
                    nameButton.tooltip = group.groupName;
                }

                var removeButton = tabElement.Q<Button>("OutfitGroupRemoveButton");
                if (removeButton != null)
                {
                    var state = GetOrCreateOutfitGroupElementState(removeButton);
                    state.group = group;
                    if (!state.bound)
                    {
                        state.bound = true;
                        removeButton.clicked += () =>
                        {
                            if (removeButton.userData is not OutfitGroupElementState s || s.group == null)
                            {
                                return;
                            }

                            RemoveOutfitGroup(s.group, tabScrollView);
                        };
                    }
                }

                RegisterTabMoveButtons(tabElement, group, tabScrollView);

                tabScrollView.contentContainer.Add(tabElement);
            }
        }

        private void AddOutfitGroup(ScrollView tabScrollView)
        {
            if (_avatarSettings == null)
            {
                return;
            }

            RecordSettingsUndo("Add Outfit Group");

            var newGroup = new AmariOutfitGroupListItem
            {
                groupName = GetUnusedOutfitGroupName(),
                outfitListItems = new List<AmariOutfitListItem>(),
                scaleMultiply = 1f
            };

            _avatarSettings.OutfitListGroupItems.Add(newGroup);
            MarkSettingsDirty();
            RefreshOutfitGroupTabs(tabScrollView);
        }

        private void RemoveOutfitGroup(AmariOutfitGroupListItem group, ScrollView tabScrollView)
        {
            if (_avatarSettings?.OutfitListGroupItems == null || group == null)
            {
                return;
            }

            var index = _avatarSettings.OutfitListGroupItems.IndexOf(group);
            if (index < 0)
            {
                return;
            }

            RecordSettingsUndo("Remove Outfit Group");

            if (group.outfitListItems != null)
            {
                foreach (var item in group.outfitListItems)
                {
                    if (item?.instance)
                    {
                        Undo.DestroyObjectImmediate(item.instance);
                    }

                    if (_avatarSettings.activePreviewOutfit == item)
                    {
                        OnActivePreviewOutfitDestroy(item, true, "Remove Outfit Group");
                    }
                }
            }

            _avatarSettings.OutfitListGroupItems.RemoveAt(index);
            MarkSettingsDirty();
            RefreshOutfitGroupTabs(tabScrollView);
        }

        private void RegisterTabMoveButtons(VisualElement tabElement, AmariOutfitGroupListItem group, ScrollView tabScrollView)
        {
            if (tabElement == null)
            {
                return;
            }

            void Wire(Button btn, int direction)
            {
                if (btn == null)
                {
                    return;
                }

                btn.clicked += () =>
                {
                    MoveOutfitGroup(group, direction);
                    RefreshOutfitGroupTabs(tabScrollView);
                };
            }

            Wire(tabElement.Q<Button>("LeftButton"), -1);
            Wire(tabElement.Q<Button>("RightButton"), 1);
        }

        private void MoveOutfitGroup(AmariOutfitGroupListItem group, int direction)
        {
            if (_avatarSettings?.OutfitListGroupItems == null || group == null)
            {
                return;
            }

            var list = _avatarSettings.OutfitListGroupItems;
            var fromIndex = list.IndexOf(group);
            if (fromIndex < 0)
            {
                return;
            }

            var toIndex = Mathf.Clamp(fromIndex + direction, 0, list.Count - 1);
            if (fromIndex == toIndex)
            {
                return;
            }

            RecordSettingsUndo("Reorder Outfit Groups");

            (list[fromIndex], list[toIndex]) = (list[toIndex], list[fromIndex]);
            MarkSettingsDirty();
        }
    }
}
