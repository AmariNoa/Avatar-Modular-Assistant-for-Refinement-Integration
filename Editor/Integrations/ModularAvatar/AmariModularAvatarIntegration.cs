using System;
using System.Collections.Generic;
using com.amari_noa.avatar_modular_assistant.runtime;
using UnityEngine;
using nadena.dev.modular_avatar.core;

// ReSharper disable once CheckNamespace
namespace com.amari_noa.avatar_modular_assistant.editor.integrations.modular_avatar
{
    public enum AmariModularAvatarPrefabKind
    {
        SystemIgnore,
        Accessory,
        Outfit
    }

    public enum AmariModularAvatarComponentKind
    {
        None,
        BoneProxy,
        MergeArmature
    }

    public enum AmariModularAvatarSuggestedAction
    {
        None,
        AddBoneProxy,
        AddMergeArmature
    }

    public readonly struct AmariModularAvatarCheckResult
    {
        public readonly AmariModularAvatarPrefabKind Kind;
        public readonly AmariModularAvatarComponentKind ExistingMa;
        public readonly AmariModularAvatarSuggestedAction Suggestion;

        public readonly bool HasMesh;
        public readonly bool HasBonesInPrefab;
        public readonly bool LooksLikeArmatureWithHipsRoot;
        public readonly string Reason;

        public AmariModularAvatarCheckResult(
            AmariModularAvatarPrefabKind kind,
            AmariModularAvatarComponentKind existingMa,
            AmariModularAvatarSuggestedAction suggestion,
            bool hasMesh,
            bool hasBonesInPrefab,
            bool looksLikeArmatureWithHipsRoot,
            string reason)
        {
            Kind = kind;
            ExistingMa = existingMa;
            Suggestion = suggestion;
            HasMesh = hasMesh;
            HasBonesInPrefab = hasBonesInPrefab;
            LooksLikeArmatureWithHipsRoot = looksLikeArmatureWithHipsRoot;
            Reason = reason;
        }
    }

    public static class AmariModularAvatarIntegration
    {
        public static bool IsInstalled()
        {
#if AMARI_MA_INSTALLED
            return true;
#else
            return false;
#endif
        }

        /*
        public static int GetSeverityCount(AmariSeverity severity)
        {
            // TODO 実装
            throw new System.NotImplementedException();
        }
        */

        // TODO AvatarDescriptorではなく本ツールのSettingsを使う形で再実装したい
        /*
        public static Dictionary<GameObject, AmariModularAvatarCheckResult> CheckAvatar(VRCAvatarDescriptor avatarRoot)
        {
            var result = new Dictionary<GameObject, AmariModularAvatarCheckResult>();
            if (avatarRoot == null) return result;

            for (var i = 0; i < avatarRoot.transform.childCount; i++)
            {
                var child = avatarRoot.transform.GetChild(i);
                result.Add(child.gameObject, CheckPrefab(child.gameObject));
            }

            return result;
        }
        */

        public static Dictionary<GameObject, AmariModularAvatarCheckResult> CheckGroup(AmariOutfitGroupListItem group)
        {
            var result = new Dictionary<GameObject, AmariModularAvatarCheckResult>();
            if (group == null) return result;

            foreach (var item in group.outfitListItems)
            {
                var instance = item.instance;
                if (instance == null)
                {
                    // TODO 警告出すべきかも
                    continue;
                }

                result.Add(instance, CheckPrefab(instance));
            }

            return result;
        }

        public static AmariModularAvatarCheckResult CheckPrefab(GameObject prefabRoot)
        {
            if (prefabRoot == null) throw new ArgumentNullException(nameof(prefabRoot));

            var existingMa = FindMaOnRootOrFirstChildren(prefabRoot);

            if (existingMa == AmariModularAvatarComponentKind.BoneProxy)
            {
                return new AmariModularAvatarCheckResult(
                    kind: AmariModularAvatarPrefabKind.Accessory,
                    existingMa: existingMa,
                    suggestion: AmariModularAvatarSuggestedAction.None,
                    hasMesh: HasAnyMesh(prefabRoot),
                    hasBonesInPrefab: HasAnyBonesInsidePrefab(prefabRoot),
                    looksLikeArmatureWithHipsRoot: LooksLikeArmatureWithHipsRoot(prefabRoot),
                    reason: "MA Bone Proxy found on root/first child -> treat as Accessory.");
            }

            if (existingMa == AmariModularAvatarComponentKind.MergeArmature)
            {
                return new AmariModularAvatarCheckResult(
                    kind: AmariModularAvatarPrefabKind.Outfit,
                    existingMa: existingMa,
                    suggestion: AmariModularAvatarSuggestedAction.None,
                    hasMesh: HasAnyMesh(prefabRoot),
                    hasBonesInPrefab: HasAnyBonesInsidePrefab(prefabRoot),
                    looksLikeArmatureWithHipsRoot: LooksLikeArmatureWithHipsRoot(prefabRoot),
                    reason: "MA Merge Armature found on root/first child -> treat as Outfit.");
            }

            var hasMesh = HasAnyMesh(prefabRoot);
            if (!hasMesh)
            {
                return new AmariModularAvatarCheckResult(
                    kind: AmariModularAvatarPrefabKind.SystemIgnore,
                    existingMa: AmariModularAvatarComponentKind.None,
                    suggestion: AmariModularAvatarSuggestedAction.None,
                    hasMesh: false,
                    hasBonesInPrefab: false,
                    looksLikeArmatureWithHipsRoot: false,
                    reason: "No mesh renderers found -> treat as SystemIgnore.");
            }

            var hasBonesInPrefab = HasAnyBonesInsidePrefab(prefabRoot);
            if (!hasBonesInPrefab)
            {
                return new AmariModularAvatarCheckResult(
                    kind: AmariModularAvatarPrefabKind.Accessory,
                    existingMa: AmariModularAvatarComponentKind.None,
                    suggestion: AmariModularAvatarSuggestedAction.AddBoneProxy,
                    hasMesh: true,
                    hasBonesInPrefab: false,
                    looksLikeArmatureWithHipsRoot: false,
                    reason: "Mesh exists but no bones in prefab -> Accessory; suggest add MA Bone Proxy.");
            }

            var armatureWithHips = LooksLikeArmatureWithHipsRoot(prefabRoot);
            if (armatureWithHips)
            {
                return new AmariModularAvatarCheckResult(
                    kind: AmariModularAvatarPrefabKind.Outfit,
                    existingMa: AmariModularAvatarComponentKind.None,
                    suggestion: AmariModularAvatarSuggestedAction.AddMergeArmature,
                    hasMesh: true,
                    hasBonesInPrefab: true,
                    looksLikeArmatureWithHipsRoot: true,
                    reason: "Bones + Armature + root bone is Hips -> Outfit; suggest add MA Merge Armature.");
            }

            return new AmariModularAvatarCheckResult(
                kind: AmariModularAvatarPrefabKind.Accessory,
                existingMa: AmariModularAvatarComponentKind.None,
                suggestion: AmariModularAvatarSuggestedAction.AddBoneProxy,
                hasMesh: true,
                hasBonesInPrefab: true,
                looksLikeArmatureWithHipsRoot: false,
                reason: "Fallback -> Accessory; suggest add MA Bone Proxy.");
        }

        // --- Step 1: MA component detection (root / depth1 only) ---

        private static AmariModularAvatarComponentKind FindMaOnRootOrFirstChildren(GameObject root)
        {
            // root
            var onRoot = GetMaKindOnGameObject(root);
            if (onRoot != AmariModularAvatarComponentKind.None) return onRoot;

            // 1階層下
            var t = root.transform;
            for (int i = 0; i < t.childCount; i++)
            {
                var child = t.GetChild(i);
                var onChild = GetMaKindOnGameObject(child.gameObject);
                if (onChild != AmariModularAvatarComponentKind.None) return onChild;
            }

            return AmariModularAvatarComponentKind.None;
        }

        private static AmariModularAvatarComponentKind GetMaKindOnGameObject(GameObject go)
        {
            if (go == null) return AmariModularAvatarComponentKind.None;

            if (go.GetComponent<ModularAvatarMergeArmature>() != null) return AmariModularAvatarComponentKind.MergeArmature;
            if (go.GetComponent<ModularAvatarBoneProxy>() != null) return AmariModularAvatarComponentKind.BoneProxy;

            return AmariModularAvatarComponentKind.None;
        }

        // --- Step 2: mesh detection ---

        private static bool HasAnyMesh(GameObject root)
        {
            if (root == null) return false;

            // SkinnedMeshRenderer or MeshRenderer+MeshFilter を対象にする
            if (root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length > 0) return true;

            var meshRenderers = root.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < meshRenderers.Length; i++)
            {
                var mr = meshRenderers[i];
                if (mr == null) continue;
                var mf = mr.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) return true;
            }

            return false;
        }

        // --- Step 3: bones existence inside prefab ---

        private static bool HasAnyBonesInsidePrefab(GameObject root)
        {
            if (root == null) return false;

            var smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (smrs.Length == 0) return false;

            var rootT = root.transform;

            foreach (var smr in smrs)
            {
                if (smr == null) continue;

                // rootBone がプレハブ内ならボーン有り扱い
                var rb = smr.rootBone;
                if (rb != null && rb.IsChildOf(rootT)) return true;

                // bones 配列にプレハブ内Transformが含まれていればボーン有り扱い
                var bones = smr.bones;
                if (bones == null) continue;

                for (int i = 0; i < bones.Length; i++)
                {
                    var b = bones[i];
                    if (b != null && b.IsChildOf(rootT)) return true;
                }
            }

            return false;
        }

        // --- Step 4: armature + hips root ---

        private static bool LooksLikeArmatureWithHipsRoot(GameObject root)
        {
            if (root == null) return false;

            var armature = FindFirstByNameIgnoreCase(root.transform, "Armature");

            if (armature == null) return false;

            // Armature直下に Hips がいるか
            Transform hips = null;
            for (int i = 0; i < armature.childCount; i++)
            {
                var c = armature.GetChild(i);
                if (c == null) continue;
                if (string.Equals(c.name, "Hips", StringComparison.OrdinalIgnoreCase))
                {
                    hips = c;
                    break;
                }
            }
            if (hips == null) return false;

            // SkinnedMeshRendererのrootBoneがHips（またはHips配下）になっているかで確度を上げる
            var smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var smr in smrs)
            {
                if (smr == null) continue;
                var rb = smr.rootBone;
                if (rb == null) continue;

                if (!rb.IsChildOf(root.transform)) continue; // 外部参照は除外
                if (rb == hips || rb.IsChildOf(hips)) return true;
            }

            // rootBone未設定でも「Armature直下がHips」なら衣装形式として扱う
            return true;
        }

        private static Transform FindFirstByNameIgnoreCase(Transform root, string name)
        {
            // TODO 名前の前方一致も許したい ModularAvatarを参考にする
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                if (string.Equals(t.name, name, StringComparison.OrdinalIgnoreCase)) return t;
            }
            return null;
        }
    }
}
