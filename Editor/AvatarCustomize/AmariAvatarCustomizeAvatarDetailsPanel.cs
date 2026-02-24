using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.Core;
using VRC.SDKBase.Editor.Api;

// ReSharper disable once CheckNamespace
namespace com.amari_noa.avatar_modular_assistant.editor
{
    public partial class AmariAvatarCustomizeWindow
    {
        private const string AvatarThumbNoImageGuid = "157fefdf931d59b4c9100ce94c5fb488";

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

        private void BuildAvatarDetailsPanel(VisualElement root)
        {
            var avatarThumbnail = root.Q<VisualElement>("AvatarThumbnail");
            var avatarName = root.Q<Label>("AvatarName");
            var blueprintIdLabel = root.Q<Label>("BlueprintId");
            string blueprintId = null;

            if (_avatarDescriptor != null &&
                _avatarDescriptor.TryGetComponent<PipelineManager>(out var pipelineManager))
            {
                blueprintId = pipelineManager.blueprintId;
            }

            if (!string.IsNullOrWhiteSpace(blueprintId))
            {
                if (blueprintIdLabel != null)
                {
                    blueprintIdLabel.text = blueprintId;
                }

                TrySetAvatarDetails(avatarThumbnail, avatarName);
                return;
            }

            if (blueprintIdLabel != null)
            {
                blueprintIdLabel.text = string.Empty;
            }

            if (avatarThumbnail != null)
            {
                var texturePath = AssetDatabase.GUIDToAssetPath(AvatarThumbNoImageGuid);
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                avatarThumbnail.style.backgroundImage = texture != null
                    ? new StyleBackground(texture)
                    : default;
            }

            avatarName?.Clear();
            // TODO アバターが切り替わった場合ここや各種設定のリロードが必要
            // (編集対象のオブジェクトのアクティブが切れたら設定をクリアしてウィンドウごと閉じる、とかが良さそう)
        }
    }
}
