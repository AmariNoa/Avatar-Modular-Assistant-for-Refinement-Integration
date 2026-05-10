using com.amari_noa.unity_editor_localization_core.editor;
using UnityEditor;

// ReSharper disable once CheckNamespace
namespace com.amari_noa.avatar_modular_assistant.editor
{
    [InitializeOnLoad]
    internal static class AmariLocalizationSourceRegistration
    {
        internal const string SourceId = "com.amari-noa.avatar-modular-assistant";
        private const string DisplayName = "Avatar Modular Assistant";
        private const string LocalizationFolderGuid = "0493772b8f41ac54a814625e5072574d";

        static AmariLocalizationSourceRegistration()
        {
            EditorLocalization.Service.RegisterSource(new EditorLocalizationSourceDefinition
            {
                SourceId = SourceId,
                DisplayName = DisplayName,
                LocalizationFolderGuid = LocalizationFolderGuid,
                DefaultLanguageCode = "en-US",
                BaseLanguageCode = "en-US"
            });
        }
    }
}
