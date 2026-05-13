using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// ReSharper disable once CheckNamespace
namespace com.amari_noa.avatar_modular_assistant.editor
{
    public partial class AmariAvatarCustomizeWindow
    {
        private sealed class AmriImportEntry
        {
            public string SourceAbsolutePath;
            public ImportedItemGroupData ParsedData;
            public string DesiredAssetPath;
            public bool DesiredIsExportedTarget;
            public string ResolvedAssetPath;
            public bool WasNewlyPlaced;
            public bool HashMatchedExisting;
            public bool Skipped;
        }

        internal void ImportSingleAmriFiles(IReadOnlyList<string> sourceAbsolutePaths, ScrollView tabScrollView, VisualElement root)
        {
            if (sourceAbsolutePaths == null || sourceAbsolutePaths.Count == 0)
            {
                return;
            }

            if (tabScrollView == null || _avatarSettings == null)
            {
                return;
            }

            var unique = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in sourceAbsolutePaths)
            {
                var normalized = AmariAmriFileUtility.NormalizeAbsolutePath(raw);
                if (string.IsNullOrEmpty(normalized) || !File.Exists(normalized))
                {
                    continue;
                }

                if (!seen.Add(normalized))
                {
                    continue;
                }

                unique.Add(normalized);
            }

            if (unique.Count == 0)
            {
                return;
            }

            var entries = new List<AmriImportEntry>();
            var broken = new List<string>();
            foreach (var source in unique)
            {
                try
                {
                    var json = File.ReadAllText(source, Encoding.UTF8);
                    if (TryParseImportedItemGroupJson(json, out var imported, out _))
                    {
                        entries.Add(new AmriImportEntry
                        {
                            SourceAbsolutePath = source,
                            ParsedData = imported
                        });
                    }
                    else
                    {
                        broken.Add(source);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AMARI] Failed to read amri ({source}): {ex.Message}");
                    broken.Add(source);
                }
            }

            if (broken.Count > 0)
            {
                ShowAmriBrokenFilesDialog(broken);
            }

            if (entries.Count == 0)
            {
                return;
            }

            DecideAmriDesiredAssetPaths(entries);

            if (!ResolveAndPlaceAmriEntries(entries))
            {
                return;
            }

            var applicable = entries
                .Where(entry => !entry.Skipped && !string.IsNullOrWhiteSpace(entry.ResolvedAssetPath))
                .ToList();

            if (applicable.Count == 0)
            {
                return;
            }

            ShowImportSuccessDialog(applicable.Count);

            var isSingleAutoApply = unique.Count == 1 && applicable.Count == 1;
            if (isSingleAutoApply)
            {
                ApplyAmriAssetPaths(applicable.Select(entry => entry.ResolvedAssetPath).ToList(), tabScrollView, root);
                return;
            }

            OpenAmriApplySelectionModal(applicable.Select(entry => entry.ResolvedAssetPath).ToList(), tabScrollView, root);
        }

        private static void DecideAmriDesiredAssetPaths(IReadOnlyList<AmriImportEntry> entries)
        {
            foreach (var entry in entries)
            {
                var exportedAssetPath = entry?.ParsedData?.exportedAssetPath;
                if (AmariAmriFileUtility.IsValidExportedAssetPath(exportedAssetPath))
                {
                    entry.DesiredAssetPath = AmariAmriFileUtility.NormalizePath(exportedAssetPath);
                    entry.DesiredIsExportedTarget = true;
                    continue;
                }

                var fileName = Path.GetFileName(entry.SourceAbsolutePath);
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    fileName = "imported.amri";
                }

                entry.DesiredAssetPath = AmariAmriFileUtility.ManualImportedAssetPath + "/" + fileName;
                entry.DesiredIsExportedTarget = false;
            }
        }

        private bool ResolveAndPlaceAmriEntries(List<AmriImportEntry> entries)
        {
            var placed = new List<string>();
            var exportedBatchDecision = AmriDuplicateBatchChoice.Individual;
            var exportedBatchDecided = false;

            foreach (var entry in entries)
            {
                var directory = Path.GetDirectoryName(entry.DesiredAssetPath)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(directory) && !AmariAmriFileUtility.TryEnsureAssetFolder(directory, out var folderError))
                {
                    Debug.LogError($"[AMARI] Failed to ensure folder {directory}: {folderError}");
                    entry.Skipped = true;
                    continue;
                }

                var desiredAbsolute = AmariAmriFileUtility.AssetPathToAbsolute(entry.DesiredAssetPath);
                var conflict = !string.IsNullOrEmpty(desiredAbsolute) && File.Exists(desiredAbsolute);

                if (conflict)
                {
                    var sourceHash = AmariAmriFileUtility.ComputeFileSha256(entry.SourceAbsolutePath);
                    var existingHash = AmariAmriFileUtility.ComputeFileSha256(desiredAbsolute);
                    if (!string.IsNullOrEmpty(sourceHash) &&
                        string.Equals(sourceHash, existingHash, StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.Log($"[AMARI] amri import skipped (same content already exists): {entry.DesiredAssetPath}");
                        entry.ResolvedAssetPath = entry.DesiredAssetPath;
                        entry.HashMatchedExisting = true;
                        continue;
                    }

                    if (entry.DesiredIsExportedTarget)
                    {
                        if (!exportedBatchDecided)
                        {
                            var pendingCount = entries.Count(e =>
                                e.DesiredIsExportedTarget &&
                                File.Exists(AmariAmriFileUtility.AssetPathToAbsolute(e.DesiredAssetPath)));
                            exportedBatchDecision = ShowAmriDuplicateBatchDialog(pendingCount);
                            exportedBatchDecided = true;
                            if (exportedBatchDecision == AmriDuplicateBatchChoice.CancelAll)
                            {
                                RollbackPlacedAmriAssets(placed);
                                return false;
                            }
                        }

                        switch (exportedBatchDecision)
                        {
                            case AmriDuplicateBatchChoice.OverwriteAll:
                                entry.ResolvedAssetPath = entry.DesiredAssetPath;
                                break;
                            case AmriDuplicateBatchChoice.SkipAll:
                                entry.Skipped = true;
                                continue;
                            case AmriDuplicateBatchChoice.Individual:
                                var itemChoice = ShowAmriDuplicateItemDialog(entry.DesiredAssetPath);
                                if (itemChoice == AmriDuplicateItemChoice.CancelAll)
                                {
                                    RollbackPlacedAmriAssets(placed);
                                    return false;
                                }

                                if (itemChoice == AmriDuplicateItemChoice.Skip)
                                {
                                    entry.Skipped = true;
                                    continue;
                                }

                                entry.ResolvedAssetPath = entry.DesiredAssetPath;
                                break;
                            default:
                                RollbackPlacedAmriAssets(placed);
                                return false;
                        }
                    }
                    else
                    {
                        entry.ResolvedAssetPath = AmariAmriFileUtility.ResolveUnusedAssetPath(entry.DesiredAssetPath);
                    }
                }
                else
                {
                    entry.ResolvedAssetPath = entry.DesiredAssetPath;
                }

                if (!AmariAmriFileUtility.TryCopyFileIntoAssets(entry.SourceAbsolutePath, entry.ResolvedAssetPath, out var copyError))
                {
                    Debug.LogError($"[AMARI] Failed to copy amri to {entry.ResolvedAssetPath}: {copyError}");
                    entry.Skipped = true;
                    continue;
                }

                entry.WasNewlyPlaced = true;
                placed.Add(entry.ResolvedAssetPath);
            }

            return true;
        }

        private void OpenAmriApplySelectionModal(IReadOnlyList<string> assetPaths, ScrollView tabScrollView, VisualElement root)
        {
            if (assetPaths == null || assetPaths.Count == 0)
            {
                return;
            }

            var items = new List<AmariAmriApplySelectionWindow.AmriApplyItem>();
            foreach (var assetPath in assetPaths)
            {
                var absolute = AmariAmriFileUtility.AssetPathToAbsolute(assetPath);
                var status = EvaluateAmriCandidateStatus(absolute);
                items.Add(new AmariAmriApplySelectionWindow.AmriApplyItem
                {
                    AssetPath = assetPath,
                    DisplayPath = assetPath,
                    Status = MapAmriCandidateStatus(status),
                    IsSelected = true
                });
            }

            IncrementAmriApplyModalCount();
            AmariAmriApplySelectionWindow.Open(
                items,
                Localize,
                (shouldApply, selectedPaths) =>
                {
                    try
                    {
                        if (!shouldApply || selectedPaths == null || selectedPaths.Count == 0)
                        {
                            return;
                        }

                        ApplyAmriAssetPaths(selectedPaths, tabScrollView, root);
                    }
                    finally
                    {
                        DecrementAmriApplyModalCount();
                    }
                });
        }

        private static AmariAmriApplySelectionWindow.AmriApplyItemStatus MapAmriCandidateStatus(AmriImportCandidateStatus status)
        {
            return status switch
            {
                AmriImportCandidateStatus.Info => AmariAmriApplySelectionWindow.AmriApplyItemStatus.Info,
                AmriImportCandidateStatus.Warning => AmariAmriApplySelectionWindow.AmriApplyItemStatus.Warning,
                _ => AmariAmriApplySelectionWindow.AmriApplyItemStatus.Critical
            };
        }

        internal HashSet<string> CaptureCurrentAmriAssetPathSnapshot()
        {
            return new HashSet<string>(EnumerateAllAmriAssetPaths(), StringComparer.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> EnumerateAllAmriAssetPaths()
        {
            var dataPath = Application.dataPath;
            if (!Directory.Exists(dataPath))
            {
                yield break;
            }

            foreach (var absolute in Directory.EnumerateFiles(dataPath, "*" + AmariAmriFileUtility.AmriExtension, SearchOption.AllDirectories))
            {
                var assetPath = AmariAmriFileUtility.AbsoluteToAssetPath(absolute);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    yield return assetPath;
                }
            }
        }

        internal void ProcessUnityPackageImportedAmri(HashSet<string> beforeSnapshot, ScrollView tabScrollView, VisualElement root)
        {
            if (beforeSnapshot == null || tabScrollView == null || _avatarSettings == null)
            {
                return;
            }

            AssetDatabase.Refresh();
            var afterSnapshot = new HashSet<string>(EnumerateAllAmriAssetPaths(), StringComparer.OrdinalIgnoreCase);
            var newlyAddedPaths = afterSnapshot.Where(path => !beforeSnapshot.Contains(path)).ToList();
            if (newlyAddedPaths.Count == 0)
            {
                return;
            }

            var unmanaged = newlyAddedPaths.Where(path => !AmariAmriFileUtility.IsAssetPathUnderItemsRoot(path)).ToList();
            var managed = newlyAddedPaths.Where(AmariAmriFileUtility.IsAssetPathUnderItemsRoot).ToList();
            var finalPaths = new List<string>(managed);

            if (unmanaged.Count > 0)
            {
                var batchChoice = ShowAmriUnmanagedMoveBatchDialog(unmanaged.Count);
                if (batchChoice == AmriUnmanagedMoveBatchChoice.CancelAll)
                {
                    return;
                }

                foreach (var assetPath in unmanaged)
                {
                    bool shouldMove;
                    switch (batchChoice)
                    {
                        case AmriUnmanagedMoveBatchChoice.MoveAll:
                            shouldMove = true;
                            break;
                        case AmriUnmanagedMoveBatchChoice.KeepAll:
                            shouldMove = false;
                            break;
                        case AmriUnmanagedMoveBatchChoice.Individual:
                            var itemChoice = ShowAmriUnmanagedMoveItemDialog(assetPath);
                            if (itemChoice == AmriUnmanagedMoveItemChoice.CancelAll)
                            {
                                return;
                            }

                            shouldMove = itemChoice == AmriUnmanagedMoveItemChoice.Move;
                            break;
                        default:
                            shouldMove = false;
                            break;
                    }

                    if (shouldMove)
                    {
                        var movedAssetPath = MoveUnmanagedAmriToManagedFolder(assetPath);
                        if (!string.IsNullOrWhiteSpace(movedAssetPath))
                        {
                            finalPaths.Add(movedAssetPath);
                            continue;
                        }
                    }

                    finalPaths.Add(assetPath);
                }
            }

            if (finalPaths.Count == 0)
            {
                return;
            }

            OpenAmriApplySelectionModal(finalPaths, tabScrollView, root);
        }

        private string MoveUnmanagedAmriToManagedFolder(string sourceAssetPath)
        {
            var sourceAbsolute = AmariAmriFileUtility.AssetPathToAbsolute(sourceAssetPath);
            if (string.IsNullOrEmpty(sourceAbsolute) || !File.Exists(sourceAbsolute))
            {
                return null;
            }

            string exportedAssetPath = null;
            try
            {
                var json = File.ReadAllText(sourceAbsolute, Encoding.UTF8);
                if (TryParseImportedItemGroupJson(json, out var imported, out _))
                {
                    exportedAssetPath = imported?.exportedAssetPath;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AMARI] Failed to read exportedAssetPath from {sourceAssetPath}: {ex.Message}");
            }

            string desiredAssetPath;
            if (AmariAmriFileUtility.IsValidExportedAssetPath(exportedAssetPath))
            {
                desiredAssetPath = AmariAmriFileUtility.NormalizePath(exportedAssetPath);
            }
            else
            {
                var fragment = sourceAssetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                    ? sourceAssetPath.Substring("Assets/".Length)
                    : sourceAssetPath;
                desiredAssetPath = AmariAmriFileUtility.ManualImportedAssetPath + "/" + fragment;
            }

            var directory = Path.GetDirectoryName(desiredAssetPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(directory) && !AmariAmriFileUtility.TryEnsureAssetFolder(directory, out var folderError))
            {
                Debug.LogError($"[AMARI] Failed to ensure folder {directory}: {folderError}");
                return null;
            }

            var resolvedTarget = AmariAmriFileUtility.ResolveUnusedAssetPath(desiredAssetPath);
            if (!AmariAmriFileUtility.TryMoveAsset(sourceAssetPath, resolvedTarget, out var moveError))
            {
                Debug.LogError($"[AMARI] Failed to move {sourceAssetPath} -> {resolvedTarget}: {moveError}");
                return null;
            }

            return resolvedTarget;
        }

        private void ApplyAmriAssetPaths(IReadOnlyList<string> assetPaths, ScrollView tabScrollView, VisualElement root)
        {
            if (assetPaths == null || assetPaths.Count == 0 || tabScrollView == null)
            {
                return;
            }

            foreach (var assetPath in assetPaths)
            {
                var absolute = AmariAmriFileUtility.AssetPathToAbsolute(assetPath);
                if (string.IsNullOrEmpty(absolute) || !File.Exists(absolute))
                {
                    Debug.LogWarning($"[AMARI] amri to apply was not found: {assetPath}");
                    continue;
                }

                try
                {
                    var json = File.ReadAllText(absolute, Encoding.UTF8);
                    if (!TryParseImportedItemGroupJson(json, out var imported, out var parseError))
                    {
                        Debug.LogError($"[AMARI] Failed to parse amri ({assetPath}): {parseError}");
                        continue;
                    }

                    ImportItemGroup(imported, tabScrollView, root);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[AMARI] Failed to apply amri ({assetPath}): {ex.Message}");
                }
            }
        }
    }
}
