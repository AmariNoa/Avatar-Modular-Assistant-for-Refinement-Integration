using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;

// ReSharper disable once CheckNamespace
namespace com.amari_noa.avatar_modular_assistant.editor
{
    /// <summary>
    /// Hierarchy ボタンを出す AmariNoa 製ツール間で共有するレジストリ（仕様 v1）の読み書き。
    /// SessionState を使うのは、ドメインリロードを跨いで保持されつつエディタ再起動で消えるため
    /// （EditorPrefs だとアンインストール済みツールの登録が残り、空白を予約し続ける）。
    /// </summary>
    internal static class AmariHierarchyButtonRegistry
    {
        internal const string ToolId = "amari";
        internal const int Priority = 100;
        internal const float Gap = 2f;

        private const string ToolsKey = "AmariNoa.HierarchyButtons.v1.Tools";
        private const string EntryKeyPrefix = "AmariNoa.HierarchyButtons.v1.Entry.";
        private const string ExtraOffsetKey = "AmariNoa.HierarchyButtons.ExtraOffset";
        private const int CurrentSchema = 1;
        private const string RowKindAvatarRoot = "avatar-root";

#if AMARI_FACEEMO_INSTALLED
        // FaceEmo はレジストリ非参加のため固定値で避ける。幅 30px は FaceEmo 実装のローカル定数（動的取得は不可）
        private const float FaceEmoWidth = 30f;
        private const string FaceEmoHideKey = "FaceEmo_HideHierarchyIcon";
        private const string FaceEmoOffsetKey = "FaceEmo_HierarchyIconOffset";
        // FaceEmo のオフセット既定値は 0 ではなく 20（jp.suzuryg.face-emo Editor/Detail/DetailConstants.cs:42 付近）。
        // 0 を既定にすると、設定を触っていない環境で 20px 足りずボタンが重なる
        private const float FaceEmoDefaultOffset = 20f;
#endif

        /// <summary>
        /// 自ツールをレジストリへ登録する（ドメインリロードごとに呼ぶ）。
        /// </summary>
        /// <param name="width">実際に描画するボタン幅（px。ギャップは含めない）</param>
        internal static void RegisterSelf(float width)
        {
            // Tools キーへ toolId を追記（重複は除去して書き戻す）
            var toolIds = ReadSessionString(ToolsKey)
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            var normalized = new List<string>(toolIds.Length + 1);
            var found = false;
            foreach (var toolId in toolIds)
            {
                if (string.Equals(toolId, ToolId, StringComparison.Ordinal))
                {
                    if (found) continue;
                    found = true;
                }

                normalized.Add(toolId);
            }

            if (!found)
            {
                normalized.Add(ToolId);
            }

            SessionState.SetString(ToolsKey, string.Join(";", normalized));

            // 登録内容は毎回上書き（幅の変更が次のリロードで反映される）
            SessionState.SetString(
                EntryKeyPrefix + ToolId,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}|{1}|{2}|{3}",
                    CurrentSchema,
                    width.ToString("R", CultureInfo.InvariantCulture),
                    Priority,
                    RowKindAvatarRoot));
        }

        /// <summary>
        /// 自ボタンの右端からのオフセットを計算する（描画のたびに呼ぶ。キャッシュしない）。
        /// </summary>
        /// <param name="isAvatarRoot">描画対象の行がアバタールートかどうか</param>
        internal static float ComputeOffset(bool isAvatarRoot)
        {
            // 非参加ツール（FaceEmo）の予約幅 + ユーザー調整用の追加余白
            var offset = ReadFaceEmoReservation(isAvatarRoot) + ReadExtraOffset();

            // 自分より priority が小さく（右端に近く）、かつこの行に描く参加ツールの幅を加算する
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var toolIds = ReadSessionString(ToolsKey)
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var toolId in toolIds)
            {
                if (!seen.Add(toolId) || string.Equals(toolId, ToolId, StringComparison.Ordinal))
                {
                    continue;
                }

                var entry = ReadEntry(toolId);
                if (!entry.IsValid ||
                    !DrawsOnRow(entry.RowKind, isAvatarRoot) ||
                    entry.Priority > Priority ||
                    (entry.Priority == Priority && string.CompareOrdinal(toolId, ToolId) >= 0))
                {
                    continue;
                }

                offset += entry.Width + Gap;
            }

            return offset;
        }

        /// <summary>
        /// rowKind を宣言したツールがこの行に描くかを判定する。
        /// 未宣言・未知の値は「描く」側に倒す（余分に空けても空白が残るだけだが、足りないと重なるため）。
        /// </summary>
        private static bool DrawsOnRow(string rowKind, bool isAvatarRoot)
        {
            if (string.Equals(rowKind, RowKindAvatarRoot, StringComparison.Ordinal))
            {
                return isAvatarRoot;
            }

            return true;
        }

        /// <summary>
        /// FaceEmo（レジストリ非参加）の予約幅を返す。未導入・非表示設定時は 0。
        /// </summary>
        private static float ReadFaceEmoReservation(bool isAvatarRoot)
        {
#if AMARI_FACEEMO_INSTALLED
            // FaceEmo は VRCAvatarDescriptor を持つ行にしか描かない（FaceEmoLauncher.cs:249-253）
            if (!isAvatarRoot)
            {
                return 0f;
            }

            try
            {
                if (EditorPrefs.GetBool(FaceEmoHideKey, false))
                {
                    return 0f;
                }

                return FaceEmoWidth + EditorPrefs.GetFloat(FaceEmoOffsetKey, FaceEmoDefaultOffset) + Gap;
            }
            catch (Exception)
            {
                return 0f;
            }
#else
            return 0f;
#endif
        }

        private static float ReadExtraOffset()
        {
            try
            {
                return EditorPrefs.GetFloat(ExtraOffsetKey, 0f);
            }
            catch (Exception)
            {
                return 0f;
            }
        }

        /// <summary>
        /// 登録内容（schema|width|priority|rowKind）を解析する。
        /// 解析に失敗したエントリは無いものとして扱う（他ツールの描画を巻き込まないため例外を投げない）。
        /// </summary>
        private static RegistryEntry ReadEntry(string toolId)
        {
            var parts = ReadSessionString(EntryKeyPrefix + toolId).Split('|');
            // rowKind は後から追加された 4 項目め。3 項目しか書かない旧実装との互換で省略を許容する
            var rowKind = parts.Length >= 4 ? parts[3] : string.Empty;
            if (parts.Length < 3 ||
                !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var schema) ||
                schema != CurrentSchema ||
                !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var width) ||
                !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var priority) ||
                width <= 0f ||
                float.IsNaN(width) ||
                float.IsInfinity(width))
            {
                return default;
            }

            return new RegistryEntry(width, priority, rowKind, true);
        }

        private static string ReadSessionString(string key)
        {
            try
            {
                return SessionState.GetString(key, string.Empty) ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private readonly struct RegistryEntry
        {
            internal RegistryEntry(float width, int priority, string rowKind, bool isValid)
            {
                Width = width;
                Priority = priority;
                RowKind = rowKind;
                IsValid = isValid;
            }

            internal readonly float Width;
            internal readonly int Priority;
            internal readonly string RowKind;
            internal readonly bool IsValid;
        }
    }
}
