using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using System.Threading.Tasks;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
using VRC.Core;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.Api;

// ReSharper disable once CheckNamespace
namespace com.amari_noa.avatar_modular_assistant.editor
{
    public class AmariCostumeListItem
    {
        public GameObject Prefab;
        public string PrefabGuid;
    }

    public class AmariAvatarCustomizeWindow : EditorWindow
    {
        [SerializeField] private VisualTreeAsset visualTreeAsset;
        [Space]
        [SerializeField] private VisualTreeAsset costumeItemAsset;

        private VRCAvatarDescriptor _avatarDescriptor;
        private readonly List<AmariCostumeListItem> _costumeListItems = new();
        private ListView _costumeListView;

        private const string WindowTitle = "[AMARI] Avatar Customize";
        private static VRCAvatarDescriptor _pendingAvatarDescriptor;


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

        private void BindCostumeList(VisualElement root)
        {
            _costumeListView = root.Q<ListView>("CostumeListView");

            _costumeListView.itemsSource = _costumeListItems;
            _costumeListView.makeItem = () => costumeItemAsset.Instantiate();

            _costumeListView.bindItem = (element, index) =>
            {
                var item = _costumeListItems[index];
                if (item == null)
                {
                    item = new AmariCostumeListItem();
                    _costumeListItems[index] = item;
                }

                // 行UXML内の要素を name で参照して反映
                var prefabField = element.Q<ObjectField>("CostumePrefabField");
                if (prefabField == null)
                {
                    return;
                }

                prefabField.objectType = typeof(GameObject);
                prefabField.allowSceneObjects = false;
                prefabField.SetValueWithoutNotify(item.Prefab);
                if (prefabField.userData != null)
                {
                    return;
                }

                prefabField.userData = "bound";
                prefabField.RegisterValueChangedCallback(e =>
                {
                    var newPrefab = e.newValue as GameObject;
                    if (newPrefab == null)
                    {
                        item.Prefab = null;
                        return;
                    }

                    if (!IsPrefabAsset(newPrefab))
                    {
                        prefabField.SetValueWithoutNotify(item.Prefab);
                        return;
                    }

                    var newPath = AssetDatabase.GetAssetPath(newPrefab);
                    var newGuid = AssetDatabase.AssetPathToGUID(newPath);
                    if (IsDuplicatePrefab(newGuid, item))
                    {
                        prefabField.SetValueWithoutNotify(item.Prefab);
                        return;
                    }

                    item.Prefab = newPrefab;
                    item.PrefabGuid = newGuid;
                });
            };

            _costumeListView.itemsAdded += indices =>
            {
                // ListItem自体がnullになることは許容しない
                foreach (var i in indices)
                {
                    _costumeListItems[i] ??= new AmariCostumeListItem();
                }
            };

            _costumeListView.Rebuild();
        }

        public void CreateGUI()
        {
            if (_avatarDescriptor != _pendingAvatarDescriptor)
            {
                _avatarDescriptor = _pendingAvatarDescriptor;
                // TODO 対象のAvatarにオブジェクトが無ければ生成、あれば読み込む
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
            // TODO アバターが切り替わった場合ここのリロードが必要
            TrySetAvatarDetails(avatarThumbnail, avatarName);

            // CostumeList ----------
            BindCostumeList(root);
            RegisterPrefabDropTargets(root);
        }

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

        private void AddPrefabsFromDrag(Object[] refs)
        {
            if (refs == null || refs.Length == 0)
            {
                return;
            }

            var added = false;
            foreach (var obj in refs)
            {
                if (!IsPrefabAsset(obj))
                {
                    continue;
                }

                var prefab = (GameObject)obj;
                var path = AssetDatabase.GetAssetPath(prefab);
                var guid = AssetDatabase.AssetPathToGUID(path);
                if (IsDuplicatePrefab(guid))
                {
                    continue;
                }
                _costumeListItems.Add(new AmariCostumeListItem
                {
                    Prefab = prefab,
                    PrefabGuid = guid
                });
                added = true;
            }

            if (added)
            {
                _costumeListView.RefreshItems();
            }
        }

        private bool IsDuplicatePrefab(string guid)
        {
            return string.IsNullOrWhiteSpace(guid) || _costumeListItems.Where(item => item != null).Any(item => string.Equals(item.PrefabGuid, guid, System.StringComparison.Ordinal));
        }

        private bool IsDuplicatePrefab(string guid, AmariCostumeListItem self)
        {
            if (string.IsNullOrWhiteSpace(guid))
            {
                return true;
            }

            foreach (var item in _costumeListItems)
            {
                if (item == null || item == self)
                {
                    continue;
                }

                if (string.Equals(item.PrefabGuid, guid, System.StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsPrefabAsset(Object obj)
        {
            if (obj is not GameObject go)
            {
                return false;
            }

            return EditorUtility.IsPersistent(go) && PrefabUtility.IsPartOfPrefabAsset(go);
        }
    }
}
