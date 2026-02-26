using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using com.amari_noa.avatar_modular_assistant.runtime;
using com.amari_noa.avatar_modular_assistant.editor.integrations.modular_avatar;
using UnityEngine;
using UnityEngine.UIElements;
using EditorObjectField = UnityEditor.UIElements.ObjectField;

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

            BindOutfitListViewForGroup(outfitListView, group, root);
            SetupLocalizationTextOutfit(root);
        }

        private void ClearOutfitGroupPanel(TextField nameField, FloatField scaleField, ListView listView)
        {
            nameField?.SetValueWithoutNotify(string.Empty);
            scaleField?.SetValueWithoutNotify(1f);
            if (listView != null)
            {
                listView.itemsSource = null;
                listView.makeItem = null;
                listView.bindItem = null;
                listView.Rebuild();
            }
        }

        private void UpdateOutfitCheckResultsForGroup(AmariOutfitGroupListItem group)
        {
            if (group?.outfitListItems == null)
            {
                return;
            }

            foreach (var item in group.outfitListItems)
            {
                if (item != null)
                {
                    _outfitCheckResults.Remove(item);
                }
            }

            if (!AmariModularAvatarIntegration.IsInstalled())
            {
                return;
            }

            var checkResults = AmariModularAvatarIntegration.CheckGroup(group);
            foreach (var item in group.outfitListItems)
            {
                if (item?.instance == null)
                {
                    continue;
                }

                if (checkResults.TryGetValue(item.instance, out var result))
                {
                    _outfitCheckResults[item] = result;
                }
            }
        }

        private void BindOutfitListViewForGroup(ListView outfitListView, AmariOutfitGroupListItem group, VisualElement root)
        {
            if (outfitListView == null || group == null)
            {
                return;
            }

            group.outfitListItems ??= new List<AmariOutfitListItem>();

            var listViewState = GetOrCreateOutfitListViewState(outfitListView);
            if (listViewState.group != null && listViewState.group != group)
            {
                _groupToListView.Remove(listViewState.group);
                _outfitListSnapshots.Remove(outfitListView);
            }

            outfitListView.itemsSource = group.outfitListItems;
            outfitListView.makeItem = () => outfitItemAsset.Instantiate();
            outfitListView.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;
            _listViewToTargetList[outfitListView] = group.outfitListItems;
            UpdateGroupListViewMapping(group, outfitListView);
            listViewState.group = group;
            UpdateOutfitCheckResultsForGroup(group);

            outfitListView.bindItem = (element, index) =>
            {
                if (!_listViewToTargetList.TryGetValue(outfitListView, out var targetList) || targetList == null)
                {
                    return;
                }

                if (index < 0 || index >= targetList.Count)
                {
                    return;
                }

                var item = targetList[index];
                if (item == null)
                {
                    item = new AmariOutfitListItem();
                    targetList[index] = item;
                    MarkSettingsDirty();
                }

                var currentGroup = FindOutfitGroupByList(targetList);

                var prefabField = element.Q<EditorObjectField>("OutfitPrefabField");
                if (prefabField == null)
                {
                    Debug.LogError("PrefabField not found in outfit item UXML");
                    return;
                }

                prefabField.objectType = typeof(GameObject);
                prefabField.allowSceneObjects = false;
                prefabField.SetValueWithoutNotify(item.prefab);
                var prefabState = GetOrCreateOutfitItemElementState(prefabField);
                prefabState.item = item;
                prefabState.group = currentGroup;
                if (!prefabState.bound)
                {
                    prefabState.bound = true;
                    prefabField.RegisterValueChangedCallback(e =>
                    {
                        if (prefabField.userData is not OutfitItemElementState state || state.item == null)
                        {
                            return;
                        }

                        var newPrefab = e.newValue as GameObject;
                        OnOutfitPrefabValueChanged(prefabField, state.item, newPrefab, state.group);
                    });
                }

                var previewButton = element.Q<Button>("OutfitPreviewStatusButton");
                if (previewButton != null)
                {
                    SetPreviewButtonState(previewButton, _avatarSettings.activePreviewOutfit == item);
                    var previewState = GetOrCreateOutfitItemElementState(previewButton);
                    previewState.item = item;
                    previewState.listView = outfitListView;
                    if (!previewState.bound)
                    {
                        previewState.bound = true;
                        previewButton.clicked += () =>
                        {
                            if (previewButton.userData is not OutfitItemElementState state || state.item == null)
                            {
                                return;
                            }

                            if (state.item.instance == null)
                            {
                                return;
                            }

                            RecordSettingsUndo("Change Active Preview Outfit");
                            _avatarSettings.activePreviewOutfit = state.item;
                            UpdatePreviewInstanceActiveStates(true, "Change Active Preview Outfit");
                            MarkSettingsDirty();
                            state.listView?.RefreshItems();
                        };
                    }
                }

                var includeInBuildTitle = element.Q<Label>("IncludeInBuildTitle");
                if (includeInBuildTitle != null)
                {
                    includeInBuildTitle.text = AmariLocalization.Get("amari.window.avatarCustomize.includeInBuildTitle");
                }

                var outfitInfoButton = element.Q<Button>("OutfitInfoButton");
                if (outfitInfoButton != null)
                {
                    var needsAttention = ShouldNotifyOutfitInfo(item);
                    SetOutfitInfoButtonState(outfitInfoButton, needsAttention);
                    BindOutfitInfoButton(outfitInfoButton, item, OnOutfitInfoButtonClicked);
                }

                var includeInBuildToggle = element.Q<Toggle>("IncludeInBuildToggle");
                if (includeInBuildToggle != null)
                {
                    var includeInBuild = item.instance != null && !item.instance.CompareTag("EditorOnly");
                    includeInBuildToggle.SetValueWithoutNotify(includeInBuild);
                    var includeState = GetOrCreateOutfitItemElementState(includeInBuildToggle);
                    includeState.item = item;
                    if (!includeState.bound)
                    {
                        includeState.bound = true;
                        includeInBuildToggle.RegisterValueChangedCallback(e =>
                        {
                            if (includeInBuildToggle.userData is not OutfitItemElementState state || state.item == null)
                            {
                                return;
                            }

                            if (state.item.instance == null)
                            {
                                includeInBuildToggle.SetValueWithoutNotify(false);
                                return;
                            }

                            Undo.RecordObject(state.item.instance, "Toggle Include In Build");
                            state.item.instance.tag = e.newValue ? "Untagged" : "EditorOnly";
                            MarkObjectDirty(state.item.instance);
                        });
                    }
                }
            };

            if (!listViewState.bound)
            {
                listViewState.bound = true;

                outfitListView.itemsRemoved += indices =>
                {
                    if (outfitListView.userData is not OutfitListViewState state || state.group?.outfitListItems == null)
                    {
                        return;
                    }

                    RecordSettingsUndo("Remove Outfit Prefab");
                    if (!_outfitListSnapshots.TryGetValue(outfitListView, out var snapshot))
                    {
                        snapshot = state.group.outfitListItems.ToList();
                    }

                    foreach (var i in indices)
                    {
                        if (i < 0 || i >= snapshot.Count)
                        {
                            continue;
                        }

                        var item = snapshot[i];
                        if (item == null || !item.instance)
                        {
                            continue;
                        }

                        Undo.DestroyObjectImmediate(item.instance);
                        if (_avatarSettings.activePreviewOutfit == item)
                        {
                            OnActivePreviewOutfitDestroy(item, true, "Remove Outfit Prefab");
                        }
                    }

                    MarkSettingsDirty();
                    _outfitListSnapshots[outfitListView] = state.group.outfitListItems.ToList();
                };

                RegisterGroupDragTargets(outfitListView);
            }

            ApplyScaleMultiplyToGroup(group);
            _outfitListSnapshots[outfitListView] = group.outfitListItems.ToList();
            outfitListView.Rebuild();
        }
    }
}
