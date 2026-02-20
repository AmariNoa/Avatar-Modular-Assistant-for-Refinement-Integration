using System.Threading.Tasks;
using VRC.Core;
using VRC.SDKBase.Editor.Api;
using UnityEngine.UIElements;

// ReSharper disable once CheckNamespace
namespace com.amari_noa.avatar_modular_assistant.editor
{
    public partial class AmariAvatarCustomizeWindow
    {
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
            // TODO アバターが切り替わった場合ここや各種設定のリロードが必要
            // (編集対象のオブジェクトのアクティブが切れたら設定をクリアしてウィンドウごと閉じる、とかが良さそう)
            TrySetAvatarDetails(avatarThumbnail, avatarName);
        }
    }
}
