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
        private AmariOutfitGroupListItem _activeOutfitGroupTab;

        private void BuildOutfitGroupTabPanel(VisualElement root)
        {
            var outfitTabScrollView = root.Q<ScrollView>("OutfitGroupTabListView");
            var outfitTabItemAddButton = root.Q<Button>("NewOutfitTabGroupButton");

            if (outfitTabScrollView == null || outfitTabItemAddButton == null || _avatarSettings == null)
            {
                return;
            }

            SetupTabScrollView(outfitTabScrollView);
            RefreshOutfitGroupTabs(outfitTabScrollView, root);

            outfitTabItemAddButton.clicked += () =>
            {
                AddOutfitGroup(outfitTabScrollView, root);
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

        private void RefreshOutfitGroupTabs(ScrollView tabScrollView, VisualElement root)
        {
            if (_avatarSettings?.OutfitListGroupItems == null || tabScrollView == null)
            {
                return;
            }

            tabScrollView.contentContainer.Clear();

            if (_activeOutfitGroupTab != null && !_avatarSettings.OutfitListGroupItems.Contains(_activeOutfitGroupTab))
            {
                _activeOutfitGroupTab = null;
            }

            if (_activeOutfitGroupTab == null && _avatarSettings.OutfitListGroupItems.Count > 0)
            {
                _activeOutfitGroupTab = _avatarSettings.OutfitListGroupItems[0];
            }

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
                    var nameState = GetOrCreateOutfitGroupElementState(nameButton);
                    nameState.group = group;
                    if (!nameState.bound)
                    {
                        nameState.bound = true;
                        nameButton.clicked += () =>
                        {
                            if (nameButton.userData is not OutfitGroupElementState s || s.group == null)
                            {
                                return;
                            }

                            if (_activeOutfitGroupTab == s.group)
                            {
                                return;
                            }

                            SelectOutfitGroup(s.group, tabScrollView, root);
                        };
                    }

                    var isActive = group == _activeOutfitGroupTab;
                    nameButton.SetEnabled(!isActive);
                }

                var removeButton = tabElement.Q<Button>("OutfitGroupRemoveButton");
                if (removeButton != null)
                {
                    // グループが1つしかない場合は削除できないようにする
                    removeButton.SetEnabled(_avatarSettings.OutfitListGroupItems.Count > 1);
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

                            RemoveOutfitGroup(s.group, tabScrollView, root);
                        };
                    }
                }

                RegisterTabMoveButtons(tabElement, group, tabScrollView, root, index);

                tabScrollView.contentContainer.Add(tabElement);
            }

            BindOutfitGroupPanel(root, _activeOutfitGroupTab, tabScrollView);
        }

        private void AddOutfitGroup(ScrollView tabScrollView, VisualElement root)
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
            _activeOutfitGroupTab = newGroup;
            RefreshOutfitGroupTabs(tabScrollView, root);
        }

        private void RemoveOutfitGroup(AmariOutfitGroupListItem group, ScrollView tabScrollView, VisualElement root)
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
            if (_activeOutfitGroupTab == group)
            {
                _activeOutfitGroupTab = _avatarSettings.OutfitListGroupItems.Count > 0
                    ? _avatarSettings.OutfitListGroupItems[Mathf.Min(index, _avatarSettings.OutfitListGroupItems.Count - 1)]
                    : null;
            }
            RefreshOutfitGroupTabs(tabScrollView, root);
        }

        private void RegisterTabMoveButtons(VisualElement tabElement, AmariOutfitGroupListItem group, ScrollView tabScrollView, VisualElement root, int index)
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
                    : index < _avatarSettings.OutfitListGroupItems.Count - 1);

                btn.clicked += () =>
                {
                    MoveOutfitGroup(group, direction);
                    _activeOutfitGroupTab = group;
                    RefreshOutfitGroupTabs(tabScrollView, root);
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

        private void SelectOutfitGroup(AmariOutfitGroupListItem group, ScrollView tabScrollView, VisualElement root)
        {
            if (group == null)
            {
                return;
            }

            _activeOutfitGroupTab = group;
            RefreshOutfitGroupTabs(tabScrollView, root);
        }

        private void BindOutfitGroupPanel(VisualElement root, AmariOutfitGroupListItem group, ScrollView tabScrollView)
        {
            var outfitGroupNameField = root.Q<TextField>("OutfitGroupNameField");
            var scaleMultiplyField = root.Q<FloatField>("ScaleMultiply");
            var outfitListView = root.Q<ListView>("OutfitListView");

            if (group == null)
            {
                ClearOutfitGroupPanel(outfitGroupNameField, scaleMultiplyField, outfitListView);
                return;
            }

            group.outfitListItems ??= new List<AmariOutfitListItem>();

            if (outfitGroupNameField != null)
            {
                outfitGroupNameField.SetValueWithoutNotify(string.IsNullOrWhiteSpace(group.groupName) ? string.Empty : group.groupName);
                var nameState = GetOrCreateOutfitGroupElementState(outfitGroupNameField);
                nameState.group = group;
                if (!nameState.bound)
                {
                    nameState.bound = true;

                    void CommitOutfitGroupName()
                    {
                        if (outfitGroupNameField.userData is not OutfitGroupElementState state || state.group == null)
                        {
                            return;
                        }

                        var desired = outfitGroupNameField.value?.Trim();
                        if (string.IsNullOrWhiteSpace(desired))
                        {
                            desired = DefaultGroupName;
                        }

                        if (string.Equals(desired, state.group.groupName, System.StringComparison.Ordinal))
                        {
                            return;
                        }

                        RecordSettingsUndo("Change Outfit Group Name");

                        var uniqueName = GetUnusedOutfitGroupName(desired);
                        state.group.groupName = uniqueName;
                        if (!string.Equals(uniqueName, outfitGroupNameField.value, System.StringComparison.Ordinal))
                        {
                            outfitGroupNameField.SetValueWithoutNotify(uniqueName);
                        }

                        MarkSettingsDirty();
                        RefreshOutfitGroupTabs(tabScrollView, root);
                    }

                    outfitGroupNameField.RegisterCallback<FocusOutEvent>(_ => CommitOutfitGroupName());
                    outfitGroupNameField.RegisterCallback<KeyDownEvent>(e =>
                    {
                        if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter)
                        {
                            return;
                        }

                        CommitOutfitGroupName();
                    });
                }
            }

            if (scaleMultiplyField != null)
            {
                scaleMultiplyField.SetValueWithoutNotify(group.scaleMultiply);
                var scaleState = GetOrCreateOutfitGroupElementState(scaleMultiplyField);
                scaleState.group = group;
                if (!scaleState.bound)
                {
                    scaleState.bound = true;
                    scaleMultiplyField.RegisterValueChangedCallback(e =>
                    {
                        if (scaleMultiplyField.userData is not OutfitGroupElementState state || state.group == null)
                        {
                            return;
                        }

                        RecordSettingsUndo("Change Outfit Scale");
                        state.group.scaleMultiply = e.newValue;
                        ApplyScaleMultiplyToGroup(state.group, true, "Apply Outfit Scale");
                        MarkSettingsDirty();
                    });
                }
            }

            BindOutfitListViewForGroup(outfitListView, group);
            SetupLocalizationTextOutfit(root);
        }
    }
}
