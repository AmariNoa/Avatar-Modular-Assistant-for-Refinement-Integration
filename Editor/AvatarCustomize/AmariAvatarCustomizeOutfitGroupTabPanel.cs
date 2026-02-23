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
        private const string TabDragDataKey = "AMARI_OutfitGroupTabIndex";

        private void BuildOutfitGroupTabPanel(VisualElement root)
        {
            var outfitTabScrollView = root.Q<ScrollView>("OutfitGroupTabListView");
            var outfitTabItemAddButton = root.Q<Button>("NewOutfitTabGroupButton");

            if (outfitTabScrollView == null || outfitTabItemAddButton == null || _avatarSettings == null)
            {
                return;
            }

            SetupTabScrollView(outfitTabScrollView);
            RegisterTabDropTarget(outfitTabScrollView);
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

                RegisterTabDragEvents(tabElement, index, tabScrollView);

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

        private void RegisterTabDragEvents(VisualElement tabElement, int index, ScrollView tabScrollView)
        {
            if (tabElement == null)
            {
                return;
            }

            tabElement.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != (int)MouseButton.LeftMouse)
                {
                    return;
                }

                // avoid starting drag from remove button
                if (evt.target is VisualElement ve && ve.name == "OutfitGroupRemoveButton")
                {
                    return;
                }

                DragAndDrop.PrepareStartDrag();
                DragAndDrop.SetGenericData(TabDragDataKey, index);
                DragAndDrop.StartDrag("Outfit Group Tab");
                DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                evt.StopImmediatePropagation();
            });
        }

        private void RegisterTabDropTarget(ScrollView tabScrollView)
        {
            var content = tabScrollView.contentContainer;
            content.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                if (DragAndDrop.GetGenericData(TabDragDataKey) is not int)
                {
                    return;
                }

                DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                evt.StopPropagation();
            });

            content.RegisterCallback<DragPerformEvent>(evt =>
            {
                if (DragAndDrop.GetGenericData(TabDragDataKey) is not int fromIndex)
                {
                    return;
                }

                var toIndex = GetTabDropIndex(tabScrollView, evt.mousePosition);
                MoveOutfitGroup(fromIndex, toIndex);
                DragAndDrop.AcceptDrag();
                RefreshOutfitGroupTabs(tabScrollView);
                evt.StopPropagation();
            });
        }

        private int GetTabDropIndex(ScrollView tabScrollView, Vector2 mousePosition)
        {
            var content = tabScrollView.contentContainer;
            for (var i = 0; i < content.childCount; i++)
            {
                var child = content[i];
                if (mousePosition.x < child.worldBound.center.x)
                {
                    return i;
                }
            }

            return content.childCount;
        }

        private void MoveOutfitGroup(int fromIndex, int toIndex)
        {
            if (_avatarSettings?.OutfitListGroupItems == null)
            {
                return;
            }

            var list = _avatarSettings.OutfitListGroupItems;
            if (fromIndex < 0 || fromIndex >= list.Count)
            {
                return;
            }

            toIndex = Mathf.Clamp(toIndex, 0, list.Count);
            if (fromIndex == toIndex || fromIndex == toIndex - 1)
            {
                return;
            }

            RecordSettingsUndo("Reorder Outfit Groups");

            var item = list[fromIndex];
            list.RemoveAt(fromIndex);
            if (toIndex > fromIndex)
            {
                toIndex--;
            }

            list.Insert(toIndex, item);
            MarkSettingsDirty();
        }
    }
}
