using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

// ReSharper disable once CheckNamespace
namespace com.amari_noa.avatar_modular_assistant.editor
{
    [InitializeOnLoad]
    public class AmariHierarchyButton
    {
        private const float ButtonWidth = 52f;
        private const float ButtonHeight = 15f;
#if AMARI_FACEEMO_INSTALLED
        // FaceEmo のボタン幅
        private const float FaceEmoButtonOffset = 50f;
#else
        private const float PaddingX = 2f;
#endif

        private static readonly GUIContent ButtonContent = new("AMARI", "Avatar Modular Assistant");

        static AmariHierarchyButton()
        {
            EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyGUI;
        }

        private static void OnHierarchyGUI(int instanceId, Rect selectionRect)
        {
            var go = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
            if (go == null) return;

            var avatar = go.GetComponent<VRCAvatarDescriptor>();
            if (avatar == null) return;

            var offsetX = 0f;

#if AMARI_FACEEMO_INSTALLED
            // FaceEmoがインストールされている場合はボタンの左右表示位置をずらす
            const float gap = 2f;
            offsetX += FaceEmoButtonOffset + gap;
#else
            offsetX += PaddingX;
#endif

            // TODO FaceEmoボタンとの相対位置で謎の上下位置ズレが出たので補正値を噛ませておく 根本原因が特定出来たらそっちで直したい
            const float offsetY = 2f;
            var r = new Rect(
                selectionRect.xMax - ButtonWidth - offsetX,
                selectionRect.y + (selectionRect.height - ButtonHeight) * 0.5f - offsetY,
                ButtonWidth,
                ButtonHeight
            );

            // Button style
            var style = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 12,
                normal =
                {
                    textColor = Color.white,
                    background = Texture2D.whiteTexture
                },
            };

            // Change background color
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = Color.black;

            // Draw button
            if (GUI.Button(r, ButtonContent, style))
            {
                AmariAvatarCustomizeWindow.OpenWithAvatarDescriptor(avatar);
            }

            // Restore background color
            GUI.backgroundColor = prevBg;
        }
    }
}
