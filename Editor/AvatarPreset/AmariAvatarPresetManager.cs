using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

// ReSharper disable once CheckNamespace
namespace com.amari_noa.avatar_modular_assistant.editor
{
    [InitializeOnLoad]
    public static class AmariAvatarPresetManager
    {
        private const string PresetParentDirPath = "Assets/_AMARI_DATA/Avatars";
        private const string DefaultLanguageCode = "en-US";

        private static readonly List<AvatarPreset> PresetList = new();
        private static readonly Dictionary<string, AvatarPreset> PresetByPath = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, AvatarPreset> PresetByAvatarPrefabGuid = new(StringComparer.Ordinal);

        public static event Action PresetsReloaded;

        static AmariAvatarPresetManager()
        {
            ReloadPresets();
        }

        public static IReadOnlyList<AvatarPreset> Presets => PresetList;
        public static int PresetCount => PresetList.Count;

        public static int ReloadPresets()
        {
            PresetList.Clear();
            PresetByPath.Clear();
            PresetByAvatarPrefabGuid.Clear();

            var jsonPaths = new List<string>(EnumeratePresetJsonFiles());
            jsonPaths.Sort(StringComparer.OrdinalIgnoreCase);

            foreach (var jsonPath in jsonPaths)
            {
                if (!TryLoadPreset(jsonPath, out var preset, out var error))
                {
                    Debug.LogWarning($"[AMARI] Avatar preset skipped: {ToAssetPath(jsonPath)} ({error})");
                    continue;
                }

                PresetList.Add(preset);
                PresetByPath[preset.SourceAssetPath] = preset;

                foreach (var avatarPrefabGuid in preset.AvatarPrefabGuids)
                {
                    if (string.IsNullOrWhiteSpace(avatarPrefabGuid))
                    {
                        continue;
                    }

                    PresetByAvatarPrefabGuid[avatarPrefabGuid] = preset;
                }
            }

            PresetList.Sort((left, right) =>
                string.Compare(left.SourceAssetPath, right.SourceAssetPath, StringComparison.OrdinalIgnoreCase));

            PresetsReloaded?.Invoke();
            return PresetList.Count;
        }

        public static bool TryGetPresetByAssetPath(string assetPath, out AvatarPreset preset)
        {
            preset = null;
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return false;
            }

            var normalizedPath = assetPath.Replace('\\', '/');
            if (Path.IsPathRooted(normalizedPath))
            {
                normalizedPath = ToAssetPath(normalizedPath);
            }

            return PresetByPath.TryGetValue(normalizedPath, out preset);
        }

        public static bool TryGetPresetByAvatarPrefabGuid(string avatarPrefabGuid, out AvatarPreset preset)
        {
            preset = null;
            if (string.IsNullOrWhiteSpace(avatarPrefabGuid))
            {
                return false;
            }

            var normalizedGuid = avatarPrefabGuid.Trim();
            if (PresetByAvatarPrefabGuid.TryGetValue(normalizedGuid, out preset))
            {
                return true;
            }

            foreach (var candidate in PresetList)
            {
                if (candidate == null || !candidate.ContainsAvatarPrefabGuid(normalizedGuid))
                {
                    continue;
                }

                preset = candidate;
                PresetByAvatarPrefabGuid[normalizedGuid] = candidate;
                return true;
            }

            return false;
        }

        public static AvatarPreset GetPresetByAvatarPrefabGuid(string avatarPrefabGuid)
        {
            return TryGetPresetByAvatarPrefabGuid(avatarPrefabGuid, out var preset) ? preset : null;
        }

        public static bool TryGetPresetByAvatarPrefab(GameObject avatarPrefab, out AvatarPreset preset)
        {
            preset = null;
            if (avatarPrefab == null)
            {
                return false;
            }

            var currentPrefab = avatarPrefab;
            while (currentPrefab != null)
            {
                var assetPath = AssetDatabase.GetAssetPath(currentPrefab);
                if (!string.IsNullOrWhiteSpace(assetPath))
                {
                    var prefabGuid = AssetDatabase.AssetPathToGUID(assetPath);
                    if (TryGetPresetByAvatarPrefabGuid(prefabGuid, out preset))
                    {
                        return true;
                    }
                }

                var parentPrefab = PrefabUtility.GetCorrespondingObjectFromSource(currentPrefab);
                if (parentPrefab == null || ReferenceEquals(parentPrefab, currentPrefab))
                {
                    break;
                }

                currentPrefab = parentPrefab;
            }

            return false;
        }

        public static bool TryGetAvatarPrefabGuidByAvatarPrefab(GameObject avatarPrefab, AvatarPreset preset, out string avatarPrefabGuid)
        {
            avatarPrefabGuid = null;
            if (avatarPrefab == null || preset == null)
            {
                return false;
            }

            var currentPrefab = avatarPrefab;
            while (currentPrefab != null)
            {
                var assetPath = AssetDatabase.GetAssetPath(currentPrefab);
                if (!string.IsNullOrWhiteSpace(assetPath))
                {
                    var candidateGuid = AssetDatabase.AssetPathToGUID(assetPath);
                    if (!string.IsNullOrWhiteSpace(candidateGuid) && preset.ContainsAvatarPrefabGuid(candidateGuid))
                    {
                        avatarPrefabGuid = candidateGuid;
                        return true;
                    }
                }

                var parentPrefab = PrefabUtility.GetCorrespondingObjectFromSource(currentPrefab);
                if (parentPrefab == null || ReferenceEquals(parentPrefab, currentPrefab))
                {
                    break;
                }

                currentPrefab = parentPrefab;
            }

            return false;
        }

        private static IEnumerable<string> EnumeratePresetJsonFiles()
        {
            var rootPath = GetPresetDirectoryAbsolutePath();
            if (!Directory.Exists(rootPath))
            {
                yield break;
            }

            IEnumerator<string> iterator;
            try
            {
                iterator = Directory.EnumerateFiles(rootPath, "*.json", SearchOption.AllDirectories).GetEnumerator();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AMARI] Failed to enumerate preset json files: {ex.Message}");
                yield break;
            }

            using (iterator)
            {
                while (true)
                {
                    string nextPath;
                    try
                    {
                        if (!iterator.MoveNext())
                        {
                            break;
                        }

                        nextPath = iterator.Current;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[AMARI] Failed while scanning preset json files: {ex.Message}");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(nextPath))
                    {
                        continue;
                    }

                    yield return nextPath;
                }
            }
        }

        private static bool TryLoadPreset(string absoluteJsonPath, out AvatarPreset preset, out string error)
        {
            preset = null;
            error = null;

            string json;
            try
            {
                json = File.ReadAllText(absoluteJsonPath, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                error = $"read failed: {ex.Message}";
                return false;
            }

            return TryParsePreset(json, ToAssetPath(absoluteJsonPath), out preset, out error);
        }

        private static bool TryParsePreset(string json, string sourceAssetPath, out AvatarPreset preset, out string error)
        {
            preset = null;
            error = null;

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (JsonException ex)
            {
                error = $"json parse failed: {ex.Message}";
                return false;
            }

            if (!TryReadLocalizedMap(root, "AvatarName", out var avatarNameMap, out error))
            {
                return false;
            }

            if (!TryReadLocalizedMap(root, "ShopName", out var shopNameMap, out error))
            {
                return false;
            }

            if (!TryReadStringList(root, "AvatarPrefabGuids", out var avatarPrefabGuids, out error))
            {
                return false;
            }

            if (!TryReadFloatMap(root, "SharedBaseBody", out var sharedBaseBodyMap, out error))
            {
                return false;
            }

            preset = new AvatarPreset(
                sourceAssetPath,
                avatarNameMap,
                shopNameMap,
                avatarPrefabGuids,
                sharedBaseBodyMap);

            return true;
        }

        private static bool TryReadLocalizedMap(JObject root, string key, out IReadOnlyDictionary<string, string> map, out string error)
        {
            map = null;
            error = null;

            if (root[key] is not JObject rawMap)
            {
                error = $"\"{key}\" must be an object";
                return false;
            }

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in rawMap.Properties())
            {
                if (string.IsNullOrWhiteSpace(pair.Name))
                {
                    continue;
                }

                if (pair.Value.Type != JTokenType.String)
                {
                    error = $"\"{key}.{pair.Name}\" must be a string";
                    return false;
                }

                result[pair.Name] = pair.Value.Value<string>() ?? string.Empty;
            }

            map = result;
            return true;
        }

        private static bool TryReadStringList(JObject root, string key, out IReadOnlyList<string> list, out string error)
        {
            list = null;
            error = null;

            if (root[key] is not JArray rawList)
            {
                error = $"\"{key}\" must be an array";
                return false;
            }

            var result = new List<string>(rawList.Count);
            foreach (var value in rawList)
            {
                if (value.Type != JTokenType.String)
                {
                    error = $"\"{key}\" array must contain only strings";
                    return false;
                }

                var text = value.Value<string>();
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                result.Add(text);
            }

            list = result;
            return true;
        }

        private static bool TryReadFloatMap(JObject root, string key, out IReadOnlyDictionary<string, float> map, out string error)
        {
            map = null;
            error = null;

            if (root[key] is not JObject rawMap)
            {
                error = $"\"{key}\" must be an object";
                return false;
            }

            var result = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (var pair in rawMap.Properties())
            {
                if (string.IsNullOrWhiteSpace(pair.Name))
                {
                    continue;
                }

                if (!TryConvertToFloat(pair.Value, out var floatValue))
                {
                    error = $"\"{key}.{pair.Name}\" must be a number";
                    return false;
                }

                result[pair.Name] = floatValue;
            }

            map = result;
            return true;
        }

        private static bool TryConvertToFloat(JToken value, out float floatValue)
        {
            if (value == null)
            {
                floatValue = 0;
                return false;
            }

            switch (value.Type)
            {
                case JTokenType.Integer:
                case JTokenType.Float:
                    floatValue = value.Value<float>();
                    return true;
                case JTokenType.String when float.TryParse(value.Value<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                    floatValue = parsed;
                    return true;
                default:
                    floatValue = 0;
                    return false;
            }
        }

        private static string GetPresetDirectoryAbsolutePath()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var relativePath = PresetParentDirPath.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
            return Path.GetFullPath(Path.Combine(projectRoot, relativePath));
        }

        private static string ToAssetPath(string absolutePath)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."))
                .Replace('\\', '/')
                .TrimEnd('/');

            var normalized = Path.GetFullPath(absolutePath).Replace('\\', '/');
            if (!normalized.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                return normalized;
            }

            return normalized[(projectRoot.Length + 1)..];
        }

        public sealed class AvatarPreset
        {
            public string SourceAssetPath { get; }
            public IReadOnlyDictionary<string, string> AvatarName { get; }
            public IReadOnlyDictionary<string, string> ShopName { get; }
            public IReadOnlyList<string> AvatarPrefabGuids { get; }
            public IReadOnlyDictionary<string, float> SharedBaseBody { get; }

            public AvatarPreset(
                string sourceAssetPath,
                IReadOnlyDictionary<string, string> avatarName,
                IReadOnlyDictionary<string, string> shopName,
                IReadOnlyList<string> avatarPrefabGuids,
                IReadOnlyDictionary<string, float> sharedBaseBody)
            {
                SourceAssetPath = sourceAssetPath;
                AvatarName = avatarName ?? new Dictionary<string, string>(StringComparer.Ordinal);
                ShopName = shopName ?? new Dictionary<string, string>(StringComparer.Ordinal);
                AvatarPrefabGuids = avatarPrefabGuids ?? new List<string>();
                SharedBaseBody = sharedBaseBody ?? new Dictionary<string, float>(StringComparer.Ordinal);
            }

            public string GetAvatarName(string languageCode = null)
            {
                return GetLocalizedText(AvatarName, languageCode);
            }

            public string GetShopName(string languageCode = null)
            {
                return GetLocalizedText(ShopName, languageCode);
            }

            public bool ContainsAvatarPrefabGuid(string avatarPrefabGuid)
            {
                if (string.IsNullOrWhiteSpace(avatarPrefabGuid))
                {
                    return false;
                }

                for (var i = 0; i < AvatarPrefabGuids.Count; i++)
                {
                    if (string.Equals(AvatarPrefabGuids[i], avatarPrefabGuid, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                return false;
            }

            private static string GetLocalizedText(IReadOnlyDictionary<string, string> source, string languageCode)
            {
                if (source == null || source.Count == 0)
                {
                    return string.Empty;
                }

                if (!string.IsNullOrWhiteSpace(languageCode) && source.TryGetValue(languageCode, out var localized))
                {
                    return localized;
                }

                if (source.TryGetValue(DefaultLanguageCode, out var defaultLanguageText))
                {
                    return defaultLanguageText;
                }

                foreach (var (_, value) in source)
                {
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }

                return string.Empty;
            }
        }

    }
}
