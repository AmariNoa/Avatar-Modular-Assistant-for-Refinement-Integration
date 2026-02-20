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

        private void OnActivePreviewOutfitDestroy(AmariOutfitListItem item)
        {
            var next = EnumerateAllOutfitItems().FirstOrDefault(ti => ti != item && ti.instance);
            _avatarSettings.activePreviewOutfit = next;
            UpdatePreviewInstanceActiveStates();
        }

        private bool CheckOrActivatePreviewOutfit(AmariOutfitListItem item)
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
            _avatarSettings.activePreviewOutfit = item;
            UpdatePreviewInstanceActiveStates();
            return true;
        }

        private void UpdatePreviewInstanceActiveStates()
        {
            var active = _avatarSettings.activePreviewOutfit;
            foreach (var item in EnumerateAllOutfitItems())
            {
                if (item.instance == null)
                {
                    continue;
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

        private GameObject UpdatePrefabInstanceInScene(AmariOutfitListItem item, GameObject newPrefab)
        {
            if (item.instance)
            {
                DestroyImmediate(item.instance);
            }

            if (!newPrefab)
            {
                OnActivePreviewOutfitDestroy(item);
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(newPrefab, _avatarDescriptor.transform);
            instance.name = newPrefab.name;
            instance.tag = "EditorOnly";

            return instance;
        }

        private void OnOutfitPrefabAdded(List<AmariOutfitListItem> targetList, GameObject prefab, string guid)
        {
            var item = new AmariOutfitListItem();

            var instance = UpdatePrefabInstanceInScene(item, prefab);
            SetOutfitListItemValues(item, prefab, guid, instance);
            ApplyScaleMultiplyToItem(FindOutfitGroupByList(targetList), item);

            instance.SetActive(CheckOrActivatePreviewOutfit(item));

            targetList.Add(item);
        }

        private void OnOutfitPrefabValueChanged(ObjectField prefabField, AmariOutfitListItem item, GameObject prefab, AmariOutfitGroupListItem group)
        {
            if (!prefab || !IsPrefabAsset(prefab))
            {
                // TODO 要挙動チェック 警告ダイアログを出す必要があるかも？
                prefabField.SetValueWithoutNotify(null);
                SetOutfitListItemValues(item, null, string.Empty, null);
                return;
            }

            var newPath = AssetDatabase.GetAssetPath(prefab);
            var newGuid = AssetDatabase.AssetPathToGUID(newPath);
            if (IsDuplicatePrefab(newGuid, item))
            {
                prefabField.SetValueWithoutNotify(item.prefab);
                return;
            }

            var instance = UpdatePrefabInstanceInScene(item, prefab);
            SetOutfitListItemValues(item, prefab, newGuid, instance);
            ApplyScaleMultiplyToItem(group, item);

            instance.SetActive(CheckOrActivatePreviewOutfit(item));
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

        private static void ApplyScaleMultiplyToItem(AmariOutfitGroupListItem group, AmariOutfitListItem item)
        {
            if (group == null || item?.instance == null || item.prefab == null)
            {
                return;
            }

            var baseScale = item.prefab.transform.localScale;
            item.instance.transform.localScale = baseScale * group.scaleMultiply;
        }

        private static void ApplyScaleMultiplyToGroup(AmariOutfitGroupListItem group)
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
                item.instance.transform.localScale = baseScale * group.scaleMultiply;
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
                }

                group.outfitListItems ??= new List<AmariOutfitListItem>();

                var outfitGroupName = groupElement.Q<TextField>("OutfitGroupNameField");
                if (outfitGroupName != null)
                {
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

                            var uniqueName = GetUnusedOutfitGroupName(desired);
                            group.groupName = uniqueName;
                            if (!string.Equals(uniqueName, outfitGroupName.value, System.StringComparison.Ordinal))
                            {
                                outfitGroupName.SetValueWithoutNotify(uniqueName);
                            }
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
                    scaleMultiplyField.SetValueWithoutNotify(group.scaleMultiply);
                    if (scaleMultiplyField.userData == null)
                    {
                        scaleMultiplyField.userData = "bound";
                        scaleMultiplyField.RegisterValueChangedCallback(e =>
                        {
                            group.scaleMultiply = e.newValue;
                            ApplyScaleMultiplyToGroup(group);
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

                            _avatarSettings.activePreviewOutfit = item;
                            UpdatePreviewInstanceActiveStates();
                            outfitListView.RefreshItems();
                        };
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

                            DestroyImmediate(item.instance);
                            if (_avatarSettings.activePreviewOutfit == item)
                            {
                                OnActivePreviewOutfitDestroy(item);
                            }
                        }

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
            };

            _outfitGroupListView.itemsRemoved += indices =>
            {
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
                            DestroyImmediate(item.instance);
                        }

                        if (_avatarSettings.activePreviewOutfit == item)
                        {
                            OnActivePreviewOutfitDestroy(item);
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
            };

            _outfitGroupListView.Rebuild();

            // 翻訳テキスト更新
            SetupLocalizationTextOutfit(root);
        }
    }
}
