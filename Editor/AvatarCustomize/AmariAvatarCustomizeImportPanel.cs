using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        private Button _importUnityPackageButton;
        private Button _importBlmButton;
        private VisualElement _importBlmButtonRoot;

        private bool _isDirectImportRunning;
        private readonly HashSet<string> _pendingDirectUnityPackagePaths = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, AmariUnityPackageImportResultContext> _directImportResultsByPath = new(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _preDirectUnityPackageAmriSnapshot;
        private int _importSuccessCount;

        private bool _isBlmBridgeInitialized;
        private bool _isBlmAvailable;
        private bool _isBlmEventsSubscribed;
        private bool _isBlmImportQueueStarting;
        private string _activeBlmBatchId = string.Empty;
        private AmariBlmIntegrationCoreBridge _blmIntegrationCoreBridge;
        private HashSet<string> _preBlmAmriSnapshot;

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
            return _isDirectImportRunning
                   || IsBlmImportQueueRunning()
                   || (_blmIntegrationCoreBridge?.IsCatalogWindowOpen ?? false)
                   || _amriApplyModalOpenCount > 0;
        }

        private bool IsBlmImportQueueRunning()
        {
            return _isBlmImportQueueStarting || !string.IsNullOrEmpty(_activeBlmBatchId);
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
            AbortInProgressImportFlows();
            UnsubscribeBlmEvents();
        }

        private void AbortInProgressImportFlows()
        {
            var shouldResetPipeline = _isDirectImportRunning || IsBlmImportQueueRunning();
            StopDirectImportTracking();
            ClearBlmFlowState();

            if (!shouldResetPipeline)
            {
                UpdateImportInProgressOverlayVisibility();
                return;
            }

            var pipeline = AmariUnityPackageImportPipeline.Service;
            if (pipeline != null && (pipeline.RemainingCount > 0 || pipeline.IsImporting))
            {
                pipeline.ResetPipelineAndClearQueue();
            }

            UpdateImportInProgressOverlayVisibility();
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
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var selectedPath in selectedPaths)
            {
                var normalizedPath = NormalizeFilePath(selectedPath);
                if (string.IsNullOrWhiteSpace(normalizedPath) || !seen.Add(normalizedPath))
                {
                    continue;
                }

                var ext = Path.GetExtension(normalizedPath).ToLowerInvariant();
                if (!string.Equals(ext, UnityPackageExtension, StringComparison.Ordinal))
                {
                    Debug.LogWarning($"[AMARI] Unsupported extension skipped: {selectedPath}");
                    continue;
                }

                if (!File.Exists(normalizedPath))
                {
                    Debug.LogWarning($"[AMARI] UnityPackage file not found: {normalizedPath}");
                    continue;
                }

                unityPackagePaths.Add(normalizedPath);
            }

            if (unityPackagePaths.Count == 0)
            {
                return;
            }

            BeginDirectUnityPackageImport(unityPackagePaths);
        }

        private void BeginDirectUnityPackageImport(IReadOnlyList<string> unityPackagePaths)
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
                return;
            }

            _preDirectUnityPackageAmriSnapshot = CaptureCurrentAmriAssetPathSnapshot();
            _importSuccessCount = 0;
            _isDirectImportRunning = true;
            UpdateImportInProgressOverlayVisibility();
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

            StopDirectImportTracking();

            if (failures.Count > 0)
            {
                _preDirectUnityPackageAmriSnapshot = null;
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

            var snapshot = _preDirectUnityPackageAmriSnapshot;
            _preDirectUnityPackageAmriSnapshot = null;

            ShowImportSuccessDialog(_importSuccessCount);
            _importSuccessCount = 0;

            if (snapshot == null)
            {
                return;
            }

            var rootForUnityPackage = rootVisualElement;
            var tabScrollViewForUnityPackage = rootForUnityPackage?.Q<ScrollView>("ItemGroupTabListView");
            if (tabScrollViewForUnityPackage != null)
            {
                ProcessUnityPackageImportedAmri(snapshot, tabScrollViewForUnityPackage, rootForUnityPackage);
            }
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
            _preDirectUnityPackageAmriSnapshot = null;
            UpdateImportInProgressOverlayVisibility();
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

            if (_blmIntegrationCoreBridge == null)
            {
                Debug.LogWarning("[AMARI] BLM integration bridge is not initialized.");
                return;
            }

            _preBlmAmriSnapshot = CaptureCurrentAmriAssetPathSnapshot();
            var hostDisplayName = Localize("amari.package.displayName", "Avatar Modular Assistant for Refinement & Integration");
            if (!_blmIntegrationCoreBridge.TryOpenPicker(_avatarSettings, hostDisplayName, out var errorMessage))
            {
                _preBlmAmriSnapshot = null;
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

            _blmIntegrationCoreBridge.CatalogWindowOpened += OnBlmCatalogWindowOpened;
            _blmIntegrationCoreBridge.CatalogWindowClosed += OnBlmCatalogWindowClosed;
            _blmIntegrationCoreBridge.BatchRequestReceived += OnBlmBatchRequestReceived;
            _blmIntegrationCoreBridge.BatchExecutionStarting += OnBlmBatchExecutionStarting;
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
                _blmIntegrationCoreBridge.CatalogWindowOpened -= OnBlmCatalogWindowOpened;
                _blmIntegrationCoreBridge.CatalogWindowClosed -= OnBlmCatalogWindowClosed;
                _blmIntegrationCoreBridge.BatchRequestReceived -= OnBlmBatchRequestReceived;
                _blmIntegrationCoreBridge.BatchExecutionStarting -= OnBlmBatchExecutionStarting;
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
            _ = batchId;

            var root = rootVisualElement;
            var tabScrollView = root?.Q<ScrollView>("ItemGroupTabListView");

            ShowImportSuccessDialog(_importSuccessCount);
            _importSuccessCount = 0;

            var snapshot = _preBlmAmriSnapshot;
            _preBlmAmriSnapshot = null;
            if (snapshot != null && tabScrollView != null)
            {
                ProcessUnityPackageImportedAmri(snapshot, tabScrollView, root);
            }

            if (candidates != null && tabScrollView != null)
            {
                var amriPaths = candidates
                    .Where(candidate => candidate != null && !string.IsNullOrWhiteSpace(candidate.SourcePath))
                    .Select(candidate => NormalizeFilePath(candidate.SourcePath))
                    .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (amriPaths.Count > 0)
                {
                    ImportSingleAmriFiles(amriPaths, tabScrollView, root);
                }
            }

            ClearBlmFlowState();
            UpdateImportInProgressOverlayVisibility();
        }

        private void OnBlmImportFailed(AmariBlmImportFailureContext failure)
        {
            _preBlmAmriSnapshot = null;
            if (failure == null)
            {
                return;
            }

            if (failure.ImportStatus == AmariUnityPackagePipelineOperationStatus.Cancelled &&
                failure.CancellationReason == AmariUnityPackageImportCancellationReason.WindowClosedFallback)
            {
                // OnPipelineImportRequestFinalizedForOverlay で既にキャンセル処理済み
                ClearBlmFlowState();
                UpdateImportInProgressOverlayVisibility();
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

            ClearBlmFlowState();
            ShowImportFailureDialog(
                LocalizeStatusText(failure.ImportStatus),
                BuildLocalizedDirectImportReasonMessage(directFailure),
                failure.FailedSourcePaths);
            UpdateImportInProgressOverlayVisibility();
        }

        private void ClearBlmFlowState()
        {
            _preBlmAmriSnapshot = null;
            _isBlmImportQueueStarting = false;
            _activeBlmBatchId = string.Empty;
        }

        private void OnBlmCatalogWindowOpened()
        {
            UpdateImportInProgressOverlayVisibility();
        }

        private void OnBlmCatalogWindowClosed()
        {
            UpdateImportInProgressOverlayVisibility();
        }

        private void OnBlmBatchRequestReceived(string batchId)
        {
            _ = batchId;
            _isBlmImportQueueStarting = true;
            UpdateImportInProgressOverlayVisibility();
        }

        private void OnBlmBatchExecutionStarting(string batchId)
        {
            _isBlmImportQueueStarting = false;
            _activeBlmBatchId = batchId ?? string.Empty;
            _importSuccessCount = 0;
            UpdateImportInProgressOverlayVisibility();
        }

        private static void ShowImportSuccessDialog(int count)
        {
            if (count <= 0)
            {
                return;
            }

            var title = L("amari.window.avatarCustomize.import.success.title", "Import Complete");
            var message = string.Format(
                L("amari.window.avatarCustomize.import.success.message", "{0} file(s) were imported successfully."),
                count);
            EditorUtility.DisplayDialog(title, message, "OK");
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
            var title = L("amari.window.avatarCustomize.importUnityPackageButton", "Import unitypackage");
            var filters = new[]
            {
                "UnityPackage / AMRI", "unitypackage,amri",
                "UnityPackage", "unitypackage",
                "AMRI", "amri"
            };

            var selectedPath = EditorUtility.OpenFilePanelWithFilters(title, Application.dataPath, filters);
            if (string.IsNullOrWhiteSpace(selectedPath))
            {
                return Array.Empty<string>();
            }

            return new[] { selectedPath };
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
