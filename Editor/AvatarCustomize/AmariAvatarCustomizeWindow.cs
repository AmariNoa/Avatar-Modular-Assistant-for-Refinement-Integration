using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using com.amari_noa.avatar_modular_assistant.runtime;
using com.amari_noa.avatar_modular_assistant.editor.integrations.modular_avatar;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
using VRC.SDK3.Avatars.Components;

// ReSharper disable once CheckNamespace
namespace com.amari_noa.avatar_modular_assistant.editor
{
    public partial class AmariAvatarCustomizeWindow : EditorWindow
    {
        [SerializeField] private VisualTreeAsset visualTreeAsset;
        [Space]
        [SerializeField] private VisualTreeAsset itemGroupTabItemAsset;

        // TODO 後で消す
        [SerializeField] private VisualTreeAsset itemGroupItemAsset;

        [SerializeField] private VisualTreeAsset itemItemAsset;

        private VRCAvatarDescriptor _avatarDescriptor;
        private AmariAvatarSettings _avatarSettings;

        // TODO 後で消す
        private ListView _itemGroupListView;

        private readonly Dictionary<ListView, List<AmariItemListItem>> _itemListSnapshots = new();
        private readonly Dictionary<AmariItemGroupListItem, ListView> _groupToListView = new();
        private readonly Dictionary<ListView, List<AmariItemListItem>> _listViewToTargetList = new();
        private readonly Dictionary<AmariItemListItem, AmariModularAvatarCheckResult> _itemCheckResults = new();
        private readonly HashSet<VisualElement> _dragTargets = new();
        private static VRCAvatarDescriptor _pendingAvatarDescriptor;

        private const string WindowTitle = "[AMARI] Avatar Customize";
        private const string AmariSettingsPrefabGuid = "2fe354710d7e2d9439856f459edadc0d";

        private void OnEnable()
        {
            EnsureItemIconsLoaded();
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
        }

        private void OnUndoRedoPerformed()
        {
            if (_avatarSettings == null)
            {
                return;
            }

            EnsureActivePreviewItem();

            if (_itemGroupListView != null)
            {
                _itemGroupListView.itemsSource = _avatarSettings.ItemListGroupItems;
                _itemGroupListView.Rebuild();
            }

            SetupLocalizationTextItem(rootVisualElement);
        }

        private void RecordSettingsUndo(string actionName)
        {
            if (_avatarSettings == null)
            {
                return;
            }

            Undo.RecordObject(_avatarSettings, actionName);
        }

        private void MarkSettingsDirty()
        {
            if (_avatarSettings == null)
            {
                return;
            }

            EditorUtility.SetDirty(_avatarSettings);
            MarkSceneDirty(_avatarSettings.gameObject);
        }

        private static void MarkSceneDirty(GameObject target)
        {
            if (target == null)
            {
                return;
            }

            var scene = target.scene;
            if (scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(scene);
            }
        }

        private static void MarkObjectDirty(Object target)
        {
            if (target == null)
            {
                return;
            }

            EditorUtility.SetDirty(target);
            switch (target)
            {
                case Component component:
                    MarkSceneDirty(component.gameObject);
                    break;
                case GameObject go:
                    MarkSceneDirty(go);
                    break;
            }
        }


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


        private static bool IsPrefabAsset(Object obj)
        {
            if (obj is not GameObject go)
            {
                return false;
            }

            return EditorUtility.IsPersistent(go) && PrefabUtility.IsPartOfPrefabAsset(go);
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
            Undo.RegisterCreatedObjectUndo(instance, "Create Avatar Settings");

            // 生成直後にアイテムグループが未設定なら空要素を1つ追加
            var itemGroups = _avatarSettings.ItemListGroupItems;
            if (itemGroups != null && itemGroups.Count == 0)
            {
                Undo.RecordObject(_avatarSettings, "Initialize Item Group");
                itemGroups.Add(new AmariItemGroupListItem
                {
                    groupName = string.Empty,
                    itemListItems = new List<AmariItemListItem>()
                });
            }

            MarkSettingsDirty();
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
                EnsureActivePreviewItem();
            }

            var root = rootVisualElement;

            VisualElement labelFromUxml = visualTreeAsset.Instantiate();
            root.Add(labelFromUxml);

            // Localization ----------
            BuildLocalizationPanel(root);

            // AvatarDetails ----------
            BuildAvatarDetailsPanel(root);

            // SubPanel ----------
            BuildSubPanel(root);

            // ItemList ----------
            BuildItemGroupTabPanel(root);
        }
    }
}
