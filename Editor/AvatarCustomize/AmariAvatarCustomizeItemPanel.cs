using System.Collections.Generic;
using System.Linq;
using com.amari_noa.avatar_modular_assistant.runtime;
using com.amari_noa.avatar_modular_assistant.editor.integrations;
using com.amari_noa.avatar_modular_assistant.editor.integrations.modular_avatar;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using EditorObjectField = UnityEditor.UIElements.ObjectField;

// ReSharper disable once CheckNamespace
namespace com.amari_noa.avatar_modular_assistant.editor
{
    public partial class AmariAvatarCustomizeWindow
    {
        private const string DefaultGroupName = "Default";
        private static Texture2D ItemInfoIconNormal;
        private static Texture2D ItemInfoIconNotify;
        private static Texture2D ItemInfoIconProblem;
        private static bool _itemIconsLoaded;

        private sealed class ItemInfoButtonState
        {
            public bool bound;
            public AmariItemListItem item;
        }

        private sealed class ItemItemElementState
        {
            public bool bound;
            public AmariItemListItem item;
            public AmariItemGroupListItem group;
            public ListView listView;
        }

        private sealed class ItemGroupElementState
        {
            public bool bound;
            public AmariItemGroupListItem group;
        }

        private sealed class ItemListViewState
        {
            public bool bound;
            public AmariItemGroupListItem group;
        }

        private sealed class ItemInfoPopupIssue
        {
            public AmariSeverity severity;
            public string message;
            public string actionButtonLabel;
            public System.Action onAction;
        }

        private sealed class ItemInfoPopupContent : UnityEditor.PopupWindowContent
        {
            private readonly System.Func<IReadOnlyList<ItemInfoPopupIssue>> _issueProvider;
            private Vector2 _scrollPosition;

            public ItemInfoPopupContent(System.Func<IReadOnlyList<ItemInfoPopupIssue>> issueProvider)
            {
                _issueProvider = issueProvider;
            }

            public override Vector2 GetWindowSize()
            {
                var issues = GetCurrentIssues();
                if (issues.Count == 0)
                {
                    return new Vector2(440f, 92f);
                }

                var visibleRows = Mathf.Clamp(issues.Count, 1, 6);
                return new Vector2(440f, 22f + visibleRows * 94f);
            }

            public override void OnGUI(Rect rect)
            {
                GUILayout.Space(4f);

                var issues = GetCurrentIssues();
                if (issues.Count == 0)
                {
                    EditorGUILayout.HelpBox(
                        Localize(
                            "amari.window.avatarCustomize.itemInfo.noIssueMessage",
                            "No critical issues or warnings were found for this item."),
                        MessageType.Info);
                    return;
                }

                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
                foreach (var issue in issues)
                {
                    if (issue == null)
                    {
                        continue;
                    }

                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.HelpBox(issue.message ?? string.Empty, ToMessageType(issue.severity));
                    if (GUILayout.Button(
                            issue.actionButtonLabel ??
                            Localize(
                                "amari.window.avatarCustomize.itemInfo.defaultActionButton",
                                "Handle"),
                            GUILayout.Height(20f)))
                    {
                        issue.onAction?.Invoke();
                        editorWindow?.Repaint();
                        GUIUtility.ExitGUI();
                    }

                    EditorGUILayout.EndVertical();
                    GUILayout.Space(2f);
                }

                EditorGUILayout.EndScrollView();
            }

            private IReadOnlyList<ItemInfoPopupIssue> GetCurrentIssues()
            {
                return _issueProvider?.Invoke() ?? System.Array.Empty<ItemInfoPopupIssue>();
            }

            private static MessageType ToMessageType(AmariSeverity severity)
            {
                return severity switch
                {
                    AmariSeverity.Critical => MessageType.Error,
                    AmariSeverity.Warning => MessageType.Warning,
                    _ => MessageType.Info
                };
            }
        }

        private static void EnsureItemIconsLoaded()
        {
            if (_itemIconsLoaded)
            {
                return;
            }

            ItemInfoIconNormal = EditorGUIUtility.IconContent("console.infoicon.sml").image as Texture2D;
            ItemInfoIconNotify = EditorGUIUtility.IconContent("console.warnicon.sml").image as Texture2D;
            ItemInfoIconProblem = EditorGUIUtility.IconContent("console.erroricon.sml").image as Texture2D;

            _itemIconsLoaded = true;
        }

        private void EnsureActivePreviewItem()
        {
            if (_avatarSettings?.ItemListGroupItems == null)
            {
                return;
            }

            var changed = false;
            foreach (var group in _avatarSettings.ItemListGroupItems.Where(group => group != null))
            {
                changed |= EnsureGroupActivePreviewItem(group);
            }

            UpdatePreviewInstanceActiveStates();
            if (changed)
            {
                MarkSettingsDirty();
            }
        }

        private static void SetItemListItemValues(AmariItemListItem item, GameObject prefab, string guid, GameObject instance)
        {
            item.prefab = prefab;
            item.prefabGuid = guid;
            item.instance = instance;
        }

        private IEnumerable<AmariItemListItem> EnumerateAllItemItems()
        {
            if (_avatarSettings?.ItemListGroupItems == null)
            {
                yield break;
            }

            foreach (var item in _avatarSettings.ItemListGroupItems.Where(group => group?.itemListItems != null).SelectMany(group => group.itemListItems.Where(item => item != null)))
            {
                yield return item;
            }
        }

        private bool IsDuplicatePrefab(string guid)
        {
            return string.IsNullOrWhiteSpace(guid) || EnumerateAllItemItems().Any(item => string.Equals(item.prefabGuid, guid, System.StringComparison.Ordinal));
        }

        private bool IsDuplicatePrefab(string guid, AmariItemListItem self)
        {
            return string.IsNullOrWhiteSpace(guid) || EnumerateAllItemItems().Any(item => item != self && string.Equals(item.prefabGuid, guid, System.StringComparison.Ordinal));
        }

        private static bool IsGroupActivePreviewItem(AmariItemGroupListItem group, AmariItemListItem item)
        {
            return group?.itemListItems != null &&
                   item != null &&
                   group.itemListItems.Contains(item) &&
                   item.instance != null;
        }

        private static bool TryResolveGroupItemReference(AmariItemGroupListItem group, AmariItemListItem sourceItem, out AmariItemListItem resolvedItem)
        {
            resolvedItem = null;
            if (group?.itemListItems == null || sourceItem == null)
            {
                return false;
            }

            if (group.itemListItems.Contains(sourceItem))
            {
                resolvedItem = sourceItem;
                return true;
            }

            if (sourceItem.instance != null)
            {
                resolvedItem = group.itemListItems.FirstOrDefault(candidate => candidate != null && candidate.instance == sourceItem.instance);
                if (resolvedItem != null)
                {
                    return true;
                }
            }

            if (!string.IsNullOrWhiteSpace(sourceItem.prefabGuid))
            {
                resolvedItem = group.itemListItems.FirstOrDefault(candidate =>
                    candidate != null &&
                    string.Equals(candidate.prefabGuid, sourceItem.prefabGuid, System.StringComparison.Ordinal));
                if (resolvedItem != null)
                {
                    return true;
                }
            }

            if (sourceItem.prefab != null)
            {
                resolvedItem = group.itemListItems.FirstOrDefault(candidate => candidate != null && candidate.prefab == sourceItem.prefab);
                if (resolvedItem != null)
                {
                    return true;
                }
            }

            return false;
        }

        private static AmariItemListItem FindGroupPreviewCandidate(AmariItemGroupListItem group, AmariItemListItem preferredItem = null)
        {
            if (group?.itemListItems == null)
            {
                return null;
            }

            if (TryResolveGroupItemReference(group, preferredItem, out var resolvedPreferredItem) &&
                IsGroupActivePreviewItem(group, resolvedPreferredItem))
            {
                return resolvedPreferredItem;
            }

            return group.itemListItems.FirstOrDefault(candidate => candidate?.instance != null);
        }

        private bool EnsureGroupPreviewStateInitialized(AmariItemGroupListItem group)
        {
            if (group == null || group.previewStateInitialized)
            {
                return false;
            }

            group.previewEnabled = true;
            group.previewStateInitialized = true;
            return true;
        }

        private bool EnsureGroupActivePreviewItem(AmariItemGroupListItem group, AmariItemListItem preferredItem = null)
        {
            if (group == null)
            {
                return false;
            }

            var changed = EnsureGroupPreviewStateInitialized(group);
            group.itemListItems ??= new List<AmariItemListItem>();

            var active = group.activePreviewItem;
            if (TryResolveGroupItemReference(group, active, out var resolvedActive) &&
                !ReferenceEquals(group.activePreviewItem, resolvedActive))
            {
                group.activePreviewItem = resolvedActive;
                changed = true;
            }

            active = group.activePreviewItem;
            if (IsGroupActivePreviewItem(group, active))
            {
                return changed;
            }

            var next = FindGroupPreviewCandidate(group, preferredItem);
            if (!ReferenceEquals(group.activePreviewItem, next))
            {
                group.activePreviewItem = next;
                changed = true;
            }

            return changed;
        }

        private bool IsItemPreviewing(AmariItemGroupListItem group, AmariItemListItem item)
        {
            if (group == null || item == null || !group.previewEnabled)
            {
                return false;
            }

            return IsGroupActivePreviewItem(group, group.activePreviewItem) && group.activePreviewItem == item;
        }

        private void OnActivePreviewItemDestroy(AmariItemGroupListItem group, AmariItemListItem item, bool registerUndo = false, string undoName = null)
        {
            if (group == null || item == null)
            {
                return;
            }

            var needsFallback = group.activePreviewItem == item || !IsGroupActivePreviewItem(group, group.activePreviewItem);
            if (!needsFallback)
            {
                return;
            }

            if (registerUndo)
            {
                RecordSettingsUndo(undoName ?? "Change Active Preview Item");
            }

            group.activePreviewItem = FindGroupPreviewCandidate(group);
            UpdatePreviewInstanceActiveStates(registerUndo, undoName);

            if (registerUndo)
            {
                MarkSettingsDirty();
            }

        }

        private bool CheckOrActivatePreviewItem(AmariItemGroupListItem group, AmariItemListItem item)
        {
            if (group == null || item == null)
            {
                return false;
            }

            EnsureGroupPreviewStateInitialized(group);
            if (IsGroupActivePreviewItem(group, group.activePreviewItem))
            {
                return false;
            }

            group.activePreviewItem = item;
            return true;
        }

        private void UpdatePreviewInstanceActiveStates(bool registerUndo = false, string undoName = null)
        {
            if (_avatarSettings?.ItemListGroupItems == null)
            {
                return;
            }

            foreach (var group in _avatarSettings.ItemListGroupItems.Where(group => group?.itemListItems != null))
            {
                var active = IsGroupActivePreviewItem(group, group.activePreviewItem) ? group.activePreviewItem : null;
                foreach (var item in group.itemListItems.Where(item => item != null))
                {
                    if (item.instance == null)
                    {
                        continue;
                    }

                    if (registerUndo)
                    {
                        Undo.RecordObject(item.instance, undoName ?? "Toggle Preview Item");
                        MarkObjectDirty(item.instance);
                    }

                    item.instance.SetActive(group.previewEnabled && item == active);
                }
            }
        }

        private static void SetPreviewButtonState(Button button, bool isPreviewing)
        {
            if (button == null)
            {
                return;
            }

            button.text = isPreviewing
                ? Localize("amari.window.avatarCustomize.previewButtonPreviewing")
                : Localize("amari.window.avatarCustomize.previewButtonPreview");
            button.style.backgroundColor = isPreviewing ? new StyleColor(new Color(0.0f, 0.6f, 0.0f)) : new StyleColor(new Color(0.6f, 0.0f, 0.0f));
        }

        private static void SetItemInfoButtonState(Button button, bool needsAttention)
        {
            if (button == null)
            {
                return;
            }

            EnsureItemIconsLoaded();

            button.text = string.Empty;
            var icon = needsAttention ? ItemInfoIconProblem : ItemInfoIconNormal;
            if (icon != null)
            {
                button.style.backgroundImage = new StyleBackground(icon);
            }
        }

        private static ItemInfoButtonState GetOrCreateItemInfoButtonState(Button button)
        {
            if (button.userData is ItemInfoButtonState state)
            {
                return state;
            }

            state = new ItemInfoButtonState();
            button.userData = state;
            return state;
        }

        private static ItemItemElementState GetOrCreateItemItemElementState(VisualElement element)
        {
            if (element.userData is ItemItemElementState state)
            {
                return state;
            }

            state = new ItemItemElementState();
            element.userData = state;
            return state;
        }

        private static ItemGroupElementState GetOrCreateItemGroupElementState(VisualElement element)
        {
            if (element.userData is ItemGroupElementState state)
            {
                return state;
            }

            state = new ItemGroupElementState();
            element.userData = state;
            return state;
        }

        private static ItemListViewState GetOrCreateItemListViewState(ListView listView)
        {
            if (listView.userData is ItemListViewState state)
            {
                return state;
            }

            state = new ItemListViewState();
            listView.userData = state;
            return state;
        }

        private void UpdateGroupListViewMapping(AmariItemGroupListItem group, ListView listView)
        {
            if (group == null || listView == null)
            {
                return;
            }

            var existing = _groupToListView.FirstOrDefault(kv => kv.Value == listView).Key;
            if (existing != null && existing != group)
            {
                _groupToListView.Remove(existing);
            }

            _groupToListView[group] = listView;
        }

        private void SyncItemListSnapshot(ListView listView, AmariItemGroupListItem group = null)
        {
            if (listView == null)
            {
                return;
            }

            if (group == null && listView.userData is ItemListViewState state)
            {
                group = state.group;
            }

            if (group?.itemListItems == null)
            {
                _itemListSnapshots.Remove(listView);
                return;
            }

            _itemListSnapshots[listView] = group.itemListItems.ToList();
        }

        private static List<AmariItemListItem> ResolveRemovedItems(IEnumerable<int> removedIndices, List<AmariItemListItem> snapshot, List<AmariItemListItem> currentItems)
        {
            var indices = removedIndices?.ToList() ?? new List<int>();
            if (snapshot == null || snapshot.Count == 0)
            {
                return new List<AmariItemListItem>();
            }

            var expectedRemovedCount = indices.Count;
            if (expectedRemovedCount <= 0 && currentItems != null)
            {
                expectedRemovedCount = Mathf.Max(0, snapshot.Count - currentItems.Count);
            }

            var removedItems = new List<AmariItemListItem>();
            var seen = new HashSet<AmariItemListItem>();

            var currentCounts = new Dictionary<AmariItemListItem, int>();
            if (currentItems != null)
            {
                foreach (var item in currentItems.Where(item => item != null))
                {
                    currentCounts.TryGetValue(item, out var count);
                    currentCounts[item] = count + 1;
                }
            }

            // 差分を基準に削除対象を特定する。indices は Unity 側の都合で不安定な場合があるため補助扱い。
            foreach (var item in snapshot.Where(item => item != null))
            {
                if (currentCounts.TryGetValue(item, out var count) && count > 0)
                {
                    if (count == 1)
                    {
                        currentCounts.Remove(item);
                    }
                    else
                    {
                        currentCounts[item] = count - 1;
                    }

                    continue;
                }

                if (!seen.Add(item))
                {
                    continue;
                }

                removedItems.Add(item);
            }

            if (expectedRemovedCount <= 0 || removedItems.Count >= expectedRemovedCount)
            {
                return removedItems;
            }

            foreach (var index in indices)
            {
                if (index < 0 || index >= snapshot.Count)
                {
                    continue;
                }

                var item = snapshot[index];
                if (item == null || !seen.Add(item))
                {
                    continue;
                }

                removedItems.Add(item);
                if (removedItems.Count >= expectedRemovedCount)
                {
                    break;
                }
            }

            return removedItems;
        }

        private static void ResetItemElementState(VisualElement element)
        {
            if (element?.userData is not ItemItemElementState state)
            {
                return;
            }

            state.item = null;
            state.group = null;
            state.listView = null;
        }

        private static void ClearItemListElementVisuals(VisualElement element)
        {
            if (element == null)
            {
                return;
            }

            var prefabField = element.Q<EditorObjectField>("ItemPrefabField");
            if (prefabField != null)
            {
                prefabField.SetValueWithoutNotify(null);
                ResetItemElementState(prefabField);
            }

            var previewButton = element.Q<Button>("ItemPreviewStatusButton");
            if (previewButton != null)
            {
                SetPreviewButtonState(previewButton, false);
                ResetItemElementState(previewButton);
            }

            var includeInBuildToggle = element.Q<Toggle>("IncludeInBuildToggle");
            if (includeInBuildToggle != null)
            {
                includeInBuildToggle.SetValueWithoutNotify(false);
                ResetItemElementState(includeInBuildToggle);
            }

            var itemInfoButton = element.Q<Button>("ItemInfoButton");
            if (itemInfoButton != null)
            {
                SetItemInfoButtonState(itemInfoButton, false);
                if (itemInfoButton.userData is ItemInfoButtonState infoState)
                {
                    infoState.item = null;
                }
            }
        }

        private static void BindItemInfoButton(Button button, AmariItemListItem item, System.Action<AmariItemListItem, Rect> onClick)
        {
            if (button == null)
            {
                return;
            }

            var state = GetOrCreateItemInfoButtonState(button);
            state.item = item;

            if (state.bound)
            {
                return;
            }

            state.bound = true;
            button.clicked += () =>
            {
                if (button.userData is not ItemInfoButtonState s || s.item == null)
                {
                    return;
                }

                onClick?.Invoke(s.item, button.worldBound);
            };
        }

        private void OnItemInfoButtonClicked(AmariItemListItem item, Rect anchorRect)
        {
            if (item == null)
            {
                return;
            }

            UnityEditor.PopupWindow.Show(anchorRect, new ItemInfoPopupContent(() => BuildItemInfoPopupIssues(item)));
        }

        private List<ItemInfoPopupIssue> BuildItemInfoPopupIssues(AmariItemListItem item)
        {
            var issues = new List<ItemInfoPopupIssue>();
            if (item == null)
            {
                return issues;
            }

            switch (GetCurrentOutfitToolType())
            {
                case AmariOutfitToolType.None:
                    return issues;
                case AmariOutfitToolType.ModularAvatar:
                    if (!_itemCheckResults.TryGetValue(item, out var result))
                    {
                        return issues;
                    }

                    if (TryBuildModularAvatarIssue(item, result, out var modularAvatarIssue))
                    {
                        issues.Add(modularAvatarIssue);
                    }

                    return issues;
                default:
                    // 未実装のツール種別は警告なし扱いにする
                    return issues;
            }
        }

        private bool TryBuildModularAvatarIssue(AmariItemListItem item, AmariModularAvatarCheckResult result, out ItemInfoPopupIssue issue)
        {
            issue = null;
            switch (result.Suggestion)
            {
                case AmariModularAvatarSuggestedAction.None:
                    return false;
                case AmariModularAvatarSuggestedAction.AddBoneProxy:
                    issue = new ItemInfoPopupIssue
                    {
                        severity = AmariSeverity.Critical,
                    message = Localize(
                            "amari.window.avatarCustomize.itemInfo.modularAvatar.addBoneProxyMessage",
                            "Modular Avatar Bone Proxy is recommended for this item."),
                    actionButtonLabel = Localize(
                            "amari.window.avatarCustomize.itemInfo.modularAvatar.addBoneProxyActionButton",
                            "Add Bone Proxy"),
                        onAction = () => { }
                    };
                    return true;
                case AmariModularAvatarSuggestedAction.AddMergeArmature:
                    issue = new ItemInfoPopupIssue
                    {
                        severity = AmariSeverity.Critical,
                    message = Localize(
                            "amari.window.avatarCustomize.itemInfo.modularAvatar.addMergeArmatureMessage",
                            "Modular Avatar Merge Armature is recommended for this item."),
                    actionButtonLabel = Localize(
                            "amari.window.avatarCustomize.itemInfo.modularAvatar.addMergeArmatureActionButton",
                            "Add Merge Armature"),
                        onAction = () => ExecuteSetupOutfitForItem(item)
                    };
                    return true;
                default:
                    issue = new ItemInfoPopupIssue
                    {
                        severity = AmariSeverity.Critical,
                        message = !string.IsNullOrWhiteSpace(result.Reason)
                            ? result.Reason
                        : Localize(
                                "amari.window.avatarCustomize.itemInfo.modularAvatar.unknownWarningMessage",
                                "A warning was detected."),
                    actionButtonLabel = Localize(
                            "amari.window.avatarCustomize.itemInfo.modularAvatar.unknownWarningActionButton",
                            "Resolve Warning"),
                        onAction = () => { }
                    };
                    return true;
            }
        }

        private void ExecuteSetupOutfitForItem(AmariItemListItem item)
        {
            if (item?.instance == null)
            {
                return;
            }

            if (!TryInvokeSetupOutfitUi(item.instance))
            {
                return;
            }

            var group = FindItemGroupByItem(item);
            UpdateItemCheckResultsForGroup(group);
            if (group != null && _groupToListView.TryGetValue(group, out var listViewForGroup))
            {
                listViewForGroup.RefreshItems();
            }
        }

        private static bool TryInvokeSetupOutfitUi(GameObject outfitRoot)
        {
            if (outfitRoot == null)
            {
                return false;
            }

            return AmariModularAvatarIntegration.TrySetupOutfitUi(outfitRoot);
        }

        private AmariOutfitToolType GetCurrentOutfitToolType()
        {
            return _avatarSettings?.outfitToolType ?? AmariOutfitToolType.None;
        }

        private bool ShouldNotifyItemInfo(AmariItemListItem item)
        {
            if (item == null)
            {
                return false;
            }

            switch (GetCurrentOutfitToolType())
            {
                case AmariOutfitToolType.None:
                    return false;
                case AmariOutfitToolType.ModularAvatar:
                    if (!_itemCheckResults.TryGetValue(item, out var result))
                    {
                        return false;
                    }

                    return result.Suggestion != AmariModularAvatarSuggestedAction.None;
                default:
                    // 未実装のツール種別は警告なし扱いにする
                    return false;
            }
        }

        private void UpdateItemCheckResultsForGroup(AmariItemGroupListItem group)
        {
            if (group?.itemListItems == null)
            {
                return;
            }

            foreach (var item in group.itemListItems.Where(item => item != null))
            {
                _itemCheckResults.Remove(item);
            }

            switch (GetCurrentOutfitToolType())
            {
                case AmariOutfitToolType.None:
                    // None は全て問題なし扱い
                    return;
                case AmariOutfitToolType.ModularAvatar:
                    if (!AmariModularAvatarIntegration.IsInstalled())
                    {
                        return;
                    }

                    var checkResults = AmariModularAvatarIntegration.CheckGroup(group);
                    foreach (var item in group.itemListItems.Where(item => item?.instance != null))
                    {
                        if (checkResults.TryGetValue(item.instance, out var result))
                        {
                            _itemCheckResults[item] = result;
                        }
                    }

                    return;
                default:
                    // 未実装のツール種別は警告なし扱い
                    return;
            }
        }

        private GameObject UpdatePrefabInstanceInScene(AmariItemListItem item, GameObject newPrefab, bool registerUndo = false, string undoName = null)
        {
            if (item.instance)
            {
                if (registerUndo)
                {
                    Undo.DestroyObjectImmediate(item.instance);
                }
                else
                {
                    DestroyImmediate(item.instance);
                }
            }

            if (!newPrefab)
            {
                var group = FindItemGroupByItem(item);
                OnActivePreviewItemDestroy(group, item, registerUndo, undoName);
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(newPrefab, _avatarDescriptor.transform);
            instance.name = newPrefab.name;
            instance.tag = "EditorOnly";

            if (registerUndo)
            {
                Undo.RegisterCreatedObjectUndo(instance, undoName ?? "Create Item Prefab");
                MarkObjectDirty(instance);
            }

            return instance;
        }

        private void OnItemPrefabAdded(List<AmariItemListItem> targetList, GameObject prefab, string guid)
        {
            RecordSettingsUndo("Add Item Prefab");

            var item = new AmariItemListItem();
            var group = FindItemGroupByList(targetList);

            var instance = UpdatePrefabInstanceInScene(item, prefab, true, "Create Item Prefab");
            SetItemListItemValues(item, prefab, guid, instance);
            targetList.Add(item);
            ApplyScaleMultiplyToItem(group, item, true, "Apply Item Scale");
            CheckOrActivatePreviewItem(group, item);
            UpdatePreviewInstanceActiveStates();
            MarkSettingsDirty();

            if (group != null && _groupToListView.TryGetValue(group, out var listViewForGroup))
            {
                SyncItemListSnapshot(listViewForGroup, group);
            }
        }

        private void OnItemPrefabValueChanged(EditorObjectField prefabField, AmariItemListItem item, GameObject prefab, AmariItemGroupListItem group)
        {
            RecordSettingsUndo("Change Item Prefab");

            if (!prefab || !IsPrefabAsset(prefab))
            {
                // TODO 要挙動チェック 警告ダイアログを出す必要があるかも？
                prefabField.SetValueWithoutNotify(null);
                SetItemListItemValues(item, null, string.Empty, null);
                MarkSettingsDirty();
                return;
            }

            var newPath = AssetDatabase.GetAssetPath(prefab);
            var newGuid = AssetDatabase.AssetPathToGUID(newPath);
            if (IsDuplicatePrefab(newGuid, item))
            {
                prefabField.SetValueWithoutNotify(item.prefab);
                return;
            }

            var instance = UpdatePrefabInstanceInScene(item, prefab, true, "Change Item Prefab");
            SetItemListItemValues(item, prefab, newGuid, instance);
            ApplyScaleMultiplyToItem(group, item, true, "Apply Item Scale");

            CheckOrActivatePreviewItem(group, item);
            UpdatePreviewInstanceActiveStates();
            UpdateItemCheckResultsForGroup(group);
            if (group != null && _groupToListView.TryGetValue(group, out var listViewForGroup))
            {
                listViewForGroup.RefreshItems();
            }
            MarkSettingsDirty();
        }

        private AmariItemGroupListItem FindItemGroupByList(List<AmariItemListItem> targetList)
        {
            if (_avatarSettings?.ItemListGroupItems == null || targetList == null)
            {
                return null;
            }

            foreach (var group in _avatarSettings.ItemListGroupItems)
            {
                if (group?.itemListItems == targetList)
                {
                    return group;
                }
            }

            return null;
        }

        private AmariItemGroupListItem FindItemGroupByItem(AmariItemListItem item)
        {
            if (_avatarSettings?.ItemListGroupItems == null || item == null)
            {
                return null;
            }

            return _avatarSettings.ItemListGroupItems.FirstOrDefault(group =>
                group?.itemListItems != null && group.itemListItems.Contains(item));
        }

        private static void ApplyScaleMultiplyToItem(AmariItemGroupListItem group, AmariItemListItem item, bool registerUndo = false, string undoName = null)
        {
            if (group == null || item?.instance == null || item.prefab == null)
            {
                return;
            }

            var baseScale = item.prefab.transform.localScale;
            if (registerUndo)
            {
                Undo.RecordObject(item.instance.transform, undoName ?? "Apply Item Scale");
            }
            item.instance.transform.localScale = baseScale * group.scaleMultiply;
            if (registerUndo)
            {
                MarkObjectDirty(item.instance.transform);
            }
        }

        private static void ApplyScaleMultiplyToGroup(AmariItemGroupListItem group, bool registerUndo = false, string undoName = null)
        {
            if (group?.itemListItems == null)
            {
                return;
            }

            foreach (var item in group.itemListItems)
            {
                if (item?.instance == null || item.prefab == null)
                {
                    continue;
                }

                var baseScale = item.prefab.transform.localScale;
                if (registerUndo)
                {
                    Undo.RecordObject(item.instance.transform, undoName ?? "Apply Item Scale");
                }
                item.instance.transform.localScale = baseScale * group.scaleMultiply;
                if (registerUndo)
                {
                    MarkObjectDirty(item.instance.transform);
                }
            }
        }

        private bool AddItemPrefab(List<AmariItemListItem> targetList, GameObject obj)
        {
            if (!IsPrefabAsset(obj))
            {
                return false;
            }

            var path = AssetDatabase.GetAssetPath(obj);
            var guid = AssetDatabase.AssetPathToGUID(path);
            if (IsDuplicatePrefab(guid))
            {
                return false;
            }

            OnItemPrefabAdded(targetList, obj, guid);
            var group = FindItemGroupByList(targetList);
            UpdateItemCheckResultsForGroup(group);
            if (group != null && _groupToListView.TryGetValue(group, out var listViewForGroup))
            {
                listViewForGroup.Rebuild();
            }
            return true;
        }

        private void AddPrefabsFromDrag(Object[] refs, List<AmariItemListItem> targetList, ListView listView)
        {
            if (refs == null || refs.Length == 0)
            {
                return;
            }

            var added = false;
            foreach (var obj in refs)
            {
                if (!AddItemPrefab(targetList, (GameObject)obj))
                {
                    continue;
                }
                added = true;
            }

            if (added)
            {
                var group = FindItemGroupByList(targetList);
                UpdateItemCheckResultsForGroup(group);
                SyncItemListSnapshot(listView, group);
                listView.Rebuild();
            }
        }

        private void RegisterGroupDragTargets(VisualElement target)
        {
            if (target == null)
            {
                return;
            }

            if (_dragTargets.Contains(target))
            {
                return;
            }

            _dragTargets.Add(target);

            target.RegisterCallback<DragUpdatedEvent>(_ =>
            {
                DragAndDrop.visualMode = DragAndDrop.objectReferences != null && DragAndDrop.objectReferences.Any(IsPrefabAsset)
                    ? DragAndDropVisualMode.Copy
                    : DragAndDropVisualMode.Rejected;
            });

            target.RegisterCallback<DragPerformEvent>(_ =>
            {
                if (DragAndDrop.objectReferences == null || !DragAndDrop.objectReferences.Any(IsPrefabAsset))
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
                    return;
                }

                var listView = target as ListView ?? target.Q<ListView>("ItemListView");
                if (listView == null)
                {
                    return;
                }

                if (!_listViewToTargetList.TryGetValue(listView, out var targetList))
                {
                    return;
                }

                DragAndDrop.AcceptDrag();
                AddPrefabsFromDrag(DragAndDrop.objectReferences, targetList, listView);
            });
        }

        // TODO この命名処理あんまりスマートじゃないのでいつか改修したい
        private string GetUnusedItemGroupName(string groupName = DefaultGroupName)
        {
            var groups = _avatarSettings?.ItemListGroupItems;
            if (groups == null)
            {
                return groupName;
            }

            var exists = groups.Any(groupInner =>
                groupInner != null && string.Equals(groupInner.groupName, groupName, System.StringComparison.Ordinal));
            if (!exists)
            {
                return groupName;
            }

            for (var i = 1; i < int.MaxValue; i++)
            {
                var tmpGroupName = $"{groupName} {i}";

                var existsTmp = groups.Any(groupInner =>
                    groupInner != null && string.Equals(groupInner.groupName, tmpGroupName, System.StringComparison.Ordinal));

                if (existsTmp)
                {
                    continue;
                }

                return tmpGroupName;
            }

            // TODO 失敗した時のグループ命名をどうするべきか考える必要がある(そもそもint.MaxValueまで使うことなんて無いはずだけど)
            return groupName;
        }

        // TODO 色々なパネルのラベル更新処理が混ざってるので移動を検討したい
        private void SetupLocalizationTextItem(VisualElement root)
        {
            if (root == null)
            {
                return;
            }

            var itemPanelTitle = root.Q<Label>("ItemPanelTitle");
            if (itemPanelTitle != null)
            {
                itemPanelTitle.text = Localize("amari.window.avatarCustomize.panelItemTitle");
            }

            var editorLanguage = root.Q<DropdownField>("EditorLanguage");
            if (editorLanguage != null)
            {
                editorLanguage.label = Localize("amari.window.avatarCustomize.editorLanguageLabel");
            }

            var importUnityPackageButton = root.Q<Button>("ImportUnityPackageButton");
            if (importUnityPackageButton != null)
            {
                importUnityPackageButton.text = Localize(
                    "amari.window.avatarCustomize.importUnityPackageButton",
                    "Import unitypackage");
            }

            var importBlmButton = root.Q<Button>("ImportBLMButton");
            if (importBlmButton != null)
            {
                importBlmButton.text = Localize(
                    "amari.window.avatarCustomize.importBlmButton",
                    "Import file(s) from BOOTH Library Manager");
            }

            var itemGroupNameFields = root.Query<TextField>("ItemGroupNameField").ToList();
            foreach (var field in itemGroupNameFields)
            {
                field.label = Localize("amari.window.avatarCustomize.itemGroupNameLabel");
            }

            var scaleMultiplyFields = root.Query<FloatField>("ScaleMultiply").ToList();
            foreach (var field in scaleMultiplyFields)
            {
                field.label = Localize("amari.window.avatarCustomize.scaleMultiplyLabel");
            }

            var includeInBuildTitles = root.Query<Label>("IncludeInBuildTitle").ToList();
            foreach (var ibTitle in includeInBuildTitles)
            {
                ibTitle.text = Localize("amari.window.avatarCustomize.includeInBuildTitle");
            }

            var previewButtons = root.Query<Button>("ItemPreviewStatusButton").ToList();
            previewButtons.AddRange(root.Query<Button>("ItemPreviewStatusButton").ToList());
            foreach (var button in previewButtons)
            {
                var isPreviewing = button.userData is ItemItemElementState state &&
                                   IsItemPreviewing(state.group, state.item);
                SetPreviewButtonState(button, isPreviewing);
            }

            var groupPreviewButtons = root.Query<Button>("ItemGroupPreviewButton").ToList();
            foreach (var button in groupPreviewButtons)
            {
                var isPreviewing = false;
                if (button.userData is ItemGroupElementState state && state.group != null)
                {
                    isPreviewing = state.group.previewEnabled;
                }

                SetPreviewButtonState(button, isPreviewing);
            }

            UpdateAvatarPresetNameLabel(root);
        }

        private void ClearItemGroupPanel(TextField nameField, FloatField scaleField, ListView listView)
        {
            nameField?.SetValueWithoutNotify(string.Empty);
            scaleField?.SetValueWithoutNotify(1f);
            if (listView != null)
            {
                if (listView.userData is ItemListViewState state && state.group != null)
                {
                    _groupToListView.Remove(state.group);
                    state.group = null;
                }

                _listViewToTargetList.Remove(listView);
                _itemListSnapshots.Remove(listView);
                listView.itemsSource = null;
                listView.makeItem = null;
                listView.bindItem = null;
                listView.Rebuild();
            }
        }

        private void BindItemListViewForGroup(ListView itemListView, AmariItemGroupListItem group)
        {
            if (itemListView == null || group == null)
            {
                return;
            }

            group.itemListItems ??= new List<AmariItemListItem>();
            if (EnsureGroupActivePreviewItem(group))
            {
                MarkSettingsDirty();
            }

            var listViewState = GetOrCreateItemListViewState(itemListView);
            if (listViewState.group != null && listViewState.group != group)
            {
                _groupToListView.Remove(listViewState.group);
                _itemListSnapshots.Remove(itemListView);
            }

            itemListView.makeItem = () => itemItemAsset.Instantiate();
            itemListView.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;
            _listViewToTargetList[itemListView] = group.itemListItems;
            UpdateGroupListViewMapping(group, itemListView);
            listViewState.group = group;
            UpdateItemCheckResultsForGroup(group);

            itemListView.bindItem = (element, index) =>
            {
                if (!_listViewToTargetList.TryGetValue(itemListView, out var targetList) || targetList == null)
                {
                    ClearItemListElementVisuals(element);
                    return;
                }

                if (index < 0 || index >= targetList.Count)
                {
                    ClearItemListElementVisuals(element);
                    return;
                }

                var currentGroup = FindItemGroupByList(targetList);
                SyncItemListSnapshot(itemListView, currentGroup);
                var item = targetList[index];
                if (item == null)
                {
                    item = new AmariItemListItem();
                    targetList[index] = item;
                    MarkSettingsDirty();
                    SyncItemListSnapshot(itemListView, currentGroup);
                }

                var prefabField = element.Q<EditorObjectField>("ItemPrefabField");
                if (prefabField == null)
                {
                    Debug.LogError("PrefabField not found in item item UXML");
                    return;
                }

                prefabField.objectType = typeof(GameObject);
                prefabField.allowSceneObjects = false;
                prefabField.SetValueWithoutNotify(item.prefab);
                var prefabState = GetOrCreateItemItemElementState(prefabField);
                prefabState.item = item;
                prefabState.group = currentGroup;
                if (!prefabState.bound)
                {
                    prefabState.bound = true;
                    prefabField.RegisterValueChangedCallback(e =>
                    {
                        if (prefabField.userData is not ItemItemElementState state || state.item == null)
                        {
                            return;
                        }

                        var newPrefab = e.newValue as GameObject;
                        OnItemPrefabValueChanged(prefabField, state.item, newPrefab, state.group);
                    });
                }

                var previewButton = element.Q<Button>("ItemPreviewStatusButton");
                if (previewButton != null)
                {
                    SetPreviewButtonState(previewButton, IsItemPreviewing(currentGroup, item));
                    var previewState = GetOrCreateItemItemElementState(previewButton);
                    previewState.item = item;
                    previewState.group = currentGroup;
                    previewState.listView = itemListView;
                    if (!previewState.bound)
                    {
                        previewState.bound = true;
                        previewButton.clicked += () =>
                        {
                            if (previewButton.userData is not ItemItemElementState state || state.item == null)
                            {
                                return;
                            }

                            if (state.group == null)
                            {
                                return;
                            }

                            if (state.item.instance == null)
                            {
                                return;
                            }

                            RecordSettingsUndo("Change Active Preview Item");
                            // グループプレビューがOFFでも、アイテム側操作で自動的にグループをONへ戻す
                            state.group.previewEnabled = true;
                            state.group.activePreviewItem = state.item;
                            UpdatePreviewInstanceActiveStates(true, "Change Active Preview Item");
                            MarkSettingsDirty();
                            state.listView?.RefreshItems();
                            SetupLocalizationTextItem(rootVisualElement);
                        };
                    }
                }

                var includeInBuildTitle = element.Q<Label>("IncludeInBuildTitle");
                if (includeInBuildTitle != null)
                {
                    includeInBuildTitle.text = Localize("amari.window.avatarCustomize.includeInBuildTitle");
                }

                var itemInfoButton = element.Q<Button>("ItemInfoButton");
                if (itemInfoButton != null)
                {
                    var needsAttention = ShouldNotifyItemInfo(item);
                    SetItemInfoButtonState(itemInfoButton, needsAttention);
                    BindItemInfoButton(itemInfoButton, item, OnItemInfoButtonClicked);
                }

                var includeInBuildToggle = element.Q<Toggle>("IncludeInBuildToggle");
                if (includeInBuildToggle != null)
                {
                    var includeInBuild = item.instance != null && !item.instance.CompareTag("EditorOnly");
                    includeInBuildToggle.SetValueWithoutNotify(includeInBuild);
                    var includeState = GetOrCreateItemItemElementState(includeInBuildToggle);
                    includeState.item = item;
                    if (includeState.bound)
                    {
                        return;
                    }

                    includeState.bound = true;
                    includeInBuildToggle.RegisterValueChangedCallback(e =>
                    {
                        if (includeInBuildToggle.userData is not ItemItemElementState state || state.item == null)
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
            };

            itemListView.itemsSource = group.itemListItems;

            if (!listViewState.bound)
            {
                listViewState.bound = true;

                itemListView.itemsAdded += indices =>
                {
                    if (itemListView.userData is not ItemListViewState state || state.group?.itemListItems == null)
                    {
                        return;
                    }

                    foreach (var index in indices)
                    {
                        if (index < 0 || index >= state.group.itemListItems.Count)
                        {
                            continue;
                        }

                        if (state.group.itemListItems[index] != null)
                        {
                            continue;
                        }

                        state.group.itemListItems[index] = new AmariItemListItem();
                    }

                    MarkSettingsDirty();

                    SyncItemListSnapshot(itemListView, state.group);
                    if (state.group != null && _groupToListView.TryGetValue(state.group, out var listViewForGroup))
                    {
                        listViewForGroup.Rebuild();
                    }
                };

                itemListView.itemsRemoved += indices =>
                {
                    if (itemListView.userData is not ItemListViewState state || state.group?.itemListItems == null)
                    {
                        return;
                    }

                    var removedIndices = indices?.ToList() ?? new List<int>();

                    RecordSettingsUndo("Remove Item Prefab");
                    if (!_itemListSnapshots.TryGetValue(itemListView, out var snapshot))
                    {
                        snapshot = new List<AmariItemListItem>();
                    }

                    var removedItems = ResolveRemovedItems(removedIndices, snapshot, state.group.itemListItems);
                    var previewChanged = false;
                    foreach (var item in removedItems)
                    {
                        if (item.instance)
                        {
                            Undo.DestroyObjectImmediate(item.instance);
                        }

                        if (state.group.activePreviewItem == item)
                        {
                            state.group.activePreviewItem = null;
                            previewChanged = true;
                        }
                    }

                    previewChanged |= EnsureGroupActivePreviewItem(state.group);
                    if (previewChanged)
                    {
                        UpdatePreviewInstanceActiveStates(true, "Remove Item Prefab");
                    }

                    UpdateItemCheckResultsForGroup(state.group);
                    MarkSettingsDirty();
                    SyncItemListSnapshot(itemListView, state.group);
                    if (state.group != null && _groupToListView.TryGetValue(state.group, out var listViewForGroup))
                    {
                        listViewForGroup.Rebuild();
                    }
                };

                RegisterGroupDragTargets(itemListView);
            }

            ApplyScaleMultiplyToGroup(group);
            SyncItemListSnapshot(itemListView, group);
            itemListView.Rebuild();
        }
    }
}
