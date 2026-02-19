using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

// ReSharper disable once CheckNamespace
namespace com.amari_noa.avatar_modular_assistant.runtime
{
    [Serializable]
    public class AmariCostumeListItem
    {
        public GameObject prefab;   // 原本プレハブ
        public string prefabGuid;   // プレハブGuid
        public GameObject instance; // 本ツールが生成したシーン上インスタンスの参照

        // TODO この設定が有効かどうかを返す(prefab != null)

        // TODO GUIDのみ残っていてprefabの割り当てが外れている場合に再検索する機能が欲しい
    }

    public class AmariAvatarSettings : MonoBehaviour
    {
        [SerializeField, ReadOnly] private List<AmariCostumeListItem> costumeListItems;
        public List<AmariCostumeListItem> CostumeListItems => costumeListItems;
    }
}
