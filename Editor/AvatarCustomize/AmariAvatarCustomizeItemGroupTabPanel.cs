using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using com.amari_noa.avatar_modular_assistant.runtime;
using com.amari_noa.avatar_modular_assistant.editor.integrations.modular_avatar;
using UnityEngine;
using UnityEngine.UIElements;

// ReSharper disable once CheckNamespace
namespace com.amari_noa.avatar_modular_assistant.editor
{
    public partial class AmariAvatarCustomizeWindow
    {
        private AmariItemGroupListItem _activeItemGroupTab;

        private void BuildItemGroupTabPanel(VisualElement root)
        {
            var itemTabScrollView = root.Q<ScrollView>("ItemGroupTabListView");
            var itemTabItemAddButton = root.Q<Button>("NewItemTabGroupButton");

            if (itemTabScrollView == null || itemTabItemAddButton == null || _avatarSettings == null)
            {
                return;
            }

            SetupTabScrollView(itemTabScrollView);
            RefreshItemGroupTabs(itemTabScrollView, root);

            itemTabItemAddButton.clicked += () =>
            {
                AddItemGroup(itemTabScrollView, root);
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

        private void RefreshItemGroupTabs(ScrollView tabScrollView, VisualElement root)
        {
            if (_avatarSettings?.ItemListGroupItems == null || tabScrollView == null)
            {
                return;
            }

            tabScrollView.contentContainer.Clear();

            if (_activeItemGroupTab != null && !_avatarSettings.ItemListGroupItems.Contains(_activeItemGroupTab))
            {
                _activeItemGroupTab = null;
            }

            if (_activeItemGroupTab == null && _avatarSettings.ItemListGroupItems.Count > 0)
            {
                _activeItemGroupTab = _avatarSettings.ItemListGroupItems[0];
            }

            for (var index = 0; index < _avatarSettings.ItemListGroupItems.Count; index++)
            {
                var group = _avatarSettings.ItemListGroupItems[index];
                if (group == null)
                {
                    group = new AmariItemGroupListItem
                    {
                        groupName = GetUnusedItemGroupName(),
                        itemListItems = new List<AmariItemListItem>(),
                        scaleMultiply = 1f
                    };
                    _avatarSettings.ItemListGroupItems[index] = group;
                    MarkSettingsDirty();
                }

                if (string.IsNullOrWhiteSpace(group.groupName))
                {
                    group.groupName = GetUnusedItemGroupName();
                    MarkSettingsDirty();
                }

                var tabElement = itemGroupTabItemAsset.Instantiate();
                var nameButton = tabElement.Q<Button>("ItemGroupNameTabButton");
                if (nameButton != null)
                {
                    nameButton.text = group.groupName;
                    nameButton.tooltip = group.groupName;
                    var nameState = GetOrCreateItemGroupElementState(nameButton);
                    nameState.group = group;
                    if (!nameState.bound)
                    {
                        nameState.bound = true;
                        nameButton.clicked += () =>
                        {
                            if (nameButton.userData is not ItemGroupElementState s || s.group == null)
                            {
                                return;
                            }

                            if (_activeItemGroupTab == s.group)
                            {
                                return;
                            }

                            SelectItemGroup(s.group, tabScrollView, root);
                        };
                    }

                    var isActive = group == _activeItemGroupTab;
                    nameButton.SetEnabled(!isActive);
                }

                var removeButton = tabElement.Q<Button>("ItemGroupRemoveButton");
                if (removeButton != null)
                {
                    // グループが1つしかない場合は削除できないようにする
                    removeButton.SetEnabled(_avatarSettings.ItemListGroupItems.Count > 1);
                    var state = GetOrCreateItemGroupElementState(removeButton);
                    state.group = group;
                    if (!state.bound)
                    {
                        state.bound = true;
                        removeButton.clicked += () =>
                        {
                            if (removeButton.userData is not ItemGroupElementState s || s.group == null)
                            {
                                return;
                            }

                            RemoveItemGroup(s.group, tabScrollView, root);
                        };
                    }
                }

                RegisterTabMoveButtons(tabElement, group, tabScrollView, root, index);

                tabScrollView.contentContainer.Add(tabElement);
            }

            BindItemGroupPanel(root, _activeItemGroupTab, tabScrollView);
        }

        private void AddItemGroup(ScrollView tabScrollView, VisualElement root)
        {
            if (_avatarSettings == null)
            {
                return;
            }

            RecordSettingsUndo("Add Item Group");

            var newGroup = new AmariItemGroupListItem
            {
                groupName = GetUnusedItemGroupName(),
                itemListItems = new List<AmariItemListItem>(),
                scaleMultiply = 1f
            };

            _avatarSettings.ItemListGroupItems.Add(newGroup);
            MarkSettingsDirty();
            _activeItemGroupTab = newGroup;
            RefreshItemGroupTabs(tabScrollView, root);
        }

        private void RemoveItemGroup(AmariItemGroupListItem group, ScrollView tabScrollView, VisualElement root)
        {
            if (_avatarSettings?.ItemListGroupItems == null || group == null)
            {
                return;
            }

            var index = _avatarSettings.ItemListGroupItems.IndexOf(group);
            if (index < 0)
            {
                return;
            }

            RecordSettingsUndo("Remove Item Group");

            if (group.itemListItems != null)
            {
                foreach (var item in group.itemListItems)
                {
                    if (item?.instance)
                    {
                        Undo.DestroyObjectImmediate(item.instance);
                    }

                    if (_avatarSettings.activePreviewItem == item)
                    {
                        OnActivePreviewItemDestroy(item, true, "Remove Item Group");
                    }
                }
            }

            _avatarSettings.ItemListGroupItems.RemoveAt(index);
            MarkSettingsDirty();
            if (_activeItemGroupTab == group)
            {
                _activeItemGroupTab = _avatarSettings.ItemListGroupItems.Count > 0
                    ? _avatarSettings.ItemListGroupItems[Mathf.Min(index, _avatarSettings.ItemListGroupItems.Count - 1)]
                    : null;
            }
            RefreshItemGroupTabs(tabScrollView, root);
        }

        private void RegisterTabMoveButtons(VisualElement tabElement, AmariItemGroupListItem group, ScrollView tabScrollView, VisualElement root, int index)
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

                btn.SetEnabled(direction < 0
                    ? index > 0
                    : index < _avatarSettings.ItemListGroupItems.Count - 1);

                btn.clicked += () =>
                {
                    MoveItemGroup(group, direction);
                    _activeItemGroupTab = group;
                    RefreshItemGroupTabs(tabScrollView, root);
                };
            }

            Wire(tabElement.Q<Button>("LeftButton"), -1);
            Wire(tabElement.Q<Button>("RightButton"), 1);
        }

        private void MoveItemGroup(AmariItemGroupListItem group, int direction)
        {
            if (_avatarSettings?.ItemListGroupItems == null || group == null)
            {
                return;
            }

            var list = _avatarSettings.ItemListGroupItems;
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

            RecordSettingsUndo("Reorder Item Groups");

            (list[fromIndex], list[toIndex]) = (list[toIndex], list[fromIndex]);
            MarkSettingsDirty();
        }

        private void SelectItemGroup(AmariItemGroupListItem group, ScrollView tabScrollView, VisualElement root)
        {
            if (group == null)
            {
                return;
            }

            _activeItemGroupTab = group;
            RefreshItemGroupTabs(tabScrollView, root);
        }

        private void BindItemGroupPanel(VisualElement root, AmariItemGroupListItem group, ScrollView tabScrollView)
        {
            var itemGroupNameField = root.Q<TextField>("ItemGroupNameField");
            var scaleMultiplyField = root.Q<FloatField>("ScaleMultiply");
            var itemListView = root.Q<ListView>("ItemListView");

            if (group == null)
            {
                ClearItemGroupPanel(itemGroupNameField, scaleMultiplyField, itemListView);
                return;
            }

            group.itemListItems ??= new List<AmariItemListItem>();

            if (itemGroupNameField != null)
            {
                itemGroupNameField.SetValueWithoutNotify(string.IsNullOrWhiteSpace(group.groupName) ? string.Empty : group.groupName);
                var nameState = GetOrCreateItemGroupElementState(itemGroupNameField);
                nameState.group = group;
                if (!nameState.bound)
                {
                    nameState.bound = true;

                    void CommitItemGroupName()
                    {
                        if (itemGroupNameField.userData is not ItemGroupElementState state || state.group == null)
                        {
                            return;
                        }

                        var desired = itemGroupNameField.value?.Trim();
                        if (string.IsNullOrWhiteSpace(desired))
                        {
                            desired = DefaultGroupName;
                        }

                        if (string.Equals(desired, state.group.groupName, System.StringComparison.Ordinal))
                        {
                            return;
                        }

                        RecordSettingsUndo("Change Item Group Name");

                        var uniqueName = GetUnusedItemGroupName(desired);
                        state.group.groupName = uniqueName;
                        if (!string.Equals(uniqueName, itemGroupNameField.value, System.StringComparison.Ordinal))
                        {
                            itemGroupNameField.SetValueWithoutNotify(uniqueName);
                        }

                        MarkSettingsDirty();
                        RefreshItemGroupTabs(tabScrollView, root);
                    }

                    itemGroupNameField.RegisterCallback<FocusOutEvent>(_ => CommitItemGroupName());
                    itemGroupNameField.RegisterCallback<KeyDownEvent>(e =>
                    {
                        if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter)
                        {
                            return;
                        }

                        CommitItemGroupName();
                    });
                }
            }

            if (scaleMultiplyField != null)
            {
                scaleMultiplyField.SetValueWithoutNotify(group.scaleMultiply);
                var scaleState = GetOrCreateItemGroupElementState(scaleMultiplyField);
                scaleState.group = group;
                if (!scaleState.bound)
                {
                    scaleState.bound = true;
                    scaleMultiplyField.RegisterValueChangedCallback(e =>
                    {
                        if (scaleMultiplyField.userData is not ItemGroupElementState state || state.group == null)
                        {
                            return;
                        }

                        RecordSettingsUndo("Change Item Scale");
                        state.group.scaleMultiply = e.newValue;
                        ApplyScaleMultiplyToGroup(state.group, true, "Apply Item Scale");
                        MarkSettingsDirty();
                    });
                }
            }

            BindItemListViewForGroup(itemListView, group);
            SetupLocalizationTextItem(root);
        }
    }
}
