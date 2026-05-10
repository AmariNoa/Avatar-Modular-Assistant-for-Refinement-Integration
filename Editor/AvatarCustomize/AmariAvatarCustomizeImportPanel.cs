using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using com.amari_noa.avatar_modular_assistant.editor.integrations.blm_integration_core;
using com.amari_noa.unity_editor_localization_core.editor;
using com.amari_noa.unitypackage_pipeline_core.editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// ReSharper disable once CheckNamespace
namespace com.amari_noa.avatar_modular_assistant.editor
{
    public partial class AmariAvatarCustomizeWindow
    {
        private const string UnityPackageExtension = ".unitypackage";
        private const string AmriExtension = ".amri";

        private Button _importUnityPackageButton;
        private Button _importBlmButton;
        private VisualElement _importBlmButtonRoot;

        private bool _isDirectImportRunning;
        private readonly HashSet<string> _pendingDirectUnityPackagePaths = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, AmariUnityPackageImportResultContext> _directImportResultsByPath = new(StringComparer.OrdinalIgnoreCase);
        private List<string> _pendingDirectAmriPaths = new();

        private bool _isBlmBridgeInitialized;
        private bool _isBlmAvailable;
        private bool _isBlmModalOpen;
        private bool _isBlmEventsSubscribed;
        private string _activeBlmBatchId = string.Empty;
        private AmariBlmIntegrationCoreBridge _blmIntegrationCoreBridge;
        private readonly List<AmriImportCandidate> _pendingBlmAmriCandidates = new();

        private sealed class AmriImportCandidate
        {
            public string SourcePath;
            public string DisplayPath;
            public AmriImportCandidateStatus Status;
        }

        private enum AmriImportCandidateStatus
        {
            Info,
            Warning,
            Critical
        }

        private sealed class DirectImportFailure
        {
            public string SourcePath;
            public AmariUnityPackagePipelineOperationStatus Status;
            public AmariUnityPackageImportCancellationReason CancellationReason;
            public AmariUnityPackageImportFailureReason FailureReason;
            public string ErrorMessage;
        }

        private static string L(string key, string fallback = null)
        {
            return Localize(key, fallback);
        }

        private bool IsAnyImportFlowRunning()
        {
            return _isDirectImportRunning || (_blmIntegrationCoreBridge?.IsImportRunning ?? false) || _isBlmModalOpen;
        }

        private void SetupImportButtons(VisualElement root)
        {
            UnregisterImportButtonHandlers();
            EnsureBlmBridgeInitialized();

            if (root == null)
            {
                return;
            }

            _importUnityPackageButton = root.Q<Button>("ImportUnityPackageButton");
            _importBlmButton = root.Q<Button>("ImportBLMButton");
            _importBlmButtonRoot = root.Q<VisualElement>("ImportBLMButtonRoot");

            if (_importUnityPackageButton != null)
            {
                _importUnityPackageButton.clicked += OnImportUnityPackageButtonClicked;
            }

            if (_importBlmButton != null)
            {
                _importBlmButton.clicked += OnImportBlmButtonClicked;
            }

            if (_importBlmButtonRoot != null)
            {
                _importBlmButtonRoot.style.display = _isBlmAvailable ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void CleanupImportIntegrationOnDisable()
        {
            UnregisterImportButtonHandlers();
            StopDirectImportTracking();
            UnsubscribeBlmEvents();
            ClearBlmFlowState();
        }

        private void UnregisterImportButtonHandlers()
        {
            if (_importUnityPackageButton != null)
            {
                _importUnityPackageButton.clicked -= OnImportUnityPackageButtonClicked;
            }

            if (_importBlmButton != null)
            {
                _importBlmButton.clicked -= OnImportBlmButtonClicked;
            }

            _importUnityPackageButton = null;
            _importBlmButton = null;
            _importBlmButtonRoot = null;
        }

        private void OnImportUnityPackageButtonClicked()
        {
            if (_avatarSettings == null)
            {
                Debug.LogWarning("[AMARI] Avatar settings are not loaded.");
                return;
            }

            if (IsAnyImportFlowRunning())
            {
                Debug.LogWarning("[AMARI] Another import flow is currently running.");
                return;
            }

            var selectedPaths = OpenImportFileDialog();
            if (selectedPaths.Count == 0)
            {
                return;
            }

            ExecuteDirectImportFlow(selectedPaths);
        }

        private void ExecuteDirectImportFlow(IReadOnlyList<string> selectedPaths)
        {
            var unityPackagePaths = new List<string>();
            var amriPaths = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var selectedPath in selectedPaths)
            {
                var normalizedPath = NormalizeFilePath(selectedPath);
                if (string.IsNullOrWhiteSpace(normalizedPath) || !seen.Add(normalizedPath))
                {
                    continue;
                }

                var ext = (Path.GetExtension(normalizedPath) ?? string.Empty).ToLowerInvariant();
                switch (ext)
                {
                    case UnityPackageExtension:
                        if (!File.Exists(normalizedPath))
                        {
                            Debug.LogWarning($"[AMARI] UnityPackage file not found: {normalizedPath}");
                            continue;
                        }

                        unityPackagePaths.Add(normalizedPath);
                        break;
                    case AmriExtension:
                        if (!File.Exists(normalizedPath))
                        {
                            Debug.LogWarning($"[AMARI] .amri file not found: {normalizedPath}");
                            continue;
                        }

                        amriPaths.Add(normalizedPath);
                        break;
                    default:
                        Debug.LogWarning($"[AMARI] Unsupported extension skipped: {selectedPath}");
                        break;
                }
            }

            if (unityPackagePaths.Count == 0 && amriPaths.Count == 0)
            {
                return;
            }

            if (unityPackagePaths.Count == 0)
            {
                ImportAmriFiles(amriPaths);
                return;
            }

            BeginDirectUnityPackageImport(unityPackagePaths, amriPaths);
        }

        private void BeginDirectUnityPackageImport(IReadOnlyList<string> unityPackagePaths, IReadOnlyList<string> amriPaths)
        {
            var pipeline = AmariUnityPackageImportPipeline.Service;
            if (pipeline == null)
            {
                ShowImportFailureDialog(
                    LocalizeStatusText(AmariUnityPackagePipelineOperationStatus.Failed),
                    L(
                        "amari.window.avatarCustomize.import.failure.failed.pipelineUnavailable",
                        "UnityPackage import pipeline is not available."),
                    Array.Empty<string>());
                return;
            }

            ResetUnityPackagePipelineIfBusy(pipeline);

            _pendingDirectAmriPaths = amriPaths?.ToList() ?? new List<string>();
            _pendingDirectUnityPackagePaths.Clear();
            _directImportResultsByPath.Clear();

            var requests = new List<AmariUnityPackageImportRequest>();
            foreach (var unityPackagePath in unityPackagePaths)
            {
                var normalizedPath = NormalizeFilePath(unityPackagePath);
                if (string.IsNullOrWhiteSpace(normalizedPath))
                {
                    continue;
                }

                if (!_pendingDirectUnityPackagePaths.Add(normalizedPath))
                {
                    continue;
                }

                requests.Add(new AmariUnityPackageImportRequest(normalizedPath, Array.Empty<string>()));
            }

            if (requests.Count == 0)
            {
                var directAmriPaths = _pendingDirectAmriPaths;
                _pendingDirectAmriPaths = new List<string>();
                ImportAmriFiles(directAmriPaths);
                return;
            }

            _isDirectImportRunning = true;
            pipeline.ImportRequestFinalized -= OnDirectUnityPackageImportRequestFinalized;
            pipeline.ImportRequestFinalized += OnDirectUnityPackageImportRequestFinalized;
            pipeline.EnqueueMultiple(requests);
            pipeline.StartImport();
        }

        private void OnDirectUnityPackageImportRequestFinalized(AmariUnityPackageImportResultContext result)
        {
            if (!_isDirectImportRunning || result == null || string.IsNullOrWhiteSpace(result.PackagePath))
            {
                return;
            }

            var normalizedPath = NormalizeFilePath(result.PackagePath);
            if (string.IsNullOrWhiteSpace(normalizedPath) || !_pendingDirectUnityPackagePaths.Remove(normalizedPath))
            {
                return;
            }

            _directImportResultsByPath[normalizedPath] = result;
            if (_pendingDirectUnityPackagePaths.Count > 0)
            {
                return;
            }

            var failures = _directImportResultsByPath.Values
                .Where(context => context != null && context.ImportStatus != AmariUnityPackagePipelineOperationStatus.Completed)
                .Select(context => new DirectImportFailure
                {
                    SourcePath = context.PackagePath ?? string.Empty,
                    Status = context.ImportStatus,
                    CancellationReason = context.CancellationReason,
                    FailureReason = context.FailureReason,
                    ErrorMessage = context.ErrorMessage ?? string.Empty
                })
                .ToList();

            var directAmriPaths = _pendingDirectAmriPaths?.ToList() ?? new List<string>();
            StopDirectImportTracking();

            if (failures.Count > 0)
            {
                var primary = failures.FirstOrDefault() ?? new DirectImportFailure
                {
                    Status = AmariUnityPackagePipelineOperationStatus.Failed,
                    ErrorMessage = string.Empty
                };
                var primaryStatusText = LocalizeStatusText(primary.Status);
                var primaryReasonMessage = BuildLocalizedDirectImportReasonMessage(primary);
                var failedPaths = failures
                    .Select(f => f.SourcePath)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                ShowImportFailureDialog(primaryStatusText, primaryReasonMessage, failedPaths);
                return;
            }

            ImportAmriFiles(directAmriPaths);
        }

        private void StopDirectImportTracking()
        {
            var pipeline = AmariUnityPackageImportPipeline.Service;
            if (pipeline != null)
            {
                pipeline.ImportRequestFinalized -= OnDirectUnityPackageImportRequestFinalized;
            }

            _isDirectImportRunning = false;
            _pendingDirectUnityPackagePaths.Clear();
            _directImportResultsByPath.Clear();
            _pendingDirectAmriPaths = new List<string>();
        }

        private void OnImportBlmButtonClicked()
        {
            if (_avatarSettings == null)
            {
                Debug.LogWarning("[AMARI] Avatar settings are not loaded.");
                return;
            }

            if (!_isBlmAvailable)
            {
                Debug.LogWarning("[AMARI] BLM integration core is not available.");
                return;
            }

            if (IsAnyImportFlowRunning())
            {
                Debug.LogWarning("[AMARI] Another import flow is currently running.");
                return;
            }

            if (_blmIntegrationCoreBridge == null)
            {
                Debug.LogWarning("[AMARI] BLM integration bridge is not initialized.");
                return;
            }

            if (!_blmIntegrationCoreBridge.TryOpenPicker(_avatarSettings, out var errorMessage))
            {
                ShowImportFailureDialog(
                    LocalizeStatusText(AmariUnityPackagePipelineOperationStatus.Failed),
                    errorMessage,
                    Array.Empty<string>());
            }
        }

        private void EnsureBlmBridgeInitialized()
        {
            if (_isBlmBridgeInitialized)
            {
                if (_isBlmAvailable && !_isBlmEventsSubscribed)
                {
                    _isBlmAvailable = SubscribeBlmEvents();
                }

                return;
            }

            _blmIntegrationCoreBridge = new AmariBlmIntegrationCoreBridge(
                AmariUnityPackageImportPipeline.Service,
                EditorLocalization.Service);
            _isBlmBridgeInitialized = true;
            _isBlmAvailable = SubscribeBlmEvents();
        }

        private bool SubscribeBlmEvents()
        {
            if (_isBlmEventsSubscribed)
            {
                return true;
            }

            if (_blmIntegrationCoreBridge == null || !_blmIntegrationCoreBridge.IsAvailable)
            {
                return false;
            }

            _blmIntegrationCoreBridge.AmriCandidatesReady += OnBlmAmriCandidatesReady;
            _blmIntegrationCoreBridge.ImportFailed += OnBlmImportFailed;
            _isBlmEventsSubscribed = true;
            return true;
        }

        private void UnsubscribeBlmEvents()
        {
            if (_blmIntegrationCoreBridge == null)
            {
                return;
            }

            if (_isBlmEventsSubscribed)
            {
                _blmIntegrationCoreBridge.AmriCandidatesReady -= OnBlmAmriCandidatesReady;
                _blmIntegrationCoreBridge.ImportFailed -= OnBlmImportFailed;
                _isBlmEventsSubscribed = false;
            }

            _blmIntegrationCoreBridge.Dispose();
            _blmIntegrationCoreBridge = null;
            _isBlmAvailable = false;
            _isBlmBridgeInitialized = false;
        }

        private void OnBlmAmriCandidatesReady(string batchId, IReadOnlyList<AmariBlmImportAmriCandidate> candidates)
        {
            _pendingBlmAmriCandidates.Clear();
            _activeBlmBatchId = batchId ?? string.Empty;

            if (candidates != null)
            {
                foreach (var candidate in candidates.Where(candidate => candidate != null))
                {
                    var sourcePath = NormalizeFilePath(candidate.SourcePath);
                    if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                    {
                        continue;
                    }

                    _pendingBlmAmriCandidates.Add(new AmriImportCandidate
                    {
                        SourcePath = sourcePath,
                        DisplayPath = candidate.DisplayPath,
                        Status = AmriImportCandidateStatus.Warning
                    });
                }
            }

            if (_pendingBlmAmriCandidates.Count == 0)
            {
                ClearBlmFlowState();
                return;
            }

            ContinueBlmAmriFlowAfterSuccessfulBatch();
        }

        private void OnBlmImportFailed(AmariBlmImportFailureContext failure)
        {
            if (failure == null)
            {
                return;
            }

            var primarySourcePath = failure.FailedSourcePaths?
                .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path)) ?? string.Empty;
            var directFailure = new DirectImportFailure
            {
                SourcePath = primarySourcePath,
                Status = failure.ImportStatus,
                CancellationReason = failure.CancellationReason,
                FailureReason = failure.FailureReason,
                ErrorMessage = failure.ErrorMessage ?? string.Empty
            };

            ShowImportFailureDialog(
                LocalizeStatusText(failure.ImportStatus),
                BuildLocalizedDirectImportReasonMessage(directFailure),
                failure.FailedSourcePaths);
            ClearBlmFlowState();
        }

        private void ContinueBlmAmriFlowAfterSuccessfulBatch()
        {
            if (_pendingBlmAmriCandidates.Count == 0)
            {
                ClearBlmFlowState();
                return;
            }

            foreach (var candidate in _pendingBlmAmriCandidates)
            {
                candidate.Status = EvaluateAmriCandidateStatus(candidate.SourcePath);
            }

            OpenBlmAmriSelectionModal(_activeBlmBatchId, _pendingBlmAmriCandidates);
        }

        private void ClearBlmFlowState()
        {
            _isBlmModalOpen = false;
            _activeBlmBatchId = string.Empty;
            _pendingBlmAmriCandidates.Clear();
        }

        private void OpenBlmAmriSelectionModal(string batchId, IReadOnlyList<AmriImportCandidate> candidates)
        {
            if (_isBlmModalOpen || candidates == null || candidates.Count == 0)
            {
                return;
            }

            var modalItems = candidates
                .Where(candidate => candidate != null)
                .Select(candidate => new AmariBlmAmriSelectionWindow.AmriModalItem
                {
                    SourcePath = candidate.SourcePath,
                    DisplayPath = candidate.DisplayPath,
                    Status = candidate.Status switch
                    {
                        AmriImportCandidateStatus.Info => AmariBlmAmriSelectionWindow.AmriModalItemStatus.Info,
                        AmriImportCandidateStatus.Warning => AmariBlmAmriSelectionWindow.AmriModalItemStatus.Warning,
                        _ => AmariBlmAmriSelectionWindow.AmriModalItemStatus.Critical
                    }
                })
                .ToList();

            _isBlmModalOpen = true;
            AmariBlmAmriSelectionWindow.Open(
                batchId,
                modalItems,
                Localize,
                (shouldImportSelected, selectedPaths) =>
                {
                    _isBlmModalOpen = false;
                    if (shouldImportSelected && selectedPaths != null && selectedPaths.Count > 0)
                    {
                        ImportAmriFiles(selectedPaths);
                    }

                    ClearBlmFlowState();
                });
        }

        private void ImportAmriFiles(IEnumerable<string> amriPaths)
        {
            if (_avatarSettings?.ItemListGroupItems == null || amriPaths == null)
            {
                return;
            }

            var root = rootVisualElement;
            if (root == null)
            {
                return;
            }

            var tabScrollView = root.Q<ScrollView>("ItemGroupTabListView");
            if (tabScrollView == null)
            {
                Debug.LogWarning("[AMARI] ItemGroupTabListView not found.");
                return;
            }

            var uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in amriPaths)
            {
                var normalizedPath = NormalizeFilePath(path);
                if (string.IsNullOrWhiteSpace(normalizedPath) || !uniquePaths.Add(normalizedPath))
                {
                    continue;
                }

                if (!File.Exists(normalizedPath))
                {
                    Debug.LogWarning($"[AMARI] .amri file not found: {normalizedPath}");
                    continue;
                }

                try
                {
                    var json = File.ReadAllText(normalizedPath, Encoding.UTF8);
                    if (!TryParseImportedItemGroupJson(json, out var imported, out var parseError))
                    {
                        Debug.LogError($"[AMARI] Failed to import item group ({normalizedPath}): {parseError}");
                        continue;
                    }

                    ImportItemGroup(imported, tabScrollView, root);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[AMARI] Failed to import item group ({normalizedPath}): {ex.Message}");
                }
            }
        }

        private AmriImportCandidateStatus EvaluateAmriCandidateStatus(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                return AmriImportCandidateStatus.Critical;
            }

            try
            {
                var json = File.ReadAllText(sourcePath, Encoding.UTF8);
                if (!TryParseImportedItemGroupJson(json, out var imported, out _))
                {
                    return AmriImportCandidateStatus.Critical;
                }

                var items = imported?.items;
                if (items == null || items.Count == 0)
                {
                    return AmriImportCandidateStatus.Warning;
                }

                var totalGuidCount = 0;
                var resolvedGuidCount = 0;
                foreach (var importedItem in items)
                {
                    var prefabGuid = importedItem?.prefabGuid?.Trim();
                    if (string.IsNullOrWhiteSpace(prefabGuid))
                    {
                        continue;
                    }

                    totalGuidCount++;
                    var assetPath = AssetDatabase.GUIDToAssetPath(prefabGuid);
                    if (!string.IsNullOrWhiteSpace(assetPath))
                    {
                        resolvedGuidCount++;
                    }
                }

                if (totalGuidCount == 0)
                {
                    return AmriImportCandidateStatus.Warning;
                }

                if (resolvedGuidCount == totalGuidCount)
                {
                    return AmriImportCandidateStatus.Info;
                }

                return resolvedGuidCount == 0
                    ? AmriImportCandidateStatus.Critical
                    : AmriImportCandidateStatus.Warning;
            }
            catch
            {
                return AmriImportCandidateStatus.Critical;
            }
        }

        private void ShowImportFailureDialog(string status, string errorMessage, IReadOnlyList<string> failedPaths)
        {
            var statusText = string.IsNullOrWhiteSpace(status)
                ? L("amari.window.avatarCustomize.import.failure.statusUnknown", "Unknown")
                : status;
            var builder = new StringBuilder();
            builder.AppendLine(L("amari.window.avatarCustomize.import.failure.summary", "One or more imports failed."));
            builder.AppendLine($"{L("amari.window.avatarCustomize.import.failure.status", "Status")}: {statusText}");

            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                builder.AppendLine($"{L("amari.window.avatarCustomize.import.failure.error", "Error")}: {errorMessage}");
            }

            if (failedPaths != null && failedPaths.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine($"{L("amari.window.avatarCustomize.import.failure.failedFiles", "Failed file paths")}:");
                foreach (var path in failedPaths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    builder.AppendLine(path);
                }
            }

            EditorUtility.DisplayDialog(
                L("amari.window.avatarCustomize.import.failure.title", "Import Failed"),
                builder.ToString().TrimEnd(),
                "OK");
        }

        private string LocalizeStatusText(AmariUnityPackagePipelineOperationStatus status)
        {
            return status switch
            {
                AmariUnityPackagePipelineOperationStatus.Cancelled =>
                    L("amari.window.avatarCustomize.import.failure.statusCancelled", "Cancelled"),
                AmariUnityPackagePipelineOperationStatus.Failed =>
                    L("amari.window.avatarCustomize.import.failure.statusFailed", "Failed"),
                _ => L("amari.window.avatarCustomize.import.failure.statusUnknown", "Unknown")
            };
        }

        private string BuildLocalizedDirectImportReasonMessage(DirectImportFailure failure)
        {
            if (failure == null)
            {
                return string.Empty;
            }

            if (failure.Status == AmariUnityPackagePipelineOperationStatus.Cancelled)
            {
                switch (failure.CancellationReason)
                {
                    case AmariUnityPackageImportCancellationReason.WindowClosedFallback:
                        return L(
                            "amari.window.avatarCustomize.import.failure.cancelled.byWindowClose",
                            "Package import was cancelled by closing the import window.");

                    case AmariUnityPackageImportCancellationReason.HangTimeoutAfterImportConfirm:
                        return L(
                            "amari.window.avatarCustomize.import.failure.cancelled.byHangTimeout",
                            "Package import was cancelled because Unity stopped responding after the import was confirmed.");

                    case AmariUnityPackageImportCancellationReason.PipelineReset:
                        return L(
                            "amari.window.avatarCustomize.import.failure.cancelled.byPipelineReset",
                            "Import was cancelled because the import pipeline was reset.");

                    case AmariUnityPackageImportCancellationReason.StaleRecovery:
                        return L(
                            "amari.window.avatarCustomize.import.failure.cancelled.byStaleRecovery",
                            "Import was cancelled while recovering inconsistent pipeline state.");

                    case AmariUnityPackageImportCancellationReason.UnityCancelledEvent:
                    case AmariUnityPackageImportCancellationReason.None:
                    default:
                        return string.IsNullOrWhiteSpace(failure.ErrorMessage)
                            ? L(
                                "amari.window.avatarCustomize.import.failure.cancelled.message",
                                "Import cancelled.")
                            : failure.ErrorMessage;
                }
            }

            if (failure.Status == AmariUnityPackagePipelineOperationStatus.Failed)
            {
                switch (failure.FailureReason)
                {
                    case AmariUnityPackageImportFailureReason.PackageImportWindowTypesUnresolved:
                        return L(
                            "amari.window.avatarCustomize.import.failure.failed.byWindowTypesUnresolved",
                            "Failed to start interactive package import (package import window could not be resolved).");

                    case AmariUnityPackageImportFailureReason.None:
                    default:
                        return failure.ErrorMessage ?? string.Empty;
                }
            }

            return failure.ErrorMessage ?? string.Empty;
        }

        private static IReadOnlyList<string> OpenImportFileDialog()
        {
#if UNITY_EDITOR_WIN
            var dialogType = Type.GetType("System.Windows.Forms.OpenFileDialog, System.Windows.Forms");
            var dialogResultType = Type.GetType("System.Windows.Forms.DialogResult, System.Windows.Forms");
            if (dialogType == null || dialogResultType == null)
            {
                Debug.LogError("[AMARI] System.Windows.Forms.OpenFileDialog is unavailable.");
                return Array.Empty<string>();
            }

            object dialog = null;
            try
            {
                dialog = Activator.CreateInstance(dialogType);
                SetObjectProperty(dialogType, dialog, "Title", L("amari.window.avatarCustomize.importUnityPackageButton", "Import unitypackage"));
                SetObjectProperty(dialogType, dialog, "Filter", "UnityPackage / AMRI (*.unitypackage;*.amri)|*.unitypackage;*.amri|UnityPackage (*.unitypackage)|*.unitypackage|AMRI (*.amri)|*.amri");
                SetObjectProperty(dialogType, dialog, "Multiselect", true);
                SetObjectProperty(dialogType, dialog, "CheckFileExists", true);
                SetObjectProperty(dialogType, dialog, "RestoreDirectory", true);

                var result = dialogType.GetMethod("ShowDialog", Type.EmptyTypes)?.Invoke(dialog, null);
                var okValue = Enum.Parse(dialogResultType, "OK");
                if (!Equals(result, okValue))
                {
                    return Array.Empty<string>();
                }

                var fileNames = dialogType.GetProperty("FileNames", BindingFlags.Public | BindingFlags.Instance)
                    ?.GetValue(dialog) as string[];
                return fileNames?
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .ToArray() ?? Array.Empty<string>();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AMARI] Failed to open import file dialog: {ex.Message}");
                return Array.Empty<string>();
            }
            finally
            {
                if (dialog is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
#else
            Debug.LogWarning("[AMARI] Multi-file import dialog is supported only on Windows Editor.");
            return Array.Empty<string>();
#endif
        }

        private static void SetObjectProperty(Type type, object instance, string propertyName, object value)
        {
            var property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (property == null || !property.CanWrite)
            {
                return;
            }

            property.SetValue(instance, value);
        }

        private static void ResetUnityPackagePipelineIfBusy(IAmariUnityPackageImportPipelineService pipeline)
        {
            if (pipeline == null)
            {
                return;
            }

            if (!pipeline.IsImporting && pipeline.RemainingCount <= 0)
            {
                return;
            }

            pipeline.ResetPipelineAndClearQueue();
        }

        private static string NormalizeFilePath(string path)
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
                return path.Replace('\\', '/').Trim();
            }
        }
    }
}
