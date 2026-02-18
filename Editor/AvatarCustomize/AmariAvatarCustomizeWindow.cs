using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System.Threading.Tasks;
using UnityEngine.UIElements;
using VRC.Core;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.Api;

// ReSharper disable once CheckNamespace
namespace com.amari_noa.avatar_modular_assistant.editor
{
    public class AmariCostumeListItem
    {
        public string Test = "test";
    }

    public class AmariAvatarCustomizeWindow : EditorWindow
    {
        [SerializeField] private VisualTreeAsset visualTreeAsset = default;
        [Space]
        [SerializeField] private VisualTreeAsset costumeItemAsset = default;

        private VRCAvatarDescriptor _avatarDescriptor;
        private List<AmariCostumeListItem> _costumeListItems = new();

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
            var costumeListView = root.Q<ListView>("CostumeListView");

            costumeListView.itemsSource = _costumeListItems;
            costumeListView.makeItem = () => costumeItemAsset.Instantiate();

            costumeListView.bindItem = (element, index) =>
            {
                var item = _costumeListItems[index];
                if (item == null)
                {
                    item = new AmariCostumeListItem();
                    _costumeListItems[index] = item;
                }

                // 行UXML内の要素を name で参照して反映
                var nameLabel = element.Q<Label>("TestLabel");
                if (nameLabel != null) nameLabel.text = item.Test;
            };

            costumeListView.itemsAdded += indices =>
            {
                // ListItem自体がnullになることは許容しない
                foreach (var i in indices)
                {
                    _costumeListItems[i] ??= new AmariCostumeListItem();
                }
            };

            costumeListView.Rebuild();
        }

        public void CreateGUI()
        {
            if (_avatarDescriptor != _pendingAvatarDescriptor)
            {
                _avatarDescriptor = _pendingAvatarDescriptor;
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
    }
}
