using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

// ReSharper disable once CheckNamespace
namespace com.amari_noa.avatar_modular_assistant.editor
{
    internal static class AmariAmriFileUtility
    {
        internal const string AmriExtension = ".amri";
        internal const string AmariDataFolderName = "_AMARI_DATA";
        internal const string ItemsFolderName = "Items";
        internal const string ManualImportedFolderName = "_AMARI_MANUAL_IMPORTED_";

        internal const string AmariDataAssetPath = "Assets/" + AmariDataFolderName;
        internal const string ItemsRootAssetPath = AmariDataAssetPath + "/" + ItemsFolderName;
        internal const string ManualImportedAssetPath = ItemsRootAssetPath + "/" + ManualImportedFolderName;

        private const string AssetsRootSegment = "Assets/";

        internal static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            return path.Replace('\\', '/').Trim();
        }

        internal static string NormalizeAbsolutePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(path).Replace('\\', '/');
            }
            catch
            {
                return NormalizePath(path);
            }
        }

        internal static bool IsAssetPathUnderItemsRoot(string assetPath)
        {
            var normalized = NormalizePath(assetPath);
            if (string.IsNullOrEmpty(normalized))
            {
                return false;
            }

            return normalized.StartsWith(ItemsRootAssetPath + "/", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsAbsolutePathUnderItemsRoot(string absolutePath)
        {
            var normalizedAbsolute = NormalizeAbsolutePath(absolutePath);
            if (string.IsNullOrEmpty(normalizedAbsolute))
            {
                return false;
            }

            var dataPath = NormalizeAbsolutePath(Application.dataPath).TrimEnd('/');
            var itemsRootAbsolute = dataPath + "/" + AmariDataFolderName + "/" + ItemsFolderName;
            return normalizedAbsolute.StartsWith(itemsRootAbsolute + "/", StringComparison.OrdinalIgnoreCase);
        }

        internal static string AbsoluteToAssetPath(string absolutePath)
        {
            var normalizedAbsolute = NormalizeAbsolutePath(absolutePath);
            if (string.IsNullOrEmpty(normalizedAbsolute))
            {
                return string.Empty;
            }

            var dataPath = NormalizeAbsolutePath(Application.dataPath).TrimEnd('/');
            if (string.Equals(normalizedAbsolute, dataPath, StringComparison.OrdinalIgnoreCase))
            {
                return "Assets";
            }

            if (!normalizedAbsolute.StartsWith(dataPath + "/", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return "Assets/" + normalizedAbsolute.Substring(dataPath.Length + 1);
        }

        internal static string AssetPathToAbsolute(string assetPath)
        {
            var normalized = NormalizePath(assetPath);
            if (string.IsNullOrEmpty(normalized) ||
                !normalized.StartsWith(AssetsRootSegment, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(normalized, "Assets", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            var dataPath = NormalizeAbsolutePath(Application.dataPath).TrimEnd('/');
            if (string.Equals(normalized, "Assets", StringComparison.OrdinalIgnoreCase))
            {
                return dataPath;
            }

            return dataPath + "/" + normalized.Substring(AssetsRootSegment.Length);
        }

        internal static bool IsValidExportedAssetPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var normalized = NormalizePath(value);
            if (normalized.Contains(".."))
            {
                return false;
            }

            if (!normalized.StartsWith(AssetsRootSegment, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!IsAssetPathUnderItemsRoot(normalized))
            {
                return false;
            }

            if (!normalized.EndsWith(AmriExtension, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        internal static string ComputeFileSha256(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath) || !File.Exists(absolutePath))
            {
                return string.Empty;
            }

            try
            {
                using var stream = File.OpenRead(absolutePath);
                using var sha = SHA256.Create();
                var hash = sha.ComputeHash(stream);
                var sb = new System.Text.StringBuilder(hash.Length * 2);
                foreach (var b in hash)
                {
                    sb.Append(b.ToString("x2"));
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AMARI] Failed to compute hash for {absolutePath}: {ex.Message}");
                return string.Empty;
            }
        }

        internal static bool TryEnsureAssetFolder(string assetFolderPath, out string error)
        {
            error = null;
            var normalized = NormalizePath(assetFolderPath);
            if (string.IsNullOrEmpty(normalized))
            {
                error = "Folder path is empty.";
                return false;
            }

            if (AssetDatabase.IsValidFolder(normalized))
            {
                return true;
            }

            var absolute = AssetPathToAbsolute(normalized);
            if (string.IsNullOrEmpty(absolute))
            {
                error = $"Folder path is not under Assets: {assetFolderPath}";
                return false;
            }

            try
            {
                Directory.CreateDirectory(absolute);
                AssetDatabase.Refresh();
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }

            return AssetDatabase.IsValidFolder(normalized);
        }

        internal static string ResolveUnusedAssetPath(string desiredAssetPath)
        {
            var normalized = NormalizePath(desiredAssetPath);
            if (string.IsNullOrEmpty(normalized))
            {
                return normalized;
            }

            var absolute = AssetPathToAbsolute(normalized);
            if (string.IsNullOrEmpty(absolute) || !File.Exists(absolute))
            {
                return normalized;
            }

            var directory = Path.GetDirectoryName(normalized)?.Replace('\\', '/') ?? string.Empty;
            var extension = Path.GetExtension(normalized);
            var baseName = Path.GetFileNameWithoutExtension(normalized);

            var highest = 0;
            try
            {
                var directoryAbsolute = AssetPathToAbsolute(directory);
                if (!string.IsNullOrEmpty(directoryAbsolute) && Directory.Exists(directoryAbsolute))
                {
                    foreach (var file in Directory.EnumerateFiles(directoryAbsolute, $"{baseName}_*{extension}", SearchOption.TopDirectoryOnly))
                    {
                        var name = Path.GetFileNameWithoutExtension(file);
                        var suffix = name.Substring(baseName.Length + 1);
                        if (int.TryParse(suffix, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n) && n > highest)
                        {
                            highest = n;
                        }
                    }
                }
            }
            catch
            {
                // 列挙に失敗しても素直にカウントアップで埋める
            }

            var next = highest + 1;
            while (true)
            {
                var candidate = string.IsNullOrEmpty(directory)
                    ? $"{baseName}_{next}{extension}"
                    : $"{directory}/{baseName}_{next}{extension}";
                var candidateAbsolute = AssetPathToAbsolute(candidate);
                if (!File.Exists(candidateAbsolute))
                {
                    return candidate;
                }

                next++;
            }
        }

        internal static bool TryCopyFileIntoAssets(string sourceAbsolutePath, string destinationAssetPath, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(sourceAbsolutePath) || !File.Exists(sourceAbsolutePath))
            {
                error = $"Source not found: {sourceAbsolutePath}";
                return false;
            }

            var destinationAbsolute = AssetPathToAbsolute(destinationAssetPath);
            if (string.IsNullOrEmpty(destinationAbsolute))
            {
                error = $"Destination is not under Assets: {destinationAssetPath}";
                return false;
            }

            var destinationDirectory = Path.GetDirectoryName(destinationAbsolute);
            try
            {
                if (!string.IsNullOrEmpty(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                File.Copy(sourceAbsolutePath, destinationAbsolute, true);
                AssetDatabase.ImportAsset(NormalizePath(destinationAssetPath), ImportAssetOptions.ForceSynchronousImport);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }

            return true;
        }

        internal static bool TryMoveAsset(string fromAssetPath, string toAssetPath, out string error)
        {
            error = null;
            var from = NormalizePath(fromAssetPath);
            var to = NormalizePath(toAssetPath);
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
            {
                error = "Empty path.";
                return false;
            }

            var parentFolder = Path.GetDirectoryName(to)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parentFolder) && !TryEnsureAssetFolder(parentFolder, out var folderError))
            {
                error = folderError;
                return false;
            }

            var validation = AssetDatabase.ValidateMoveAsset(from, to);
            if (!string.IsNullOrEmpty(validation))
            {
                error = validation;
                return false;
            }

            var result = AssetDatabase.MoveAsset(from, to);
            if (!string.IsNullOrEmpty(result))
            {
                error = result;
                return false;
            }

            return true;
        }

        internal static bool TryDeleteAsset(string assetPath)
        {
            var normalized = NormalizePath(assetPath);
            if (string.IsNullOrEmpty(normalized))
            {
                return false;
            }

            return AssetDatabase.DeleteAsset(normalized);
        }

        internal static IReadOnlyList<string> EnumerateManagedAmriAssetPaths()
        {
            var results = new List<string>();
            var rootAbsolute = AssetPathToAbsolute(ItemsRootAssetPath);
            if (string.IsNullOrEmpty(rootAbsolute) || !Directory.Exists(rootAbsolute))
            {
                return results;
            }

            try
            {
                foreach (var absolute in Directory.EnumerateFiles(rootAbsolute, "*" + AmriExtension, SearchOption.AllDirectories))
                {
                    var assetPath = AbsoluteToAssetPath(absolute);
                    if (!string.IsNullOrEmpty(assetPath))
                    {
                        results.Add(assetPath);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AMARI] Failed to enumerate managed amri files: {ex.Message}");
            }

            return results;
        }
    }
}
