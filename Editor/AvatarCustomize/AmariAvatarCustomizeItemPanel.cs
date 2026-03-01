using System.Collections.Generic;
using System.Linq;
using com.amari_noa.avatar_modular_assistant.runtime;
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

        private static AmariItemListItem FindGroupPreviewCandidate(AmariItemGroupListItem group, AmariItemListItem preferredItem = null)
        {
            if (group?.itemListItems == null)
            {
                return null;
            }

            if (IsGroupActivePreviewItem(group, preferredItem))
            {
                return preferredItem;
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
                ? AmariLocalization.Get("amari.window.avatarCustomize.previewButtonPreviewing")
                : AmariLocalization.Get("amari.window.avatarCustomize.previewButtonPreview");
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

        private static void BindItemInfoButton(Button button, AmariItemListItem item, System.Action<AmariItemListItem> onClick)
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

                onClick?.Invoke(s.item);
            };
        }

        private static void OnItemInfoButtonClicked(AmariItemListItem item)
        {
            // TODO: implement the actual behavior to guide the user to pending actions.
            if (item == null)
            {
                return;
            }

            Debug.Log($"[AMARI] Item info clicked: {item.prefabGuid}");
        }

        private bool ShouldNotifyItemInfo(AmariItemListItem item)
        {
            if (item == null)
            {
                return false;
            }

            if (!_itemCheckResults.TryGetValue(item, out var result))
            {
                return false;
            }

            return result.Suggestion != AmariModularAvatarSuggestedAction.None;
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
                listViewForGroup.RefreshItems();
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
                listView.RefreshItems();
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

        private void SetupLocalizationTextItem(VisualElement root)
        {
            var itemPanelTitle = root.Q<Label>("ItemPanelTitle");
            itemPanelTitle.text = AmariLocalization.Get("amari.window.avatarCustomize.panelItemTitle");

            var editorLanguage = root.Q<DropdownField>("EditorLanguage");
            if (editorLanguage != null)
            {
                editorLanguage.label = AmariLocalization.Get("amari.window.avatarCustomize.editorLanguageLabel");
            }

            var itemGroupNameFields = root.Query<TextField>("ItemGroupNameField").ToList();
            foreach (var field in itemGroupNameFields)
            {
                field.label = AmariLocalization.Get("amari.window.avatarCustomize.itemGroupNameLabel");
            }

            var scaleMultiplyFields = root.Query<FloatField>("ScaleMultiply").ToList();
            foreach (var field in scaleMultiplyFields)
            {
                field.label = AmariLocalization.Get("amari.window.avatarCustomize.scaleMultiplyLabel");
            }

            var includeInBuildTitles = root.Query<Label>("IncludeInBuildTitle").ToList();
            foreach (var ibTitle in includeInBuildTitles)
            {
                ibTitle.text = AmariLocalization.Get("amari.window.avatarCustomize.includeInBuildTitle");
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
        }

        private void ClearItemGroupPanel(TextField nameField, FloatField scaleField, ListView listView)
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
                    return;
                }

                if (index < 0 || index >= targetList.Count)
                {
                    return;
                }

                var item = targetList[index];
                if (item == null)
                {
                    item = new AmariItemListItem();
                    targetList[index] = item;
                    MarkSettingsDirty();
                }

                var currentGroup = FindItemGroupByList(targetList);

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
                    includeInBuildTitle.text = AmariLocalization.Get("amari.window.avatarCustomize.includeInBuildTitle");
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

                itemListView.itemsRemoved += indices =>
                {
                    if (itemListView.userData is not ItemListViewState state || state.group?.itemListItems == null)
                    {
                        return;
                    }

                    RecordSettingsUndo("Remove Item Prefab");
                    if (!_itemListSnapshots.TryGetValue(itemListView, out var snapshot))
                    {
                        snapshot = state.group.itemListItems.ToList();
                    }

                    var previewChanged = false;
                    foreach (var i in indices)
                    {
                        if (i < 0 || i >= snapshot.Count)
                        {
                            continue;
                        }

                        var item = snapshot[i];
                        if (item == null)
                        {
                            continue;
                        }

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
                    _itemListSnapshots[itemListView] = state.group.itemListItems.ToList();
                    if (state.group != null && _groupToListView.TryGetValue(state.group, out var listViewForGroup))
                    {
                        listViewForGroup.RefreshItems();
                    }
                };

                RegisterGroupDragTargets(itemListView);
            }

            ApplyScaleMultiplyToGroup(group);
            _itemListSnapshots[itemListView] = group.itemListItems.ToList();
            itemListView.Rebuild();
        }
    }
}
