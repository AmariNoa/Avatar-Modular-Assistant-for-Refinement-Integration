using System.Collections.Generic;
using com.amari_noa.avatar_modular_assistant.editor.integrations;
using com.amari_noa.avatar_modular_assistant.editor.integrations.modular_avatar;
using UnityEngine.UIElements;

// ReSharper disable once CheckNamespace
namespace com.amari_noa.avatar_modular_assistant.editor
{
    public partial class AmariAvatarCustomizeWindow
    {
        private void BuildSubPanel(VisualElement root)
        {
            var toolTypeDd = root.Q<DropdownField>("OutfitToolType");

            var activeTools = new List<string> { nameof(AmariOutfitToolType.None) };
            if (AmariModularAvatarIntegration.IsInstalled())
            {
                // MAインストール済み
                activeTools.Add(nameof(AmariOutfitToolType.ModularAvatar));
            }

            toolTypeDd.choices = activeTools;
            toolTypeDd.SetValueWithoutNotify(_avatarSettings.outfitToolType.ToString());
            toolTypeDd.RegisterValueChangedCallback(e =>
            {
                if (_avatarSettings == null || string.IsNullOrWhiteSpace(e.newValue))
                {
                    return;
                }

                if (!System.Enum.TryParse<AmariOutfitToolType>(e.newValue, out var newToolType))
                {
                    toolTypeDd.SetValueWithoutNotify(_avatarSettings.outfitToolType.ToString());
                    return;
                }

                if (_avatarSettings.outfitToolType == newToolType)
                {
                    return;
                }

                RecordSettingsUndo("Change Outfit Tool Type");
                _avatarSettings.outfitToolType = newToolType;
                MarkSettingsDirty();

                // TODO 実装 ツール切り替え
                // 各種チェックを回し直してUIに反映
            });
        }
    }
}
