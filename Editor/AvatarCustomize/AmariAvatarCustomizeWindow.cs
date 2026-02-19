using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using System.Threading.Tasks;
using com.amari_noa.avatar_modular_assistant.runtime;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
using VRC.Core;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.Api;

// ReSharper disable once CheckNamespace
namespace com.amari_noa.avatar_modular_assistant.editor
{
    public class AmariAvatarCustomizeWindow : EditorWindow
    {
        [SerializeField] private VisualTreeAsset visualTreeAsset;
        [Space]
        [SerializeField] private VisualTreeAsset costumeGroupItemAsset;
        [SerializeField] private VisualTreeAsset costumeItemAsset;

        private VRCAvatarDescriptor _avatarDescriptor;
        private AmariAvatarSettings _avatarSettings;
        private ListView _costumeGroupListView;
        private readonly Dictionary<ListView, List<AmariCostumeListItem>> _costumeListSnapshots = new();
        private readonly Dictionary<AmariCostumeGroupListItem, ListView> _groupToListView = new();
        private readonly Dictionary<ListView, List<AmariCostumeListItem>> _listViewToTargetList = new();
        private readonly HashSet<VisualElement> _dragTargets = new();
        private static VRCAvatarDescriptor _pendingAvatarDescriptor;

        private const string WindowTitle = "[AMARI] Avatar Customize";
        private const string AmariSettingsPrefabGuid = "2fe354710d7e2d9439856f459edadc0d";


        // TODO 将来的にWindow側でアバター選択が出来るようになったらここを復活させるかも
        /*
        [MenuItem("Tools/Avatar Modular Assistant/Avatar Customize Window")]
        public static void Open()
        {
            OpenWithAvatarDescriptor(null);
        }
        */

        public static void OpenWithAvatarDescriptor(VRCAvatarDescriptor target)
        {
            _pendingAvatarDescriptor = target;
            var w = GetWindow<AmariAvatarCustomizeWindow>(false, WindowTitle, true);
            w.Show();
        }

        private static void SetupLocalizationText(VisualElement root)
        {
            var costumePanelTitle = root.Q<Label>("CostumePanelTitle");
            costumePanelTitle.text = AmariLocalization.Get("amari.window.avatarCustomize.panelCostumeTitle");
        }


        #region AvatarDetails UI Controls
        private void TrySetAvatarDetails(VisualElement avatarThumbnail, Label avatarName)
        {
            if (avatarThumbnail == null || avatarName == null || _avatarDescriptor == null)
            {
                avatarThumbnail?.Clear();
                avatarName?.Clear();

                return;
            }

            if (!_avatarDescriptor.TryGetComponent<PipelineManager>(out var pipelineManager))
            {
                return;
            }

            var blueprintId = pipelineManager.blueprintId;
            if (string.IsNullOrWhiteSpace(blueprintId))
            {
                return;
            }

            _ = SetAvatarThumbnailAsync(blueprintId, avatarThumbnail, avatarName);
        }

        private static async Task SetAvatarThumbnailAsync(string blueprintId, VisualElement avatarThumbnail, Label avatarName)
        {
            var avatar = await VRCApi.GetAvatar(blueprintId);
            if (string.IsNullOrWhiteSpace(avatar.ThumbnailImageUrl))
            {
                return;
            }

            var texture = await VRCApi.GetImage(avatar.ThumbnailImageUrl);
            avatarThumbnail.style.backgroundImage = new StyleBackground(texture);

            avatarName.text = avatar.Name;
        }
        #endregion


        #region CostumeList UI controls
        private static void SetCostumeListItemValues(AmariCostumeListItem item, GameObject prefab, string guid, GameObject instance)
        {
            item.prefab = prefab;
            item.prefabGuid = guid;
            item.instance = instance;
        }

        private IEnumerable<AmariCostumeListItem> EnumerateAllCostumeItems()
        {
            if (_avatarSettings?.CostumeListGroupItems == null)
            {
                yield break;
            }

            foreach (var item in _avatarSettings.CostumeListGroupItems.Where(group => group?.costumeListItems != null).SelectMany(group => group.costumeListItems.Where(item => item != null)))
            {
                yield return item;
            }
        }

        private bool IsDuplicatePrefab(string guid)
        {
            return string.IsNullOrWhiteSpace(guid) || EnumerateAllCostumeItems().Any(item => string.Equals(item.prefabGuid, guid, System.StringComparison.Ordinal));
        }

        private bool IsDuplicatePrefab(string guid, AmariCostumeListItem self)
        {
            return string.IsNullOrWhiteSpace(guid) || EnumerateAllCostumeItems().Any(item => item != self && string.Equals(item.prefabGuid, guid, System.StringComparison.Ordinal));
        }

        private static bool IsPrefabAsset(Object obj)
        {
            if (obj is not GameObject go)
            {
                return false;
            }

            return EditorUtility.IsPersistent(go) && PrefabUtility.IsPartOfPrefabAsset(go);
        }

        private void OnActivePreviewCostumeDestroy(AmariCostumeListItem item)
        {
            var next = EnumerateAllCostumeItems().FirstOrDefault(ti => ti != item && ti.instance);
            _avatarSettings.activePreviewCostume = next;
            UpdatePreviewInstanceActiveStates();
        }

        private bool CheckOrActivatePreviewCostume(AmariCostumeListItem item)
        {
            if (_avatarSettings.activePreviewCostume != null)
            {
                // アクティブが存在してもリスト外・実体無しなら無効化
                var active = _avatarSettings.activePreviewCostume;
                if (EnumerateAllCostumeItems().Contains(active) && active.instance)
                {
                    return false;
                }

                _avatarSettings.activePreviewCostume = null;
            }

            // アクティブが存在しなければ更新アイテムをアクティブとして扱う
            _avatarSettings.activePreviewCostume = item;
            UpdatePreviewInstanceActiveStates();
            return true;
        }

        private void UpdatePreviewInstanceActiveStates()
        {
            var active = _avatarSettings.activePreviewCostume;
            foreach (var item in EnumerateAllCostumeItems())
            {
                if (item.instance == null)
                {
                    continue;
                }

                item.instance.SetActive(item == active);
            }
        }

        private GameObject UpdatePrefabInstanceInScene(AmariCostumeListItem item, GameObject newPrefab)
        {
            if (item.instance)
            {
                DestroyImmediate(item.instance);
            }

            if (!newPrefab)
            {
                OnActivePreviewCostumeDestroy(item);
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(newPrefab, _avatarDescriptor.transform);
            instance.name = newPrefab.name;
            instance.tag = "EditorOnly";

            return instance;
        }

        private void OnCostumePrefabAdded(List<AmariCostumeListItem> targetList, GameObject prefab, string guid)
        {
            var item = new AmariCostumeListItem();

            var instance = UpdatePrefabInstanceInScene(item, prefab);
            SetCostumeListItemValues(item, prefab, guid, instance);

            instance.SetActive(CheckOrActivatePreviewCostume(item));

            targetList.Add(item);
        }

        private void OnCostumePrefabValueChanged(ObjectField prefabField, AmariCostumeListItem item, GameObject prefab)
        {
            if (!prefab || !IsPrefabAsset(prefab))
            {
                // TODO 要挙動チェック 警告ダイアログを出す必要があるかも？
                prefabField.SetValueWithoutNotify(null);
                SetCostumeListItemValues(item, null, string.Empty, null);
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
            SetCostumeListItemValues(item, prefab, newGuid, instance);

            instance.SetActive(CheckOrActivatePreviewCostume(item));
        }

        private bool AddCostumePrefab(List<AmariCostumeListItem> targetList, GameObject obj)
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

            OnCostumePrefabAdded(targetList, obj, guid);
            return true;
        }

        private void AddPrefabsFromDrag(Object[] refs, List<AmariCostumeListItem> targetList, ListView listView)
        {
            if (refs == null || refs.Length == 0)
            {
                return;
            }

            var added = false;
            foreach (var obj in refs)
            {
                if (!AddCostumePrefab(targetList, (GameObject)obj))
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

        private void RegisterGroupDragTargets(VisualElement target, ListView listView)
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

                if (!_listViewToTargetList.TryGetValue(listView, out var targetList))
                {
                    return;
                }

                DragAndDrop.AcceptDrag();
                AddPrefabsFromDrag(DragAndDrop.objectReferences, targetList, listView);
            });
        }

        // TODO この命名処理あんまりスマートじゃないのでいつか改修したい
        private string GetUnusedGroupName(string groupName = "Default")
        {
            var exists = _avatarSettings.CostumeListGroupItems.Any(groupInner =>
                groupInner != null && string.Equals(groupInner.groupName, groupName, System.StringComparison.Ordinal));
            if (!exists)
            {
                return groupName;
            }

            for (var i = 1; i < int.MaxValue; i++)
            {
                var tmpGroupName = $"{groupName} {i}";

                var existsTmp = _avatarSettings.CostumeListGroupItems.Any(groupInner =>
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

        private void BindCostumeList(VisualElement root)
        {
            _costumeGroupListView = root.Q<ListView>("CostumeGroupListView");
            _costumeGroupListView.itemsSource = _avatarSettings.CostumeListGroupItems;
            _costumeGroupListView.makeItem = () => costumeGroupItemAsset.Instantiate();

            _costumeGroupListView.bindItem = (groupElement, groupIndex) =>
            {
                if (groupIndex < 0 || groupIndex >= _avatarSettings.CostumeListGroupItems.Count)
                {
                    return;
                }

                var group = _avatarSettings.CostumeListGroupItems[groupIndex];
                if (group == null)
                {
                    group = new AmariCostumeGroupListItem
                    {
                        groupName = GetUnusedGroupName(),
                        costumeListItems = new List<AmariCostumeListItem>()
                    };
                    _avatarSettings.CostumeListGroupItems[groupIndex] = group;
                }

                group.costumeListItems ??= new List<AmariCostumeListItem>();

                var costumeGroupName = groupElement.Q<TextField>("CostumeGroupNameField");
                if (costumeGroupName != null)
                {
                    costumeGroupName.SetValueWithoutNotify(group.groupName);
                    if (costumeGroupName.userData == null)
                    {
                        // TODO これ何？
                        costumeGroupName.userData = "bound";
                        costumeGroupName.RegisterValueChangedCallback(e =>
                        {
                            var desired = e.newValue?.Trim();
                            if (string.IsNullOrWhiteSpace(desired))
                            {
                                desired = "Default";
                            }

                            if (string.Equals(desired, group.groupName, System.StringComparison.Ordinal))
                            {
                                return;
                            }

                            var uniqueName = GetUnusedGroupName(desired);
                            group.groupName = uniqueName;
                            if (!string.Equals(uniqueName, e.newValue, System.StringComparison.Ordinal))
                            {
                                costumeGroupName.SetValueWithoutNotify(uniqueName);
                            }
                        });
                    }
                }


                var costumeListView = groupElement.Q<ListView>("CostumeListView");
                if (costumeListView == null)
                {
                    Debug.LogError("CostumeListView not found in CostumeGroupListItem UXML");
                    return;
                }

                costumeListView.itemsSource = group.costumeListItems;
                costumeListView.makeItem = () => costumeItemAsset.Instantiate();
                _listViewToTargetList[costumeListView] = group.costumeListItems;

                if (costumeListView.userData == null)
                {
                    costumeListView.userData = "bound";
                    _groupToListView[group] = costumeListView;

                    costumeListView.bindItem = (element, index) =>
                    {
                        var item = group.costumeListItems[index];
                        if (item == null)
                        {
                            item = new AmariCostumeListItem();
                            group.costumeListItems[index] = item;
                        }

                        var prefabField = element.Q<ObjectField>("CostumePrefabField");
                        if (prefabField == null)
                        {
                            Debug.LogError("PrefabField not found in costume item UXML");
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
                            OnCostumePrefabValueChanged(prefabField, item, newPrefab);
                        });
                    };

                    costumeListView.itemsAdded += indices =>
                    {
                        foreach (var i in indices)
                        {
                            EnsureListSize(group.costumeListItems, i);
                            group.costumeListItems[i] ??= new AmariCostumeListItem();
                        }

                        _costumeListSnapshots[costumeListView] = group.costumeListItems.ToList();
                    };

                    costumeListView.itemsRemoved += indices =>
                    {
                        if (!_costumeListSnapshots.TryGetValue(costumeListView, out var snapshot))
                        {
                            snapshot = group.costumeListItems.ToList();
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
                            if (_avatarSettings.activePreviewCostume == item)
                            {
                                OnActivePreviewCostumeDestroy(item);
                            }
                        }

                        _costumeListSnapshots[costumeListView] = group.costumeListItems.ToList();
                    };

                    RegisterGroupDragTargets(costumeListView, costumeListView);
                }

                // 各ListItemに実体インスタンスが存在するかチェックして、存在しないものはリストから消す
                group.costumeListItems.RemoveAll(item => item?.instance == null);
                _costumeListSnapshots[costumeListView] = group.costumeListItems.ToList();
                costumeListView.Rebuild();

                RegisterGroupDragTargets(groupElement, costumeListView);
            };

            _costumeGroupListView.itemsAdded += indices =>
            {
                foreach (var i in indices)
                {
                    EnsureListSize(_avatarSettings.CostumeListGroupItems, i);
                    _avatarSettings.CostumeListGroupItems[i] ??= new AmariCostumeGroupListItem
                    {
                        groupName = GetUnusedGroupName(),
                        costumeListItems = new List<AmariCostumeListItem>()
                    };
                }
            };

            _costumeGroupListView.itemsRemoved += indices =>
            {
                var snapshot = _avatarSettings.CostumeListGroupItems.ToList();
                foreach (var i in indices)
                {
                    if (i < 0 || i >= snapshot.Count)
                    {
                        continue;
                    }

                    var group = snapshot[i];
                    if (group?.costumeListItems == null)
                    {
                        continue;
                    }

                    foreach (var item in group.costumeListItems)
                    {
                        if (item?.instance)
                        {
                            DestroyImmediate(item.instance);
                        }

                        if (_avatarSettings.activePreviewCostume == item)
                        {
                            OnActivePreviewCostumeDestroy(item);
                        }
                    }

                    if (!_groupToListView.TryGetValue(group, out var listView))
                    {
                        continue;
                    }

                    _costumeListSnapshots.Remove(listView);
                    _groupToListView.Remove(group);
                    _listViewToTargetList.Remove(listView);
                }
            };

            _costumeGroupListView.Rebuild();
        }

        private static void EnsureListSize<T>(List<T> list, int index)
        {
            if (list == null || index < 0)
            {
                return;
            }

            while (list.Count <= index)
            {
                list.Add(default);
            }
        }
        #endregion


        #region AvatarSettings component controls
        private static AmariAvatarSettings FindAvatarSettings(VRCAvatarDescriptor avatarDescriptor)
        {
            var transform = avatarDescriptor.transform;

            for (var i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                var prefabAsset = PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject);
                if (prefabAsset == null)
                {
                    continue;
                }

                var prefabPath = AssetDatabase.GetAssetPath(prefabAsset);
                var prefabGuid = AssetDatabase.AssetPathToGUID(prefabPath);
                if (!string.Equals(prefabGuid, AmariSettingsPrefabGuid, System.StringComparison.Ordinal))
                {
                    continue;
                }

                return child.GetComponent<AmariAvatarSettings>();
            }

            return null;
        }

        private void LoadOrCreateAmariSettings(VRCAvatarDescriptor avatarDescriptor)
        {
            if (avatarDescriptor == null)
            {
                // TODO 設定を外す
                return;
            }

            // 読み込み試行
            var avatarSettings = FindAvatarSettings(avatarDescriptor);
            if (avatarSettings)
            {
                // 読み込み
                _avatarSettings = avatarSettings;

                return;
            }

            // 新規生成
            var prefabPath = AssetDatabase.GUIDToAssetPath(AmariSettingsPrefabGuid);
            if (string.IsNullOrWhiteSpace(prefabPath))
            {
                Debug.LogError("Settings prefab not found: " + AmariSettingsPrefabGuid);
                return;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogError("Settings prefab load failed: " + prefabPath);
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, avatarDescriptor.transform);
            instance.name = prefab.name;

            _avatarSettings = instance.GetComponent<AmariAvatarSettings>();
        }
        #endregion


        public void CreateGUI()
        {
            if (_avatarDescriptor != _pendingAvatarDescriptor)
            {
                _avatarDescriptor = _pendingAvatarDescriptor;
                LoadOrCreateAmariSettings(_avatarDescriptor);
            }

            if (_avatarSettings != null)
            {
                EnsureActivePreviewCostume();
            }

            var root = rootVisualElement;

            VisualElement labelFromUxml = visualTreeAsset.Instantiate();
            root.Add(labelFromUxml);

            // Localization ----------
            SetupLocalizationText(root);

            var langDd = root.Q<DropdownField>("EditorLanguage");
            langDd.choices = AmariLocalization.LanguageCodes;
            langDd.SetValueWithoutNotify(AmariLocalization.CurrentLanguageCode);
            langDd.RegisterValueChangedCallback(e =>
            {
                AmariLocalization.LoadLanguage(e.newValue);
                SetupLocalizationText(root);
            });

            // AvatarDetails ----------
            var avatarThumbnail = root.Q<VisualElement>("AvatarThumbnail");
            var avatarName = root.Q<Label>("AvatarName");
            // TODO アバターが切り替わった場合ここや各種設定のリロードが必要
            // (編集対象のオブジェクトのアクティブが切れたら設定をクリアしてウィンドウごと閉じる、とかが良さそう)
            TrySetAvatarDetails(avatarThumbnail, avatarName);

            // CostumeList ----------
            BindCostumeList(root);
        }

        private void EnsureActivePreviewCostume()
        {
            if (_avatarSettings.CostumeListGroupItems == null)
            {
                _avatarSettings.activePreviewCostume = null;
                return;
            }

            var active = _avatarSettings.activePreviewCostume;
            if (active == null || !EnumerateAllCostumeItems().Contains(active) || active.instance == null)
            {
                active = EnumerateAllCostumeItems().FirstOrDefault(item => item.instance != null);
                _avatarSettings.activePreviewCostume = active;
            }

            UpdatePreviewInstanceActiveStates();
        }
    }
}
