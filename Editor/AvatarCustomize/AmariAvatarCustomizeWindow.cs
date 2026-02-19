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
        [SerializeField] private VisualTreeAsset costumeItemAsset;

        private VRCAvatarDescriptor _avatarDescriptor;
        //private readonly List<AmariCostumeListItem> _costumeListItems = new();
        private AmariAvatarSettings _avatarSettings;
        private ListView _costumeListView;
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
        private bool IsDuplicatePrefab(string guid)
        {
            return string.IsNullOrWhiteSpace(guid) || _avatarSettings.CostumeListItems.Where(item => item != null).Any(item => string.Equals(item.prefabGuid, guid, System.StringComparison.Ordinal));
        }

        private bool IsDuplicatePrefab(string guid, AmariCostumeListItem self)
        {
            return string.IsNullOrWhiteSpace(guid) || _avatarSettings.CostumeListItems.Where(item => item != null && item != self).Any(item => string.Equals(item.prefabGuid, guid, System.StringComparison.Ordinal));
        }

        private static bool IsPrefabAsset(Object obj)
        {
            if (obj is not GameObject go)
            {
                return false;
            }

            return EditorUtility.IsPersistent(go) && PrefabUtility.IsPartOfPrefabAsset(go);
        }

        private void RegisterPrefabDropTargets(VisualElement root)
        {
            if (_costumeListView == null)
            {
                return;
            }

            var costumeListPanel = root.Q<VisualElement>("CostumeListPanel");
            RegisterPrefabDropTarget(costumeListPanel);

            var costumeTitleLabel = root.Q<Label>("CostumePanelTitle");
            RegisterPrefabDropTarget(costumeTitleLabel);

            RegisterPrefabDropTarget(_costumeListView);
        }

        private void RegisterPrefabDropTarget(VisualElement target)
        {
            target.RegisterCallback<DragUpdatedEvent>(_ =>
            {
                DragAndDrop.visualMode = CanAcceptPrefabDrag() ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
            });

            target.RegisterCallback<DragPerformEvent>(_ =>
            {
                if (!CanAcceptPrefabDrag())
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
                    return;
                }

                DragAndDrop.AcceptDrag();
                AddPrefabsFromDrag(DragAndDrop.objectReferences);
            });
        }

        private static bool CanAcceptPrefabDrag()
        {
            var refs = DragAndDrop.objectReferences;
            if (refs == null || refs.Length == 0)
            {
                return false;
            }

            return refs.Any(IsPrefabAsset);
        }

        private static void SetCostumeListItemValues(AmariCostumeListItem item, GameObject prefab, string guid, GameObject instance)
        {
            item.prefab = prefab;
            item.prefabGuid = guid;
            item.instance = instance;
        }


        private void OnActivePreviewCostumeDestroy(AmariCostumeListItem item)
        {
            if (_avatarSettings.CostumeListItems.Count == 0 || (_avatarSettings.CostumeListItems.Count == 1 && _avatarSettings.CostumeListItems[0] == item))
            {
                // 要素数が0、または削除対象しか無いならアクティブに出来るものがない
                _avatarSettings.activePreviewCostume = null;
                UpdatePreviewInstanceActiveStates();
                return;
            }

            // 削除対象の要素を除いたリストの一番上をアクティブとして扱う
            var next = _avatarSettings.CostumeListItems.FirstOrDefault(ti => ti != item && ti != null && ti.instance);
            _avatarSettings.activePreviewCostume = next;
            UpdatePreviewInstanceActiveStates();
        }

        private bool CheckOrActivatePreviewCostume(AmariCostumeListItem item)
        {
            if (_avatarSettings.activePreviewCostume != null)
            {
                // アクティブが存在してもリスト外・実体無しなら無効化
                var active = _avatarSettings.activePreviewCostume;
                if (_avatarSettings.CostumeListItems.Contains(active) && active.instance)
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
            foreach (var item in _avatarSettings.CostumeListItems.Where(item => item?.instance != null))
            {
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

        private void OnCostumePrefabAdded(GameObject prefab, string guid)
        {
            var item = new AmariCostumeListItem();

            var instance = UpdatePrefabInstanceInScene(item, prefab);
            SetCostumeListItemValues(item, prefab, guid, instance);

            instance.SetActive(CheckOrActivatePreviewCostume(item));

            _avatarSettings.CostumeListItems.Add(item);
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

        private bool AddCostumePrefab(GameObject obj)
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

            OnCostumePrefabAdded(obj, guid);

            return true;
        }

        private void AddPrefabsFromDrag(Object[] refs)
        {
            if (refs == null || refs.Length == 0)
            {
                return;
            }

            var added = false;
            foreach (var obj in refs)
            {
                if (!AddCostumePrefab((GameObject)obj))
                {
                    continue;
                }
                added = true;
            }

            if (added)
            {
                _costumeListView.RefreshItems();
            }
        }

        private void BindCostumeList(VisualElement root)
        {
            _costumeListView = root.Q<ListView>("CostumeListView");

            _costumeListView.itemsSource = _avatarSettings.CostumeListItems;
            _costumeListView.makeItem = () => costumeItemAsset.Instantiate();

            _costumeListView.bindItem = (element, index) =>
            {
                var item = _avatarSettings.CostumeListItems[index];
                if (item == null)
                {
                    item = new AmariCostumeListItem();
                    _avatarSettings.CostumeListItems[index] = item;
                }

                // 行UXML内の要素を name で参照して反映
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

                // TODO これ何？
                prefabField.userData = "bound";

                prefabField.RegisterValueChangedCallback(e =>
                {
                    var newPrefab = e.newValue as GameObject;
                    OnCostumePrefabValueChanged(prefabField, item, newPrefab);
                });
            };

            // 各ListItemに実体インスタンスが存在するかチェックして、存在しないものはリストから消す
            _avatarSettings.CostumeListItems.RemoveAll(item => item?.instance == null);

            _costumeListView.itemsAdded += indices =>
            {
                // ListItem自体がnullになることは許容しない
                foreach (var i in indices)
                {
                    _avatarSettings.CostumeListItems[i] ??= new AmariCostumeListItem();
                }
            };

            _costumeListView.itemsRemoved += indices =>
            {
                // リストから要素が消えた時はインスタンスも破棄する
                foreach (var i in indices)
                {
                    if (i < 0 || i >= _avatarSettings.CostumeListItems.Count)
                    {
                        continue;
                    }

                    var item = _avatarSettings.CostumeListItems[i];
                    if (item == null || !item.instance)
                    {
                        continue;
                    }

                    DestroyImmediate(item.instance);
                    OnActivePreviewCostumeDestroy(item);
                }
            };

            _costumeListView.Rebuild();
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
            RegisterPrefabDropTargets(root);
        }

        private void EnsureActivePreviewCostume()
        {
            if (_avatarSettings.CostumeListItems == null)
            {
                _avatarSettings.activePreviewCostume = null;
                return;
            }

            var active = _avatarSettings.activePreviewCostume;
            if (active == null || !_avatarSettings.CostumeListItems.Contains(active) || active.instance == null)
            {
                active = _avatarSettings.CostumeListItems.FirstOrDefault(item => item?.instance != null);
                _avatarSettings.activePreviewCostume = active;
            }

            UpdatePreviewInstanceActiveStates();
        }
    }
}
