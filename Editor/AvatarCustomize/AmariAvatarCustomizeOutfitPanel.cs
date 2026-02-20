using System.Collections.Generic;
using System.Linq;
using com.amari_noa.avatar_modular_assistant.runtime;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

// ReSharper disable once CheckNamespace
namespace com.amari_noa.avatar_modular_assistant.editor
{
    public partial class AmariAvatarCustomizeWindow
    {
        private const string DefaultGroupName = "Default";

        private void EnsureActivePreviewOutfit()
        {
            if (_avatarSettings.OutfitListGroupItems == null)
            {
                _avatarSettings.activePreviewOutfit = null;
                return;
            }

            var active = _avatarSettings.activePreviewOutfit;
            if (active == null || !EnumerateAllOutfitItems().Contains(active) || active.instance == null)
            {
                active = EnumerateAllOutfitItems().FirstOrDefault(item => item.instance != null);
                _avatarSettings.activePreviewOutfit = active;
            }

            UpdatePreviewInstanceActiveStates();
        }

        private static void SetOutfitListItemValues(AmariOutfitListItem item, GameObject prefab, string guid, GameObject instance)
        {
            item.prefab = prefab;
            item.prefabGuid = guid;
            item.instance = instance;
        }

        private IEnumerable<AmariOutfitListItem> EnumerateAllOutfitItems()
        {
            if (_avatarSettings?.OutfitListGroupItems == null)
            {
                yield break;
            }

            foreach (var item in _avatarSettings.OutfitListGroupItems.Where(group => group?.outfitListItems != null).SelectMany(group => group.outfitListItems.Where(item => item != null)))
            {
                yield return item;
            }
        }

        private bool IsDuplicatePrefab(string guid)
        {
            return string.IsNullOrWhiteSpace(guid) || EnumerateAllOutfitItems().Any(item => string.Equals(item.prefabGuid, guid, System.StringComparison.Ordinal));
        }

        private bool IsDuplicatePrefab(string guid, AmariOutfitListItem self)
        {
            return string.IsNullOrWhiteSpace(guid) || EnumerateAllOutfitItems().Any(item => item != self && string.Equals(item.prefabGuid, guid, System.StringComparison.Ordinal));
        }

        private void OnActivePreviewOutfitDestroy(AmariOutfitListItem item, bool registerUndo = false, string undoName = null)
        {
            if (registerUndo)
            {
                RecordSettingsUndo(undoName ?? "Change Active Preview Outfit");
            }

            var next = EnumerateAllOutfitItems().FirstOrDefault(ti => ti != item && ti.instance);
            _avatarSettings.activePreviewOutfit = next;
            UpdatePreviewInstanceActiveStates(registerUndo, undoName);

            if (registerUndo)
            {
                MarkSettingsDirty();
            }
        }

        private bool CheckOrActivatePreviewOutfit(AmariOutfitListItem item, bool registerUndo = false, string undoName = null)
        {
            if (_avatarSettings.activePreviewOutfit != null)
            {
                // アクティブが存在してもリスト外・実体無しなら無効化
                var active = _avatarSettings.activePreviewOutfit;
                if (EnumerateAllOutfitItems().Contains(active) && active.instance)
                {
                    return false;
                }

                _avatarSettings.activePreviewOutfit = null;
            }

            // アクティブが存在しなければ更新アイテムをアクティブとして扱う
            if (registerUndo)
            {
                RecordSettingsUndo(undoName ?? "Change Active Preview Outfit");
            }

            _avatarSettings.activePreviewOutfit = item;
            UpdatePreviewInstanceActiveStates(registerUndo, undoName);

            if (registerUndo)
            {
                MarkSettingsDirty();
            }

            return true;
        }

        private void UpdatePreviewInstanceActiveStates(bool registerUndo = false, string undoName = null)
        {
            var active = _avatarSettings.activePreviewOutfit;
            foreach (var item in EnumerateAllOutfitItems())
            {
                if (item.instance == null)
                {
                    continue;
                }

                if (registerUndo)
                {
                    Undo.RecordObject(item.instance, undoName ?? "Toggle Preview Outfit");
                    MarkObjectDirty(item.instance);
                }

                item.instance.SetActive(item == active);
            }

            // Refresh UI so preview buttons reflect current active item
            if (_outfitGroupListView != null)
            {
                _outfitGroupListView.RefreshItems();
            }

            foreach (var listView in _outfitListSnapshots.Keys)
            {
                listView?.RefreshItems();
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

        private static void SetInstanceActive(GameObject instance, bool isActive, bool registerUndo, string undoName)
        {
            if (instance == null)
            {
                return;
            }

            if (registerUndo)
            {
                Undo.RecordObject(instance, undoName ?? "Toggle Preview Outfit");
            }

            instance.SetActive(isActive);

            if (registerUndo)
            {
                MarkObjectDirty(instance);
            }
        }

        private GameObject UpdatePrefabInstanceInScene(AmariOutfitListItem item, GameObject newPrefab, bool registerUndo = false, string undoName = null)
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
                OnActivePreviewOutfitDestroy(item, registerUndo, undoName);
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(newPrefab, _avatarDescriptor.transform);
            instance.name = newPrefab.name;
            instance.tag = "EditorOnly";

            if (registerUndo)
            {
                Undo.RegisterCreatedObjectUndo(instance, undoName ?? "Create Outfit Prefab");
                MarkObjectDirty(instance);
            }

            return instance;
        }

        private void OnOutfitPrefabAdded(List<AmariOutfitListItem> targetList, GameObject prefab, string guid)
        {
            RecordSettingsUndo("Add Outfit Prefab");

            var item = new AmariOutfitListItem();

            var instance = UpdatePrefabInstanceInScene(item, prefab, true, "Create Outfit Prefab");
            SetOutfitListItemValues(item, prefab, guid, instance);
            ApplyScaleMultiplyToItem(FindOutfitGroupByList(targetList), item, true, "Apply Outfit Scale");

            var shouldActivate = CheckOrActivatePreviewOutfit(item, true, "Change Active Preview Outfit");
            SetInstanceActive(instance, shouldActivate, true, "Change Active Preview Outfit");

            targetList.Add(item);
            MarkSettingsDirty();
        }

        private void OnOutfitPrefabValueChanged(ObjectField prefabField, AmariOutfitListItem item, GameObject prefab, AmariOutfitGroupListItem group)
        {
            RecordSettingsUndo("Change Outfit Prefab");

            if (!prefab || !IsPrefabAsset(prefab))
            {
                // TODO 要挙動チェック 警告ダイアログを出す必要があるかも？
                prefabField.SetValueWithoutNotify(null);
                SetOutfitListItemValues(item, null, string.Empty, null);
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

            var instance = UpdatePrefabInstanceInScene(item, prefab, true, "Change Outfit Prefab");
            SetOutfitListItemValues(item, prefab, newGuid, instance);
            ApplyScaleMultiplyToItem(group, item, true, "Apply Outfit Scale");

            var shouldActivate = CheckOrActivatePreviewOutfit(item, true, "Change Active Preview Outfit");
            SetInstanceActive(instance, shouldActivate, true, "Change Active Preview Outfit");
            MarkSettingsDirty();
        }

        private AmariOutfitGroupListItem FindOutfitGroupByList(List<AmariOutfitListItem> targetList)
        {
            if (_avatarSettings?.OutfitListGroupItems == null || targetList == null)
            {
                return null;
            }

            foreach (var group in _avatarSettings.OutfitListGroupItems)
            {
                if (group?.outfitListItems == targetList)
                {
                    return group;
                }
            }

            return null;
        }

        private static void ApplyScaleMultiplyToItem(AmariOutfitGroupListItem group, AmariOutfitListItem item, bool registerUndo = false, string undoName = null)
        {
            if (group == null || item?.instance == null || item.prefab == null)
            {
                return;
            }

            var baseScale = item.prefab.transform.localScale;
            if (registerUndo)
            {
                Undo.RecordObject(item.instance.transform, undoName ?? "Apply Outfit Scale");
            }
            item.instance.transform.localScale = baseScale * group.scaleMultiply;
            if (registerUndo)
            {
                MarkObjectDirty(item.instance.transform);
            }
        }

        private static void ApplyScaleMultiplyToGroup(AmariOutfitGroupListItem group, bool registerUndo = false, string undoName = null)
        {
            if (group?.outfitListItems == null)
            {
                return;
            }

            foreach (var item in group.outfitListItems)
            {
                if (item?.instance == null || item.prefab == null)
                {
                    continue;
                }

                var baseScale = item.prefab.transform.localScale;
                if (registerUndo)
                {
                    Undo.RecordObject(item.instance.transform, undoName ?? "Apply Outfit Scale");
                }
                item.instance.transform.localScale = baseScale * group.scaleMultiply;
                if (registerUndo)
                {
                    MarkObjectDirty(item.instance.transform);
                }
            }
        }

        private bool AddOutfitPrefab(List<AmariOutfitListItem> targetList, GameObject obj)
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

            OnOutfitPrefabAdded(targetList, obj, guid);
            return true;
        }

        private void AddPrefabsFromDrag(Object[] refs, List<AmariOutfitListItem> targetList, ListView listView)
        {
            if (refs == null || refs.Length == 0)
            {
                return;
            }

            var added = false;
            foreach (var obj in refs)
            {
                if (!AddOutfitPrefab(targetList, (GameObject)obj))
                {
                    continue;
                }
                added = true;
            }

            if (added)
            {
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

                var listView = target as ListView ?? target.Q<ListView>("OutfitListView");
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
        private string GetUnusedOutfitGroupName(string groupName = DefaultGroupName)
        {
            var groups = _avatarSettings?.OutfitListGroupItems;
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

        private void SetupLocalizationTextOutfit(VisualElement root)
        {
            var outfitPanelTitle = root.Q<Label>("OutfitPanelTitle");
            outfitPanelTitle.text = AmariLocalization.Get("amari.window.avatarCustomize.panelOutfitTitle");

            var editorLanguage = root.Q<DropdownField>("EditorLanguage");
            if (editorLanguage != null)
            {
                editorLanguage.label = AmariLocalization.Get("amari.window.avatarCustomize.editorLanguageLabel");
            }

            var outfitGroupNameFields = root.Query<TextField>("OutfitGroupNameField").ToList();
            foreach (var field in outfitGroupNameFields)
            {
                field.label = AmariLocalization.Get("amari.window.avatarCustomize.outfitGroupNameLabel");
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

            var previewButtons = root.Query<Button>("OutfitPreviewStatusButton").ToList();
            if (previewButtons.Count == 0)
            {
                return;
            }

            foreach (var button in previewButtons)
            {
                var isPreviewing = false;
                var parent = button.parent;
                while (parent != null)
                {
                    var prefabField = parent.Q<ObjectField>("OutfitPrefabField");
                    if (prefabField != null)
                    {
                        var prefab = prefabField.value as GameObject;
                        if (prefab != null && _avatarSettings != null)
                        {
                            isPreviewing = _avatarSettings.activePreviewOutfit != null &&
                                           _avatarSettings.activePreviewOutfit.prefab == prefab;
                        }
                        break;
                    }
                    parent = parent.parent;
                }

                SetPreviewButtonState(button, isPreviewing);
            }
        }

        private void BindOutfitList(VisualElement root)
        {
            if (_avatarSettings?.OutfitListGroupItems == null)
            {
                return;
            }

            _outfitGroupListView = root.Q<ListView>("OutfitGroupListView");
            _outfitGroupListView.itemsSource = _avatarSettings.OutfitListGroupItems;
            _outfitGroupListView.makeItem = () => outfitGroupItemAsset.Instantiate();

            _outfitGroupListView.bindItem = (groupElement, groupIndex) =>
            {
                if (groupIndex < 0 || groupIndex >= _avatarSettings.OutfitListGroupItems.Count)
                {
                    return;
                }

                var group = _avatarSettings.OutfitListGroupItems[groupIndex];
                if (group == null)
                {
                    group = new AmariOutfitGroupListItem
                    {
                        groupName = GetUnusedOutfitGroupName(),
                        outfitListItems = new List<AmariOutfitListItem>(),
                        scaleMultiply = 1f
                    };
                    _avatarSettings.OutfitListGroupItems[groupIndex] = group;
                    MarkSettingsDirty();
                }

                group.outfitListItems ??= new List<AmariOutfitListItem>();

                        var outfitGroupName = groupElement.Q<TextField>("OutfitGroupNameField");
                        if (outfitGroupName != null)
                        {
                    if (string.IsNullOrWhiteSpace(group.groupName))
                    {
                        RecordSettingsUndo("Fix Empty Outfit Group Name");
                        group.groupName = GetUnusedOutfitGroupName();
                        MarkSettingsDirty();
                    }

                            outfitGroupName.label = AmariLocalization.Get("amari.window.avatarCustomize.outfitGroupNameLabel");
                            outfitGroupName.SetValueWithoutNotify(group.groupName);
                            if (outfitGroupName.userData == null)
                            {
                                outfitGroupName.userData = "bound";

                        void CommitOutfitGroupName()
                        {
                            var desired = outfitGroupName.value?.Trim();
                            if (string.IsNullOrWhiteSpace(desired))
                            {
                                desired = DefaultGroupName;
                            }

                            if (string.Equals(desired, group.groupName, System.StringComparison.Ordinal))
                            {
                                return;
                            }

                            RecordSettingsUndo("Change Outfit Group Name");

                            var uniqueName = GetUnusedOutfitGroupName(desired);
                            group.groupName = uniqueName;
                            if (!string.Equals(uniqueName, outfitGroupName.value, System.StringComparison.Ordinal))
                            {
                                outfitGroupName.SetValueWithoutNotify(uniqueName);
                            }

                            MarkSettingsDirty();
                        }

                        outfitGroupName.RegisterCallback<FocusOutEvent>(_ => CommitOutfitGroupName());
                        outfitGroupName.RegisterCallback<KeyDownEvent>(e =>
                        {
                            if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter)
                            {
                                return;
                            }

                            CommitOutfitGroupName();
                        });
                    }
                }

                        var scaleMultiplyField = groupElement.Q<FloatField>("ScaleMultiply");
                        if (scaleMultiplyField != null)
                        {
                            scaleMultiplyField.label = AmariLocalization.Get("amari.window.avatarCustomize.scaleMultiplyLabel");
                            scaleMultiplyField.SetValueWithoutNotify(group.scaleMultiply);
                            if (scaleMultiplyField.userData == null)
                            {
                        scaleMultiplyField.userData = "bound";
                        scaleMultiplyField.RegisterValueChangedCallback(e =>
                        {
                            RecordSettingsUndo("Change Outfit Scale");
                            group.scaleMultiply = e.newValue;
                            ApplyScaleMultiplyToGroup(group, true, "Apply Outfit Scale");
                            MarkSettingsDirty();
                        });
                    }
                }


                var outfitListView = groupElement.Q<ListView>("OutfitListView");
                if (outfitListView == null)
                {
                    Debug.LogError("OutfitListView not found in OutfitGroupListItem UXML");
                    return;
                }

                outfitListView.itemsSource = group.outfitListItems;
                outfitListView.makeItem = () => outfitItemAsset.Instantiate();
                _listViewToTargetList[outfitListView] = group.outfitListItems;

                if (outfitListView.userData == null)
                {
                    outfitListView.userData = "bound";
                    _groupToListView[group] = outfitListView;

                    outfitListView.bindItem = (element, index) =>
                    {
                        var item = group.outfitListItems[index];
                        if (item == null)
                        {
                            item = new AmariOutfitListItem();
                            group.outfitListItems[index] = item;
                        }

                        var prefabField = element.Q<ObjectField>("OutfitPrefabField");
                        if (prefabField == null)
                        {
                            Debug.LogError("PrefabField not found in outfit item UXML");
                            return;
                        }

                        prefabField.objectType = typeof(GameObject);
                        prefabField.allowSceneObjects = false;
                        prefabField.SetValueWithoutNotify(item.prefab);
                        if (prefabField.userData != null)
                        {
                            return;
                        }

                        prefabField.userData = "bound";
                        prefabField.RegisterValueChangedCallback(e =>
                        {
                            var newPrefab = e.newValue as GameObject;
                            OnOutfitPrefabValueChanged(prefabField, item, newPrefab, group);
                        });

                        var previewButton = element.Q<Button>("OutfitPreviewStatusButton");
                        if (previewButton == null)
                        {
                            return;
                        }

                        SetPreviewButtonState(previewButton, _avatarSettings.activePreviewOutfit == item);
                        if (previewButton.userData != null)
                        {
                            return;
                        }

                        previewButton.userData = "bound";
                        previewButton.clicked += () =>
                        {
                            if (item.instance == null)
                            {
                                return;
                            }

                            RecordSettingsUndo("Change Active Preview Outfit");
                            _avatarSettings.activePreviewOutfit = item;
                            UpdatePreviewInstanceActiveStates(true, "Change Active Preview Outfit");
                            MarkSettingsDirty();
                            outfitListView.RefreshItems();
                        };

                        var includeInBuildTitle = element.Q<Label>("IncludeInBuildTitle");
                        if (includeInBuildTitle != null)
                        {
                            includeInBuildTitle.text = AmariLocalization.Get("amari.window.avatarCustomize.includeInBuildTitle");
                        }

                        var includeInBuildToggle = element.Q<Toggle>("IncludeInBuildToggle");
                        if (includeInBuildToggle != null)
                        {
                            var includeInBuild = item.instance != null && !item.instance.CompareTag("EditorOnly");
                            includeInBuildToggle.SetValueWithoutNotify(includeInBuild);
                            if (includeInBuildToggle.userData != null)
                            {
                                return;
                            }
                            includeInBuildToggle.userData = "bound";
                            includeInBuildToggle.RegisterValueChangedCallback(e =>
                            {
                                if (item.instance == null)
                                {
                                    includeInBuildToggle.SetValueWithoutNotify(false);
                                    return;
                                }

                                Undo.RecordObject(item.instance, "Toggle Include In Build");
                                item.instance.tag = e.newValue ? "Untagged" : "EditorOnly";
                                MarkObjectDirty(item.instance);
                            });
                        }
                    };

                    outfitListView.itemsAdded += indices =>
                    {
                        foreach (var i in indices)
                        {
                            EnsureListSize(group.outfitListItems, i);
                            group.outfitListItems[i] ??= new AmariOutfitListItem();
                        }

                        _outfitListSnapshots[outfitListView] = group.outfitListItems.ToList();
                    };

                    outfitListView.itemsRemoved += indices =>
                    {
                        RecordSettingsUndo("Remove Outfit Prefab");
                        if (!_outfitListSnapshots.TryGetValue(outfitListView, out var snapshot))
                        {
                            snapshot = group.outfitListItems.ToList();
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
                        _outfitListSnapshots[outfitListView] = group.outfitListItems.ToList();
                    };

                    RegisterGroupDragTargets(outfitListView);
                }

                // 各ListItemに実体インスタンスが存在するかチェックして、存在しないものはリストから消す
                group.outfitListItems.RemoveAll(item => item?.instance == null);
                ApplyScaleMultiplyToGroup(group);
                _outfitListSnapshots[outfitListView] = group.outfitListItems.ToList();
                outfitListView.Rebuild();

                RegisterGroupDragTargets(groupElement);
            };

            _outfitGroupListView.itemsAdded += indices =>
            {
                RecordSettingsUndo("Add Outfit Group");
                foreach (var i in indices)
                {
                    EnsureListSize(_avatarSettings.OutfitListGroupItems, i);
                    _avatarSettings.OutfitListGroupItems[i] ??= new AmariOutfitGroupListItem
                    {
                        groupName = GetUnusedOutfitGroupName(),
                        outfitListItems = new List<AmariOutfitListItem>(),
                        scaleMultiply = 1f
                    };
                }
                MarkSettingsDirty();
            };

            _outfitGroupListView.itemsRemoved += indices =>
            {
                RecordSettingsUndo("Remove Outfit Group");
                var snapshot = _avatarSettings.OutfitListGroupItems.ToList();
                foreach (var i in indices)
                {
                    if (i < 0 || i >= snapshot.Count)
                    {
                        continue;
                    }

                    var group = snapshot[i];
                    if (group?.outfitListItems == null)
                    {
                        continue;
                    }

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

                    if (!_groupToListView.TryGetValue(group, out var listView))
                    {
                        continue;
                    }

                    _outfitListSnapshots.Remove(listView);
                    _groupToListView.Remove(group);
                    _listViewToTargetList.Remove(listView);
                }

                MarkSettingsDirty();
            };

            _outfitGroupListView.Rebuild();

            // 翻訳テキスト更新
            SetupLocalizationTextOutfit(root);
        }
    }
}
