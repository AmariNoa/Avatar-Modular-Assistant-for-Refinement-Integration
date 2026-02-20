using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

// ReSharper disable once CheckNamespace
namespace com.amari_noa.avatar_modular_assistant.runtime
{
    [Serializable]
    public class AmariOutfitGroupListItem
    {
        public string groupName;
        public List<AmariOutfitListItem> outfitListItems;
    }

    [Serializable]
    public class AmariOutfitListItem
    {
        public GameObject prefab;   // 原本プレハブ
        public string prefabGuid;   // プレハブGuid
        public GameObject instance; // 本ツールが生成したシーン上インスタンスの参照

        // TODO この設定が有効かどうかを返す(prefab != null)

        // TODO GUIDのみ残っていてprefabの割り当てが外れている場合に再検索する機能が欲しい
    }

    public class AmariAvatarSettings : MonoBehaviour
    {
        // 登録衣装一覧
        [SerializeField, ReadOnly] private List<AmariOutfitGroupListItem> outfitListGroupItems;
        public List<AmariOutfitGroupListItem> OutfitListGroupItems => outfitListGroupItems;

        // アクティブな衣装の記録
        [ReadOnly] public AmariOutfitListItem activePreviewOutfit;
    }
}
