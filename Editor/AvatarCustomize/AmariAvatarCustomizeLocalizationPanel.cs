using System.Collections.Generic;
using com.amari_noa.unity_editor_localization_core.editor;
using UnityEngine.UIElements;

// ReSharper disable once CheckNamespace
namespace com.amari_noa.avatar_modular_assistant.editor
{
    public partial class AmariAvatarCustomizeWindow
    {
        private void BuildLocalizationPanel(VisualElement root)
        {
            var service = EditorLocalization.Service;
            var sourceId = AmariLocalizationSourceRegistration.SourceId;
            var langDd = root.Q<DropdownField>("EditorLanguage");
            if (langDd == null)
            {
                return;
            }

            var choices = new List<string>(service.GetAvailableLanguages(sourceId));
            langDd.choices = choices;

            var currentLanguageCode = service.CurrentLanguageCode;
            if (choices.Contains(currentLanguageCode))
            {
                langDd.SetValueWithoutNotify(currentLanguageCode);
            }
            else if (choices.Count > 0)
            {
                langDd.SetValueWithoutNotify(choices[0]);
            }
            else
            {
                langDd.SetValueWithoutNotify(currentLanguageCode);
            }

            langDd.RegisterValueChangedCallback(e =>
            {
                if (!langDd.choices.Contains(e.newValue))
                {
                    langDd.SetValueWithoutNotify(service.CurrentLanguageCode);
                    return;
                }

                var result = service.SetLanguage(sourceId, e.newValue);
                if (result is EditorLocalizationSetLanguageResult.FAILED or EditorLocalizationSetLanguageResult.NOT_REGISTERED)
                {
                    langDd.SetValueWithoutNotify(service.CurrentLanguageCode);
                    return;
                }

                langDd.SetValueWithoutNotify(service.CurrentLanguageCode);
                SetupLocalizationTextItem(root);  // Item panel
            });
        }
    }
}
