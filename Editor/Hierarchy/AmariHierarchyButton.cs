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

        private static readonly GUIContent ButtonContent = new("AMARI", "Avatar Modular Assistant");

        // Hierarchy は行ごと・フレームごとに描画されるため、スタイルは生成せずキャッシュを使い回す
        private static GUIStyle _buttonStyle;

        static AmariHierarchyButton()
        {
            // 静的コンストラクタから例外が抜けると型が TypeInitializationException で壊れたままになるため、
            // 失敗してもログだけ残してボタン無しで続行する
            try
            {
                EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyGUI;

                // 共有レジストリへ登録し、Materilune 等の参加ツールが AMARI の幅を避けられるようにする
                AmariHierarchyButtonRegistry.RegisterSelf(ButtonWidth);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
        }

        private static void OnHierarchyGUI(int instanceId, Rect selectionRect)
        {
            var go = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
            if (go == null) return;

            var avatar = go.GetComponent<VRCAvatarDescriptor>();
            if (avatar == null) return;

            // FaceEmo 予約幅 + 追加余白 + 自分より右に並ぶ参加ツールの幅（AMARI は最右のため現状ゼロ）
            var offsetX = AmariHierarchyButtonRegistry.ComputeOffset(true);

            // 高さ 15px（奇数）は行高 16px との中央寄せで y が半ピクセル位置になり、
            // ディスプレイスケールの丸めで上下 1px ぶれる。座標を整数へ丸めて確定させる
            // （旧実装の offsetY = 2f 補正はこの丸めと見た目差の目視合わせだったため撤去）
            var r = new Rect(
                Mathf.Round(selectionRect.xMax - offsetX - ButtonWidth),
                Mathf.Round(selectionRect.y + (selectionRect.height - ButtonHeight) * 0.5f),
                ButtonWidth,
                ButtonHeight
            );

            // Button style
            var style = GetButtonStyle();

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

        private static GUIStyle GetButtonStyle()
        {
            return _buttonStyle ??= new GUIStyle(GUI.skin.button)
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
        }
    }
}
