using System;
using System.Collections.Generic;
using com.amari_noa.avatar_modular_assistant.editor.integrations;
using UnityEngine;
using UnityEngine.Serialization;

// ReSharper disable once CheckNamespace
namespace com.amari_noa.avatar_modular_assistant.runtime
{
    [Serializable]
    public class AmariItemGroupListItem
    {
        public string groupName;
        public List<AmariItemListItem> itemListItems;
        public float scaleMultiply = 1f;
        public bool includeInBuild = false;

        // グループ単位のプレビュー設定
        public bool previewEnabled = true;
        public bool previewStateInitialized = true;
        public AmariItemListItem activePreviewItem;
    }

    [Serializable]
    public class AmariItemListItem
    {
        public GameObject prefab;   // 原本プレハブ
        public string prefabGuid;   // プレハブGuid
        public GameObject instance; // 本ツールが生成したシーン上インスタンスの参照

        // TODO この設定が有効かどうかを返す(prefab != null)

        // TODO GUIDのみ残っていてprefabの割り当てが外れている場合に再検索する機能が欲しい
    }

    public class AmariAvatarSettings : MonoBehaviour
    {
        // 登録アイテム一覧
        [SerializeField, ReadOnly] private List<AmariItemGroupListItem> itemListGroupItems;
        public List<AmariItemGroupListItem> ItemListGroupItems => itemListGroupItems ??= new List<AmariItemGroupListItem>();

        // アクティブなアイテムの記録
        [ReadOnly] public AmariItemListItem activePreviewItem;

        // 使うOutfitツールの記録
        [ReadOnly] public AmariOutfitToolType outfitToolType = AmariOutfitToolType.None;
    }
}
