using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;

// ReSharper disable once CheckNamespace
namespace com.amari_noa.avatar_modular_assistant.editor
{
    public class AmariAvatarCustomizeWindow : EditorWindow
    {
        [SerializeField] private VisualTreeAsset m_VisualTreeAsset = default;
        [SerializeField] private VRCAvatarDescriptor target;

        private const string WindowTitle = "[AMARI] Avatar Customize";


        [MenuItem("Tools/Avatar Modular Assistant/Avatar Customize Window")]
        public static void Open()
        {
            OpenWithAvatarDescriptor(null);
        }

        public static void OpenWithAvatarDescriptor(VRCAvatarDescriptor target)
        {
            var w = GetWindow<AmariAvatarCustomizeWindow>(false, WindowTitle, true);
            w.target = target;
            w.Show();
        }


        public void CreateGUI()
        {
            // Each editor window contains a root VisualElement object
            var root = rootVisualElement;

            // VisualElements objects can contain other VisualElement following a tree hierarchy.
            VisualElement label = new Label("Hello World! From C#");
            root.Add(label);

            // Instantiate UXML
            VisualElement labelFromUxml = m_VisualTreeAsset.Instantiate();
            root.Add(labelFromUxml);
        }
    }
}
