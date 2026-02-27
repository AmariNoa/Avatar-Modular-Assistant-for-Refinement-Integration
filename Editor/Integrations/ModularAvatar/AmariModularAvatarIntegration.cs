using System;
using System.Collections.Generic;
using System.Linq;
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

        private static AmariModularAvatarComponentKind FindMaOnRootOrFirstChildren(GameObject root)
        {
            var onRoot = GetMaKindOnGameObject(root);
            if (onRoot != AmariModularAvatarComponentKind.None) return onRoot;

            var t = root.transform;
            for (var i = 0; i < t.childCount; i++)
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

        private static bool HasAnyMesh(GameObject root)
        {
            if (root == null) return false;

            if (root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length > 0) return true;

            var meshRenderers = root.GetComponentsInChildren<MeshRenderer>(true);
            for (var i = 0; i < meshRenderers.Length; i++)
            {
                var mr = meshRenderers[i];
                if (mr == null) continue;
                var mf = mr.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) return true;
            }

            return false;
        }

        private static bool HasAnyBonesInsidePrefab(GameObject root)
        {
            if (root == null) return false;

            var smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (smrs.Length == 0) return false;

            var rootT = root.transform;

            foreach (var smr in smrs)
            {
                if (smr == null) continue;

                var rb = smr.rootBone;
                if (rb != null && rb.IsChildOf(rootT)) return true;

                var bones = smr.bones;
                if (bones == null) continue;

                for (var i = 0; i < bones.Length; i++)
                {
                    var b = bones[i];
                    if (b != null && b.IsChildOf(rootT)) return true;
                }
            }

            return false;
        }

        private static bool LooksLikeArmatureWithHipsRoot(GameObject root)
        {
            if (root == null) return false;

            var armature = FindFirstByNameIgnoreCase(root.transform, "Armature");

            if (armature == null) return false;

            Transform hips = null;
            var hipsAccepted = GetAcceptedNames("Hips");
            for (var i = 0; i < armature.childCount; i++)
            {
                var c = armature.GetChild(i);
                if (c == null) continue;
                if (IsNameInAcceptedSet(c.name, hipsAccepted))
                {
                    hips = c;
                    break;
                }
            }
            if (hips == null) return false;

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

        // This list is originally from https://github.com/HhotateA/AvatarModifyTools/blob/d8ae75fed8577707253d6b63a64d6053eebbe78b/Assets/HhotateA/AvatarModifyTool/Editor/EnvironmentVariable.cs#L81-L139
        // Copyright (c) 2021 @HhotateA_xR
        // Licensed under the MIT License
        // In addition, some part is copied from from https://github.com/Azukimochi/BoneRenamer/blob/6ec12b848830f467e35ddf7ff105aaa72be02908/BoneNames.xml
        // Copyright (c) 2023 Azukimochi
        // Licensed under the MIT License
        // Portions derived from Modular Avatar (https://github.com/bdunderscore/modular-avatar/blob/main/Editor/HeuristicBoneMapper.cs)
        // Copyright (c) 2022 bd_
        // Licensed under the MIT License
        private static readonly string[][] BoneNamePatterns = new[]
        {
            new[] {"Hips", "Hip", "pelvis"},
            new[]
            {
                "LeftUpperLeg", "UpperLeg_Left", "UpperLeg_L", "Leg_Left", "Leg_L", "ULeg_L", "Left leg", "LeftUpLeg",
                "UpLeg.L", "Thigh_L"
            },
            new[]
            {
                "RightUpperLeg", "UpperLeg_Right", "UpperLeg_R", "Leg_Right", "Leg_R", "ULeg_R", "Right leg",
                "RightUpLeg", "UpLeg.R", "Thigh_R"
            },
            new[]
            {
                "LeftLowerLeg", "LowerLeg_Left", "LowerLeg_L", "Knee_Left", "Knee_L", "LLeg_L", "Left knee", "LeftLeg", "leg_L", "shin.L"
            },
            new[]
            {
                "RightLowerLeg", "LowerLeg_Right", "LowerLeg_R", "Knee_Right", "Knee_R", "LLeg_R", "Right knee",
                "RightLeg", "leg_R", "shin.R"
            },
            new[] {"LeftFoot", "Foot_Left", "Foot_L", "Ankle_L", "Foot.L.001", "Left ankle", "heel.L", "heel"},
            new[] {"RightFoot", "Foot_Right", "Foot_R", "Ankle_R", "Foot.R.001", "Right ankle", "heel.R", "heel"},
            new[] {"Spine", "spine01"},
            new[] {"Chest", "Bust", "spine02", "upper_chest"},
            new[] {"Neck"},
            new[] {"Head"},
            new[] {"LeftShoulder", "Shoulder_Left", "Shoulder_L"},
            new[] {"RightShoulder", "Shoulder_Right", "Shoulder_R"},
            new[]
            {
                "LeftUpperArm", "UpperArm_Left", "UpperArm_L", "Arm_Left", "Arm_L", "UArm_L", "Left arm", "UpperLeftArm"
            },
            new[]
            {
                "RightUpperArm", "UpperArm_Right", "UpperArm_R", "Arm_Right", "Arm_R", "UArm_R", "Right arm",
                "UpperRightArm"
            },
            new[] {"LeftLowerArm", "LowerArm_Left", "LowerArm_L", "LArm_L", "Left elbow", "LeftForeArm", "Elbow_L", "forearm_L", "ForArm_L"},
            new[] {"RightLowerArm", "LowerArm_Right", "LowerArm_R", "LArm_R", "Right elbow", "RightForeArm", "Elbow_R", "forearm_R", "ForArm_R"},
            new[] {"LeftHand", "Hand_Left", "Hand_L", "Left wrist", "Wrist_L"},
            new[] {"RightHand", "Hand_Right", "Hand_R", "Right wrist", "Wrist_R"},
            new[]
            {
                "LeftToes", "Toes_Left", "Toe_Left", "ToeIK_L", "Toes_L", "Toe_L", "Foot.L.002", "Left Toe",
                "LeftToeBase"
            },
            new[]
            {
                "RightToes", "Toes_Right", "Toe_Right", "ToeIK_R", "Toes_R", "Toe_R", "Foot.R.002", "Right Toe",
                "RightToeBase"
            },
            new[] {"LeftEye", "Eye_Left", "Eye_L"},
            new[] {"RightEye", "Eye_Right", "Eye_R"},
            new[] {"Jaw"},
            new[]
            {
                "LeftThumbProximal", "ProximalThumb_Left", "ProximalThumb_L", "Thumb1_L", "ThumbFinger1_L",
                "LeftHandThumb1", "Thumb Proximal.L", "Thunb1_L", "finger01_01_L"
            },
            new[]
            {
                "LeftThumbIntermediate", "IntermediateThumb_Left", "IntermediateThumb_L", "Thumb2_L", "ThumbFinger2_L",
                "LeftHandThumb2", "Thumb Intermediate.L", "Thunb2_L", "finger01_02_L"
            },
            new[]
            {
                "LeftThumbDistal", "DistalThumb_Left", "DistalThumb_L", "Thumb3_L", "ThumbFinger3_L", "LeftHandThumb3",
                "Thumb Distal.L", "Thunb3_L", "finger01_03_L"
            },
            new[]
            {
                "LeftIndexProximal", "ProximalIndex_Left", "ProximalIndex_L", "Index1_L", "IndexFinger1_L",
                "LeftHandIndex1", "Index Proximal.L", "finger02_01_L", "f_index.01.L"
            },
            new[]
            {
                "LeftIndexIntermediate", "IntermediateIndex_Left", "IntermediateIndex_L", "Index2_L", "IndexFinger2_L",
                "LeftHandIndex2", "Index Intermediate.L", "finger02_02_L", "f_index.02.L"
            },
            new[]
            {
                "LeftIndexDistal", "DistalIndex_Left", "DistalIndex_L", "Index3_L", "IndexFinger3_L", "LeftHandIndex3",
                "Index Distal.L", "finger02_03_L", "f_index.03.L"
            },
            new[]
            {
                "LeftMiddleProximal", "ProximalMiddle_Left", "ProximalMiddle_L", "Middle1_L", "MiddleFinger1_L",
                "LeftHandMiddle1", "Middle Proximal.L", "finger03_01_L", "f_middle.01.L"
            },
            new[]
            {
                "LeftMiddleIntermediate", "IntermediateMiddle_Left", "IntermediateMiddle_L", "Middle2_L",
                "MiddleFinger2_L", "LeftHandMiddle2", "Middle Intermediate.L", "finger03_02_L", "f_middle.02.L"
            },
            new[]
            {
                "LeftMiddleDistal", "DistalMiddle_Left", "DistalMiddle_L", "Middle3_L", "MiddleFinger3_L",
                "LeftHandMiddle3", "Middle Distal.L", "finger03_03_L", "f_middle.03.L"
            },
            new[]
            {
                "LeftRingProximal", "ProximalRing_Left", "ProximalRing_L", "Ring1_L", "RingFinger1_L", "LeftHandRing1",
                "Ring Proximal.L", "finger04_01_L", "f_ring.01.L"
            },
            new[]
            {
                "LeftRingIntermediate", "IntermediateRing_Left", "IntermediateRing_L", "Ring2_L", "RingFinger2_L",
                "LeftHandRing2", "Ring Intermediate.L", "finger04_02_L", "f_ring.02.L"
            },
            new[]
            {
                "LeftRingDistal", "DistalRing_Left", "DistalRing_L", "Ring3_L", "RingFinger3_L", "LeftHandRing3",
                "Ring Distal.L", "finger04_03_L", "f_ring.03.L"
            },
            new[]
            {
                "LeftLittleProximal", "ProximalLittle_Left", "ProximalLittle_L", "Little1_L", "LittleFinger1_L",
                "LeftHandPinky1", "Little Proximal.L", "finger05_01_L", "f_pinky.01.L", "Pinky1.L"
            },
            new[]
            {
                "LeftLittleIntermediate", "IntermediateLittle_Left", "IntermediateLittle_L", "Little2_L",
                "LittleFinger2_L", "LeftHandPinky2", "Little Intermediate.L", "finger05_02_L", "f_pinky.02.L", "Pinky2.L"
            },
            new[]
            {
                "LeftLittleDistal", "DistalLittle_Left", "DistalLittle_L", "Little3_L", "LittleFinger3_L",
                "LeftHandPinky3", "Little Distal.L", "finger05_03_L", "f_pinky.03.L", "Pinky3.L"
            },
            new[]
            {
                "RightThumbProximal", "ProximalThumb_Right", "ProximalThumb_R", "Thumb1_R", "ThumbFinger1_R",
                "RightHandThumb1", "Thumb Proximal.R", "Thunb1_R", "finger01_01_R"
            },
            new[]
            {
                "RightThumbIntermediate", "IntermediateThumb_Right", "IntermediateThumb_R", "Thumb2_R",
                "ThumbFinger2_R", "RightHandThumb2", "Thumb Intermediate.R", "Thunb2_R", "finger01_02_R"
            },
            new[]
            {
                "RightThumbDistal", "DistalThumb_Right", "DistalThumb_R", "Thumb3_R", "ThumbFinger3_R",
                "RightHandThumb3", "Thumb Distal.R", "Thunb3_R", "finger01_03_R"
            },
            new[]
            {
                "RightIndexProximal", "ProximalIndex_Right", "ProximalIndex_R", "Index1_R", "IndexFinger1_R",
                "RightHandIndex1", "Index Proximal.R", "finger02_01_R", "f_index.01.R"
            },
            new[]
            {
                "RightIndexIntermediate", "IntermediateIndex_Right", "IntermediateIndex_R", "Index2_R",
                "IndexFinger2_R", "RightHandIndex2", "Index Intermediate.R", "finger02_02_R", "f_index.02.R"
            },
            new[]
            {
                "RightIndexDistal", "DistalIndex_Right", "DistalIndex_R", "Index3_R", "IndexFinger3_R",
                "RightHandIndex3", "Index Distal.R", "finger02_03_R", "f_index.03.R"
            },
            new[]
            {
                "RightMiddleProximal", "ProximalMiddle_Right", "ProximalMiddle_R", "Middle1_R", "MiddleFinger1_R",
                "RightHandMiddle1", "Middle Proximal.R", "finger03_01_R", "f_middle.01.R"
            },
            new[]
            {
                "RightMiddleIntermediate", "IntermediateMiddle_Right", "IntermediateMiddle_R", "Middle2_R",
                "MiddleFinger2_R", "RightHandMiddle2", "Middle Intermediate.R", "finger03_02_R", "f_middle.02.R"
            },
            new[]
            {
                "RightMiddleDistal", "DistalMiddle_Right", "DistalMiddle_R", "Middle3_R", "MiddleFinger3_R",
                "RightHandMiddle3", "Middle Distal.R", "finger03_03_R", "f_middle.03.R"
            },
            new[]
            {
                "RightRingProximal", "ProximalRing_Right", "ProximalRing_R", "Ring1_R", "RingFinger1_R",
                "RightHandRing1", "Ring Proximal.R", "finger04_01_R", "f_ring.01.R"
            },
            new[]
            {
                "RightRingIntermediate", "IntermediateRing_Right", "IntermediateRing_R", "Ring2_R", "RingFinger2_R",
                "RightHandRing2", "Ring Intermediate.R", "finger04_02_R", "f_ring.02.R"
            },
            new[]
            {
                "RightRingDistal", "DistalRing_Right", "DistalRing_R", "Ring3_R", "RingFinger3_R", "RightHandRing3",
                "Ring Distal.R", "finger04_03_R", "f_ring.03.R"
            },
            new[]
            {
                "RightLittleProximal", "ProximalLittle_Right", "ProximalLittle_R", "Little1_R", "LittleFinger1_R",
                "RightHandPinky1", "Little Proximal.R", "finger05_01_R", "f_pinky.01.R", "Pinky1.R"
            },
            new[]
            {
                "RightLittleIntermediate", "IntermediateLittle_Right", "IntermediateLittle_R", "Little2_R",
                "LittleFinger2_R", "RightHandPinky2", "Little Intermediate.R", "finger05_02_R", "f_pinky.02.R", "Pinky2.R"
            },
            new[]
            {
                "RightLittleDistal", "DistalLittle_Right", "DistalLittle_R", "Little3_R", "LittleFinger3_R",
                "RightHandPinky3", "Little Distal.R", "finger05_03_R", "f_pinky.03.R", "Pinky3.R"
            },
            new[] {"UpperChest", "UChest"},
        };

        private static readonly Dictionary<string, HashSet<string>> NormalizedBonePatternMap = BuildNormalizedBonePatternMap();

        private static Dictionary<string, HashSet<string>> BuildNormalizedBonePatternMap()
        {
            var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

            foreach (var pattern in BoneNamePatterns)
            {
                if (pattern == null) continue;

                var normalizedList = new List<string>();
                foreach (var name in pattern)
                {
                    var norm = NormalizeName(name);
                    if (!string.IsNullOrEmpty(norm))
                    {
                        normalizedList.Add(norm);
                    }
                }

                if (normalizedList.Count == 0)
                {
                    continue;
                }

                foreach (var norm in normalizedList)
                {
                    if (!map.TryGetValue(norm, out var set))
                    {
                        set = new HashSet<string>(StringComparer.Ordinal);
                        map[norm] = set;
                    }

                    foreach (var other in normalizedList)
                    {
                        set.Add(other);
                    }
                }
            }

            return map;
        }

        private static HashSet<string> GetAcceptedNames(string name)
        {
            var target = NormalizeName(name);
            if (string.IsNullOrEmpty(target)) return null;

            var accepted = new HashSet<string>(StringComparer.Ordinal) { target };
            if (NormalizedBonePatternMap.TryGetValue(target, out var mapped))
            {
                foreach (var m in mapped)
                {
                    accepted.Add(m);
                }
            }

            return accepted;
        }

        private static bool IsNameInAcceptedSet(string candidate, HashSet<string> accepted)
        {
            if (accepted == null || candidate == null) return false;
            var current = NormalizeName(candidate);
            return !string.IsNullOrEmpty(current) && accepted.Contains(current);
        }

        private static Transform FindFirstByNameIgnoreCase(Transform root, string name)
        {
            if (root == null || string.IsNullOrWhiteSpace(name)) return null;

            var accepted = GetAcceptedNames(name);
            if (accepted == null || accepted.Count == 0) return null;

            Transform prefixHit = null;
            Transform containsHit = null;

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;

                var current = NormalizeName(t.name);
                if (string.IsNullOrEmpty(current)) continue;

                if (accepted.Contains(current))
                {
                    return t;
                }

                if (prefixHit == null && StartsWithAny(current, accepted))
                {
                    prefixHit = t;
                }

                if (containsHit == null && ContainsAny(current, accepted))
                {
                    containsHit = t;
                }
            }

            return prefixHit ?? containsHit;

            static bool StartsWithAny(string value, HashSet<string> candidates)
            {
                return candidates.Any(c => value.StartsWith(c, StringComparison.Ordinal));
            }

            static bool ContainsAny(string value, HashSet<string> candidates)
            {
                return candidates.Any(c => value.Contains(c, StringComparison.Ordinal));
            }
        }

        private static string NormalizeName(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            // ModularAvatarのボーン探索挙動に合わせ、ケース無視＋記号/空白除去で緩く比較する
            var span = value.AsSpan();
            var buffer = new char[span.Length];
            var idx = 0;
            for (var i = 0; i < span.Length; i++)
            {
                var c = span[i];
                if (!char.IsLetterOrDigit(c)) continue;

                buffer[idx++] = char.ToLowerInvariant(c);
            }

            return new string(buffer, 0, idx);
        }
    }
}
