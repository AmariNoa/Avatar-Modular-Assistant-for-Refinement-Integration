using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
        private const string ItemGroupExportDefaultFileName = "ItemGroupExport";

        private AmariItemGroupListItem _activeItemGroupTab;

        private void BuildItemGroupTabPanel(VisualElement root)
        {
            var itemTabScrollView = root.Q<ScrollView>("ItemGroupTabListView");
            var itemTabItemAddButton = root.Q<Button>("NewItemTabGroupButton");
            var itemGroupExportButton = root.Q<Button>("ItemGroupExport");

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

            if (itemGroupExportButton != null)
            {
                itemGroupExportButton.clicked += OnItemGroupExportButtonClicked;
            }
        }

        private void OnItemGroupExportButtonClicked()
        {
            if (_activeItemGroupTab == null)
            {
                EditorUtility.DisplayDialog("Item Group Export", "No active item group found.", "OK");
                return;
            }

            var fileName = BuildItemGroupExportFileName(_activeItemGroupTab.groupName);
            var savePath = EditorUtility.SaveFilePanel(
                "Export Item Group",
                Application.dataPath,
                fileName,
                "json");

            if (string.IsNullOrWhiteSpace(savePath))
            {
                return;
            }

            if (!savePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                savePath += ".json";
            }

            try
            {
                var exportRoot = BuildItemGroupExportJson(_activeItemGroupTab);
                var exportText = exportRoot.ToString(Formatting.Indented);
                File.WriteAllText(savePath, exportText, new UTF8Encoding(false));

                if (IsPathUnderAssetsDirectory(savePath))
                {
                    AssetDatabase.Refresh();
                }

                Debug.Log($"[AMARI] Item group exported: {savePath}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AMARI] Failed to export item group: {ex.Message}");
            }
        }

        private JObject BuildItemGroupExportJson(AmariItemGroupListItem group)
        {
            var itemsObject = new JObject();
            var usedItemKeys = new HashSet<string>(StringComparer.Ordinal);
            var items = group?.itemListItems;

            if (items != null)
            {
                for (var i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    if (item == null)
                    {
                        continue;
                    }

                    var itemKey = BuildExportItemKey(item.prefabGuid, i, usedItemKeys);
                    var includeInBuild = item.instance != null && !item.instance.CompareTag("EditorOnly");

                    itemsObject[itemKey] = new JObject
                    {
                        ["IncludeInBuild"] = includeInBuild,
                        /*
                        ["MaterialOverrides"] = new JObject
                        {
                            // TODO マテリアルの上書き機能を実装したらここに出力
                        }
                        */
                    };
                }
            }

            return new JObject
            {
                ["ItemGroupName"] = group?.groupName ?? string.Empty,
                ["AvatarPrefabGuid"] = ResolveAvatarPrefabGuidForExport(),
                ["ScaleMultiply"] = group?.scaleMultiply ?? 1f,
                ["Items"] = itemsObject
            };
        }

        private string ResolveAvatarPrefabGuidForExport()
        {
            if (_avatarDescriptor == null || _avatarDescriptor.gameObject == null)
            {
                return string.Empty;
            }

            if (!AmariAvatarPresetManager.TryGetPresetByAvatarPrefab(_avatarDescriptor.gameObject, out var preset) || preset == null)
            {
                return string.Empty;
            }

            return AmariAvatarPresetManager.TryGetAvatarPrefabGuidByAvatarPrefab(_avatarDescriptor.gameObject, preset, out var avatarPrefabGuid)
                ? avatarPrefabGuid ?? string.Empty
                : string.Empty;
        }

        private static string BuildItemGroupExportFileName(string groupName)
        {
            var fileName = string.IsNullOrWhiteSpace(groupName) ? ItemGroupExportDefaultFileName : groupName.Trim();
            foreach (var invalidChar in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(invalidChar, '_');
            }

            return string.IsNullOrWhiteSpace(fileName) ? ItemGroupExportDefaultFileName : fileName;
        }

        private static string BuildExportItemKey(string prefabGuid, int index, ISet<string> usedKeys)
        {
            var baseKey = string.IsNullOrWhiteSpace(prefabGuid) ? $"__EMPTY_PREFAB_GUID_{index}" : prefabGuid.Trim();
            if (usedKeys.Add(baseKey))
            {
                return baseKey;
            }

            var suffix = 1;
            while (true)
            {
                var candidateKey = $"{baseKey}_{suffix}";
                if (usedKeys.Add(candidateKey))
                {
                    return candidateKey;
                }

                suffix++;
            }
        }

        private static bool IsPathUnderAssetsDirectory(string path)
        {
            var normalizedPath = Path.GetFullPath(path).Replace('\\', '/');
            var normalizedAssets = Path.GetFullPath(Application.dataPath).Replace('\\', '/').TrimEnd('/');
            return normalizedPath.StartsWith(normalizedAssets + "/", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalizedPath, normalizedAssets, StringComparison.OrdinalIgnoreCase);
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
                        scaleMultiply = 1f,
                        previewEnabled = true,
                        previewStateInitialized = true
                    };
                    _avatarSettings.ItemListGroupItems[index] = group;
                    MarkSettingsDirty();
                }

                if (string.IsNullOrWhiteSpace(group.groupName))
                {
                    group.groupName = GetUnusedItemGroupName();
                    MarkSettingsDirty();
                }

                if (EnsureGroupActivePreviewItem(group))
                {
                    MarkSettingsDirty();
                }

                var tabElement = itemGroupTabItemAsset.Instantiate();
                var groupPreviewButton = tabElement.Q<Button>("ItemGroupPreviewButton");
                if (groupPreviewButton != null)
                {
                    SetPreviewButtonState(groupPreviewButton, group.previewEnabled);
                    var previewState = GetOrCreateItemGroupElementState(groupPreviewButton);
                    previewState.group = group;
                    if (!previewState.bound)
                    {
                        previewState.bound = true;
                        groupPreviewButton.clicked += () =>
                        {
                            if (groupPreviewButton.userData is not ItemGroupElementState s || s.group == null)
                            {
                                return;
                            }

                            RecordSettingsUndo("Toggle Item Group Preview");
                            var shouldEnable = !s.group.previewEnabled;
                            s.group.previewEnabled = shouldEnable;
                            if (shouldEnable)
                            {
                                // OFF中に保持していた最後のプレビュー対象を優先し、無効なら候補を補完
                                EnsureGroupActivePreviewItem(s.group);
                            }

                            UpdatePreviewInstanceActiveStates(true, "Toggle Item Group Preview");
                            MarkSettingsDirty();
                            RefreshItemGroupTabs(tabScrollView, root);
                        };
                    }
                }

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
                scaleMultiply = 1f,
                previewEnabled = true,
                previewStateInitialized = true
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
