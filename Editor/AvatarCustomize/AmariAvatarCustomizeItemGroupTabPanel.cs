using System;
using System.Collections.Generic;
using System.Globalization;
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

        private sealed class ImportedItemGroupData
        {
            public string groupName;
            public string avatarPrefabGuid;
            public float scaleMultiply = 1f;
            public List<ImportedItemData> items = new();
        }

        private sealed class ImportedItemData
        {
            public string prefabGuid;
            public bool includeInBuild;
        }

        private sealed class SharedBaseBodyScaleCandidate
        {
            public string avatarPrefabGuid;
            public string displayName;
            public float scaleMultiply;
        }

        private sealed class ScaleByPresetPopupContent : UnityEditor.PopupWindowContent
        {
            private readonly IReadOnlyList<SharedBaseBodyScaleCandidate> _candidates;
            private readonly Action<float> _onSelected;
            private Vector2 _scrollPosition;

            public ScaleByPresetPopupContent(IReadOnlyList<SharedBaseBodyScaleCandidate> candidates, Action<float> onSelected)
            {
                _candidates = candidates ?? Array.Empty<SharedBaseBodyScaleCandidate>();
                _onSelected = onSelected;
            }

            public override Vector2 GetWindowSize()
            {
                if (_candidates.Count == 0)
                {
                    return new Vector2(360f, 76f);
                }

                var visibleRows = Mathf.Clamp(_candidates.Count, 1, 10);
                return new Vector2(360f, 16f + visibleRows * 24f);
            }

            public override void OnGUI(Rect rect)
            {
                GUILayout.Space(4f);

                if (_candidates.Count == 0)
                {
                    EditorGUILayout.HelpBox(
                        AmariLocalization.Get("amari.window.avatarCustomize.sharedBaseBodyCandidateEmpty"),
                        MessageType.Info);
                    return;
                }

                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
                foreach (var candidate in _candidates)
                {
                    if (candidate == null)
                    {
                        continue;
                    }

                    var name = string.IsNullOrWhiteSpace(candidate.displayName)
                        ? candidate.avatarPrefabGuid
                        : candidate.displayName;
                    var buttonLabel = $"{name} ({candidate.scaleMultiply:0.###})";
                    if (!GUILayout.Button(buttonLabel, GUILayout.Height(20f)))
                    {
                        continue;
                    }

                    _onSelected?.Invoke(candidate.scaleMultiply);
                    editorWindow?.Close();
                    GUIUtility.ExitGUI();
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private AmariItemGroupListItem _activeItemGroupTab;

        private void BuildItemGroupTabPanel(VisualElement root)
        {
            var itemTabScrollView = root.Q<ScrollView>("ItemGroupTabListView");
            var itemTabItemAddButton = root.Q<Button>("NewItemTabGroupButton");
            var itemGroupImportButton = root.Q<Button>("ItemGroupImport");
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

            if (itemGroupImportButton != null)
            {
                itemGroupImportButton.clicked += () => OnItemGroupImportButtonClicked(itemTabScrollView, root);
            }

            if (itemGroupExportButton != null)
            {
                itemGroupExportButton.clicked += OnItemGroupExportButtonClicked;
            }
        }

        private void OnItemGroupImportButtonClicked(ScrollView tabScrollView, VisualElement root)
        {
            if (_avatarSettings?.ItemListGroupItems == null)
            {
                return;
            }

            var importPath = EditorUtility.OpenFilePanel(
                "Import Item Group",
                Application.dataPath,
                "json");

            if (string.IsNullOrWhiteSpace(importPath))
            {
                return;
            }

            if (!File.Exists(importPath))
            {
                Debug.LogError($"[AMARI] Item group import file not found: {importPath}");
                return;
            }

            try
            {
                var json = File.ReadAllText(importPath, Encoding.UTF8);
                if (!TryParseImportedItemGroupJson(json, out var imported, out var parseError))
                {
                    Debug.LogError($"[AMARI] Failed to import item group: {parseError}");
                    return;
                }

                ImportItemGroup(imported, tabScrollView, root);
                Debug.Log($"[AMARI] Item group imported: {importPath}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AMARI] Failed to import item group: {ex.Message}");
            }
        }

        private void ShowScaleByPresetPopup(Button anchorButton, FloatField scaleMultiplyField, AmariItemGroupListItem group)
        {
            if (anchorButton == null || scaleMultiplyField == null || group == null)
            {
                return;
            }

            var candidates = BuildSharedBaseBodyScaleCandidates();
            UnityEditor.PopupWindow.Show(anchorButton.worldBound, new ScaleByPresetPopupContent(candidates, selectedScale =>
            {
                scaleMultiplyField.value = selectedScale;
            }));
        }

        private List<SharedBaseBodyScaleCandidate> BuildSharedBaseBodyScaleCandidates()
        {
            var candidates = new List<SharedBaseBodyScaleCandidate>();
            var addedPresetKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_avatarDescriptor == null || _avatarDescriptor.gameObject == null)
            {
                return candidates;
            }

            if (!AmariAvatarPresetManager.TryGetPresetByAvatarPrefab(_avatarDescriptor.gameObject, out var currentPreset) ||
                currentPreset == null)
            {
                return candidates;
            }

            var currentAvatarGuid = string.Empty;
            if (AmariAvatarPresetManager.TryGetAvatarPrefabGuidByAvatarPrefab(_avatarDescriptor.gameObject, currentPreset, out var resolvedGuid))
            {
                currentAvatarGuid = resolvedGuid ?? string.Empty;
            }

            var languageCode = AmariLocalization.CurrentLanguageCode;
            foreach (var (sharedAvatarGuid, scaleMultiply) in currentPreset.SharedBaseBody)
            {
                if (string.IsNullOrWhiteSpace(sharedAvatarGuid))
                {
                    continue;
                }

                if (string.Equals(sharedAvatarGuid, currentAvatarGuid, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!AmariAvatarPresetManager.TryGetPresetByAvatarPrefabGuid(sharedAvatarGuid, out var targetPreset) ||
                    targetPreset == null)
                {
                    continue;
                }

                if (ReferenceEquals(targetPreset, currentPreset))
                {
                    continue;
                }

                var presetKey = targetPreset.SourceAssetPath;
                if (string.IsNullOrWhiteSpace(presetKey))
                {
                    presetKey = $"__GUID__:{sharedAvatarGuid}";
                }

                if (!addedPresetKeys.Add(presetKey))
                {
                    continue;
                }

                var displayName = targetPreset.GetAvatarName(languageCode);
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    displayName = targetPreset.GetAvatarName();
                }

                candidates.Add(new SharedBaseBodyScaleCandidate
                {
                    avatarPrefabGuid = sharedAvatarGuid,
                    displayName = displayName,
                    scaleMultiply = scaleMultiply
                });
            }

            candidates.Sort((left, right) =>
            {
                var leftName = left?.displayName ?? string.Empty;
                var rightName = right?.displayName ?? string.Empty;
                return string.Compare(leftName, rightName, StringComparison.OrdinalIgnoreCase);
            });

            return candidates;
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

        private bool TryParseImportedItemGroupJson(string json, out ImportedItemGroupData imported, out string error)
        {
            imported = null;
            error = null;

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (JsonException ex)
            {
                error = $"json parse failed: {ex.Message}";
                return false;
            }

            var groupName = root["ItemGroupName"]?.Value<string>()?.Trim();
            if (string.IsNullOrWhiteSpace(groupName))
            {
                groupName = DefaultGroupName;
            }

            var avatarPrefabGuid = root["AvatarPrefabGuid"]?.Value<string>()?.Trim();
            if (!IsLikelyGuid(avatarPrefabGuid))
            {
                avatarPrefabGuid = string.Empty;
            }

            var scaleMultiply = 1f;
            if (root.TryGetValue("ScaleMultiply", out var scaleToken) && !TryReadScaleMultiply(scaleToken, out scaleMultiply))
            {
                error = "\"ScaleMultiply\" must be a number";
                return false;
            }

            if (root["Items"] is not JObject itemsObject)
            {
                error = "\"Items\" must be an object";
                return false;
            }

            var importedItems = new List<ImportedItemData>();
            foreach (var itemPair in itemsObject.Properties())
            {
                if (!TryResolveImportedPrefabGuid(itemPair.Name, itemPair.Value as JObject, out var prefabGuid))
                {
                    Debug.LogWarning($"[AMARI] Item group import skipped invalid prefab guid key: {itemPair.Name}");
                    continue;
                }

                var includeInBuild = false;
                if (itemPair.Value is JObject itemObject)
                {
                    includeInBuild = TryReadIncludeInBuild(itemObject);
                }

                importedItems.Add(new ImportedItemData
                {
                    prefabGuid = prefabGuid,
                    includeInBuild = includeInBuild
                });
            }

            imported = new ImportedItemGroupData
            {
                groupName = groupName,
                avatarPrefabGuid = avatarPrefabGuid,
                scaleMultiply = scaleMultiply,
                items = importedItems
            };

            return true;
        }

        private bool TryResolveSharedBaseBodyScaleMultiply(string importedAvatarPrefabGuid, out float scaleMultiply)
        {
            scaleMultiply = 1f;
            if (string.IsNullOrWhiteSpace(importedAvatarPrefabGuid) || _avatarDescriptor?.gameObject == null)
            {
                return false;
            }

            if (!AmariAvatarPresetManager.TryGetPresetByAvatarPrefab(_avatarDescriptor.gameObject, out var currentPreset) ||
                currentPreset == null)
            {
                return false;
            }

            var normalizedImportedGuid = importedAvatarPrefabGuid.Trim();

            var currentAvatarGuid = string.Empty;
            if (AmariAvatarPresetManager.TryGetAvatarPrefabGuidByAvatarPrefab(_avatarDescriptor.gameObject, currentPreset, out var resolvedGuid))
            {
                currentAvatarGuid = resolvedGuid ?? string.Empty;
            }

            // 同一アバター由来のJSONは共通素体補正の対象外（書き出し値をそのまま利用する）
            if (string.Equals(normalizedImportedGuid, currentAvatarGuid, StringComparison.Ordinal))
            {
                return false;
            }

            return currentPreset.SharedBaseBody.TryGetValue(normalizedImportedGuid, out scaleMultiply);
        }

        private static bool TryReadScaleMultiply(JToken token, out float scaleMultiply)
        {
            switch (token?.Type)
            {
                case JTokenType.Integer:
                case JTokenType.Float:
                    scaleMultiply = token.Value<float>();
                    return true;
                case JTokenType.String when float.TryParse(token.Value<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                    scaleMultiply = parsed;
                    return true;
                case JTokenType.String when float.TryParse(token.Value<string>(), NumberStyles.Float, CultureInfo.CurrentCulture, out var parsedCurrentCulture):
                    scaleMultiply = parsedCurrentCulture;
                    return true;
                default:
                    scaleMultiply = 0f;
                    return false;
            }
        }

        private static bool TryReadIncludeInBuild(JObject itemObject)
        {
            var includeToken = itemObject["IncludeInBuild"];
            if (includeToken == null)
            {
                return false;
            }

            switch (includeToken.Type)
            {
                case JTokenType.Boolean:
                    return includeToken.Value<bool>();
                case JTokenType.Integer:
                    return includeToken.Value<int>() != 0;
                case JTokenType.String when bool.TryParse(includeToken.Value<string>(), out var parsed):
                    return parsed;
                default:
                    return false;
            }
        }

        private static bool TryResolveImportedPrefabGuid(string rawKey, JObject itemObject, out string prefabGuid)
        {
            prefabGuid = null;
            if (string.IsNullOrWhiteSpace(rawKey))
            {
                return false;
            }

            var candidate = rawKey.Trim();
            if (candidate.StartsWith("__EMPTY_PREFAB_GUID_", StringComparison.Ordinal))
            {
                return false;
            }

            if (IsLikelyGuid(candidate))
            {
                prefabGuid = candidate;
                return true;
            }

            var suffixSeparatorIndex = candidate.LastIndexOf('_');
            if (suffixSeparatorIndex <= 0 || suffixSeparatorIndex >= candidate.Length - 1)
            {
                return TryResolvePrefabGuidFromValue(itemObject, out prefabGuid);
            }

            var suffix = candidate[(suffixSeparatorIndex + 1)..];
            if (!int.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                return TryResolvePrefabGuidFromValue(itemObject, out prefabGuid);
            }

            var originalGuidCandidate = candidate[..suffixSeparatorIndex];
            if (!IsLikelyGuid(originalGuidCandidate))
            {
                return TryResolvePrefabGuidFromValue(itemObject, out prefabGuid);
            }

            prefabGuid = originalGuidCandidate;
            return true;
        }

        private static bool TryResolvePrefabGuidFromValue(JObject itemObject, out string prefabGuid)
        {
            prefabGuid = null;
            if (itemObject == null)
            {
                return false;
            }

            var candidate = itemObject["PrefabGuid"]?.Value<string>()?.Trim();
            if (!IsLikelyGuid(candidate))
            {
                return false;
            }

            prefabGuid = candidate;
            return true;
        }

        private static bool IsLikelyGuid(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate) || candidate.Length != 32)
            {
                return false;
            }

            for (var i = 0; i < candidate.Length; i++)
            {
                var c = candidate[i];
                var isHex = (c >= '0' && c <= '9') ||
                            (c >= 'a' && c <= 'f') ||
                            (c >= 'A' && c <= 'F');
                if (!isHex)
                {
                    return false;
                }
            }

            return true;
        }

        private void ImportItemGroup(ImportedItemGroupData imported, ScrollView tabScrollView, VisualElement root)
        {
            if (imported == null || _avatarSettings?.ItemListGroupItems == null)
            {
                return;
            }

            RecordSettingsUndo("Import Item Group");

            var desiredGroupName = string.IsNullOrWhiteSpace(imported.groupName) ? DefaultGroupName : imported.groupName.Trim();
            var group = new AmariItemGroupListItem
            {
                groupName = GetUnusedItemGroupName(desiredGroupName),
                itemListItems = new List<AmariItemListItem>(),
                scaleMultiply = imported.scaleMultiply,
                previewEnabled = true,
                previewStateInitialized = true
            };

            if (TryResolveSharedBaseBodyScaleMultiply(imported.avatarPrefabGuid, out var sharedBaseBodyScale))
            {
                group.scaleMultiply = sharedBaseBodyScale;
            }

            _avatarSettings.ItemListGroupItems.Add(group);

            foreach (var importedItem in imported.items.Where(item => item != null && !string.IsNullOrWhiteSpace(item.prefabGuid)))
            {
                var item = new AmariItemListItem();
                GameObject prefab = null;
                GameObject instance = null;
                var prefabPath = AssetDatabase.GUIDToAssetPath(importedItem.prefabGuid);
                if (!string.IsNullOrWhiteSpace(prefabPath))
                {
                    var loadedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                    if (IsPrefabAsset(loadedPrefab))
                    {
                        prefab = loadedPrefab;
                        instance = UpdatePrefabInstanceInScene(item, prefab, true, "Import Item Prefab");
                    }
                    else
                    {
                        Debug.LogWarning($"[AMARI] Item group import prefab is not a prefab asset guid: {importedItem.prefabGuid}");
                    }
                }
                else
                {
                    Debug.LogWarning($"[AMARI] Item group import prefab guid not found in project: {importedItem.prefabGuid}");
                }

                SetItemListItemValues(item, prefab, importedItem.prefabGuid, instance);
                if (instance != null)
                {
                    Undo.RecordObject(instance, "Toggle Include In Build");
                    instance.tag = importedItem.includeInBuild ? "Untagged" : "EditorOnly";
                    MarkObjectDirty(instance);
                }

                group.itemListItems.Add(item);
                ApplyScaleMultiplyToItem(group, item, true, "Apply Item Scale");
                CheckOrActivatePreviewItem(group, item);
            }

            EnsureGroupActivePreviewItem(group);
            UpdatePreviewInstanceActiveStates(true, "Import Item Group");
            UpdateItemCheckResultsForGroup(group);
            MarkSettingsDirty();

            _activeItemGroupTab = group;
            RefreshItemGroupTabs(tabScrollView, root);
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

            var scaleByPresetButton = root.Q<Button>("ScaleByPreset");
            if (scaleByPresetButton != null)
            {
                var scaleButtonState = GetOrCreateItemGroupElementState(scaleByPresetButton);
                scaleButtonState.group = group;
                if (!scaleButtonState.bound)
                {
                    scaleButtonState.bound = true;
                    scaleByPresetButton.clicked += () =>
                    {
                        if (scaleByPresetButton.userData is not ItemGroupElementState state || state.group == null)
                        {
                            return;
                        }

                        var field = root.Q<FloatField>("ScaleMultiply");
                        ShowScaleByPresetPopup(scaleByPresetButton, field, state.group);
                    };
                }
            }

            BindItemListViewForGroup(itemListView, group);
            SetupLocalizationTextItem(root);
        }
    }
}
