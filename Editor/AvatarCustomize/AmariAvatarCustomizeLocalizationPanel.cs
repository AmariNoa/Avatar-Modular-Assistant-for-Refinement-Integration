using UnityEngine.UIElements;

// ReSharper disable once CheckNamespace
namespace com.amari_noa.avatar_modular_assistant.editor
{
    public partial class AmariAvatarCustomizeWindow
    {
        private void BuildLocalizationPanel(VisualElement root)
        {
            var langDd = root.Q<DropdownField>("EditorLanguage");
            langDd.choices = AmariLocalization.LanguageCodes;
            langDd.SetValueWithoutNotify(AmariLocalization.CurrentLanguageCode);
            langDd.RegisterValueChangedCallback(e =>
            {
                AmariLocalization.LoadLanguage(e.newValue);
                SetupLocalizationTextOutfit(root);  // Outfit panel
            });
        }
    }
}
