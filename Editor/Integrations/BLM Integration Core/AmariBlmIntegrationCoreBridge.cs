using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using com.amari_noa.unity_editor_localization_core.editor;
using com.amari_noa.unitypackage_pipeline_core.editor;
using UnityEngine;

#if AMARI_BLM_INTEGRATION_CORE_INSTALLED
using com.amari_noa.blm_integration_core.editor;
#endif

namespace com.amari_noa.avatar_modular_assistant.editor.integrations.blm_integration_core
{
    public sealed class AmariBlmImportAmriCandidate
    {
        public string SourcePath = string.Empty;
        public string DisplayPath = string.Empty;
    }

    public sealed class AmariBlmImportFailureContext
    {
        public AmariUnityPackagePipelineOperationStatus ImportStatus = AmariUnityPackagePipelineOperationStatus.Failed;
        public AmariUnityPackageImportCancellationReason CancellationReason = AmariUnityPackageImportCancellationReason.None;
        public AmariUnityPackageImportFailureReason FailureReason = AmariUnityPackageImportFailureReason.None;
        public string ErrorMessage = string.Empty;
        public IReadOnlyList<string> FailedSourcePaths = Array.Empty<string>();
    }

    public sealed class AmariBlmIntegrationCoreBridge : IDisposable
    {
        private const string UnityPackageExtension = ".unitypackage";
        private const string AmriExtension = ".amri";
        private const string UnknownShopName = "UnknownShop";
        private const string UnknownProductName = "UnknownProduct";
        private const string BlmLocalizationSourceId = "com.amari-noa.blm-integration-core";

        private readonly IAmariUnityPackageImportPipelineService _pipelineService;
        private readonly IEditorLocalizationService _localizationService;

#if AMARI_BLM_INTEGRATION_CORE_INSTALLED
        private bool _isSubscribed;
        private bool _isImportRunning;
        private string _activeBatchId = string.Empty;
        private object _activeHostContext;
        private BlmPickerContext _activePickerContext;
        private readonly List<AmariBlmImportAmriCandidate> _pendingAmriCandidates = new();
#endif

        public bool IsAvailable { get; }

        public bool IsImportRunning
        {
#if AMARI_BLM_INTEGRATION_CORE_INSTALLED
            get { return _isImportRunning; }
#else
            get { return false; }
#endif
        }

        public event Action<string, IReadOnlyList<AmariBlmImportAmriCandidate>> AmriCandidatesReady;
        public event Action<AmariBlmImportFailureContext> ImportFailed;

        public AmariBlmIntegrationCoreBridge(
            IAmariUnityPackageImportPipelineService pipelineService,
            IEditorLocalizationService localizationService)
        {
            _pipelineService = pipelineService;
            _localizationService = localizationService;
            IsAvailable = SubscribeEvents();
        }

        public void Dispose()
        {
            UnsubscribeEvents();
            ClearFlowState();
        }

        public bool TryOpenPicker(object hostContext, out string errorMessage)
        {
#if !AMARI_BLM_INTEGRATION_CORE_INSTALLED
            errorMessage = "BLM integration core is not available.";
            return false;
#else
            if (!IsAvailable)
            {
                errorMessage = "BLM integration core is not available.";
                return false;
            }

            var pickerContext = BuildPickerContext(hostContext, out errorMessage);
            if (pickerContext == null)
            {
                return false;
            }

            _activeHostContext = hostContext;
            _activePickerContext = pickerContext;

            try
            {
                BlmCatalogWindowGateway.Shared.Open(pickerContext);
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
#endif
        }

#if AMARI_BLM_INTEGRATION_CORE_INSTALLED
        private bool SubscribeEvents()
        {
            if (_isSubscribed)
            {
                return true;
            }

            try
            {
                BlmCatalogWindowGateway.Shared.BatchRequestConfirmed += OnBatchRequestConfirmed;
                BlmImportProcessor.Shared.ImportBatchCompleted += OnImportBatchCompleted;
                _isSubscribed = true;
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AMARI] Failed to subscribe BLM events: {ex.Message}");
                _isSubscribed = false;
                return false;
            }
        }

        private void UnsubscribeEvents()
        {
            if (!_isSubscribed)
            {
                return;
            }

            try
            {
                BlmCatalogWindowGateway.Shared.BatchRequestConfirmed -= OnBatchRequestConfirmed;
                BlmImportProcessor.Shared.ImportBatchCompleted -= OnImportBatchCompleted;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AMARI] Failed to unsubscribe BLM events: {ex.Message}");
            }
            finally
            {
                _isSubscribed = false;
            }
        }

        private BlmPickerContext BuildPickerContext(object hostContext, out string errorMessage)
        {
            var pickerContext = new BlmPickerContext
            {
                InvocationContext = BlmInvocationContext.Integration,
                PreferredDisplayExtensions = new List<string> { UnityPackageExtension, AmriExtension },
                UnityPackageImportPipelineService = _pipelineService,
                DestinationAssetPathUpdater = BlmImportProcessor.Shared,
                EditorLocalizationService = _localizationService,
                LocalizationSourceId = BlmLocalizationSourceId,
                HostContext = hostContext
            };

            if (pickerContext.ValidateRequiredServices(out errorMessage))
            {
                return pickerContext;
            }

            return null;
        }

        private void OnBatchRequestConfirmed(BlmImportBatchRequest request)
        {
            if (request == null)
            {
                return;
            }

            if (_isImportRunning)
            {
                Debug.LogWarning("[AMARI] Another BLM import is currently running. Request was ignored.");
                return;
            }

            _pendingAmriCandidates.Clear();
            _activeBatchId = request.BatchId ?? string.Empty;

            var items = request.Items;
            if (items == null)
            {
                RaiseImportFailed(
                    AmariUnityPackagePipelineOperationStatus.Failed,
                    AmariUnityPackageImportCancellationReason.None,
                    AmariUnityPackageImportFailureReason.None,
                    "BLM batch request items were null.",
                    Array.Empty<string>());
                ClearFlowState();
                return;
            }

            for (var i = items.Count - 1; i >= 0; i--)
            {
                var item = items[i];
                if (item == null)
                {
                    items.RemoveAt(i);
                    continue;
                }

                var ext = (Path.GetExtension(item.SourcePath) ?? string.Empty).ToLowerInvariant();
                if (!string.Equals(ext, AmriExtension, StringComparison.Ordinal))
                {
                    continue;
                }

                var candidate = BuildAmriCandidate(item);
                if (candidate != null)
                {
                    _pendingAmriCandidates.Add(candidate);
                }

                items.RemoveAt(i);
            }

            NormalizeAndSortCandidates(_pendingAmriCandidates);

            if (items.Count == 0)
            {
                RaiseAmriCandidatesReady();
                return;
            }

            ResetPipelineIfBusy(_pipelineService);

            var pickerContext = _activePickerContext;
            if (pickerContext == null)
            {
                pickerContext = BuildPickerContext(_activeHostContext, out _);
            }

            if (pickerContext == null)
            {
                RaiseImportFailed(
                    AmariUnityPackagePipelineOperationStatus.Failed,
                    AmariUnityPackageImportCancellationReason.None,
                    AmariUnityPackageImportFailureReason.None,
                    "BLM picker context is not available.",
                    Array.Empty<string>());
                ClearFlowState();
                return;
            }

            try
            {
                _isImportRunning = true;
                BlmImportProcessor.Shared.Execute(request, pickerContext);
            }
            catch (Exception ex)
            {
                _isImportRunning = false;
                RaiseImportFailed(
                    AmariUnityPackagePipelineOperationStatus.Failed,
                    AmariUnityPackageImportCancellationReason.None,
                    AmariUnityPackageImportFailureReason.None,
                    ex.Message,
                    Array.Empty<string>());
                ClearFlowState();
            }
        }

        private void OnImportBatchCompleted(BlmImportBatchResultContext result)
        {
            if (!_isImportRunning || result == null)
            {
                return;
            }

            var batchId = result.BatchId ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(_activeBatchId) &&
                !string.Equals(batchId, _activeBatchId, StringComparison.Ordinal))
            {
                return;
            }

            _isImportRunning = false;

            if (result.ImportStatus == AmariUnityPackagePipelineOperationStatus.Completed)
            {
                RaiseAmriCandidatesReady();
                return;
            }

            RaiseImportFailed(
                result.ImportStatus,
                result.CancellationReason,
                result.FailureReason,
                result.ErrorMessage ?? string.Empty,
                ExtractFailedSourcePaths(result.FailedItems));
            ClearFlowState();
        }

        private void RaiseAmriCandidatesReady()
        {
            if (_pendingAmriCandidates.Count == 0)
            {
                ClearFlowState();
                return;
            }

            var batchId = _activeBatchId;
            var snapshot = CloneCandidates(_pendingAmriCandidates);

            try
            {
                AmriCandidatesReady?.Invoke(batchId, snapshot);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AMARI] AmriCandidatesReady callback failed: {ex.Message}");
            }
            finally
            {
                ClearFlowState();
            }
        }

        private void RaiseImportFailed(
            AmariUnityPackagePipelineOperationStatus importStatus,
            AmariUnityPackageImportCancellationReason cancellationReason,
            AmariUnityPackageImportFailureReason failureReason,
            string errorMessage,
            IReadOnlyList<string> failedSourcePaths)
        {
            var failure = new AmariBlmImportFailureContext
            {
                ImportStatus = importStatus,
                CancellationReason = cancellationReason,
                FailureReason = failureReason,
                ErrorMessage = errorMessage ?? string.Empty,
                FailedSourcePaths = failedSourcePaths ?? Array.Empty<string>()
            };

            try
            {
                ImportFailed?.Invoke(failure);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AMARI] ImportFailed callback failed: {ex.Message}");
            }
        }

        private void ClearFlowState()
        {
            _isImportRunning = false;
            _activeBatchId = string.Empty;
            _activePickerContext = null;
            _activeHostContext = null;
            _pendingAmriCandidates.Clear();
        }

        private static void ResetPipelineIfBusy(IAmariUnityPackageImportPipelineService pipelineService)
        {
            if (pipelineService == null)
            {
                return;
            }

            if (!pipelineService.IsImporting && pipelineService.RemainingCount <= 0)
            {
                return;
            }

            pipelineService.ResetPipelineAndClearQueue();
        }

        private static AmariBlmImportAmriCandidate BuildAmriCandidate(BlmImportRequestItem item)
        {
            var sourcePath = NormalizeFilePath(item?.SourcePath);
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return null;
            }

            var shopName = item?.ShopName ?? string.Empty;
            var productName = item?.ProductName ?? string.Empty;
            var normalizedRelativePath = NormalizeRelativePath(item?.NormalizedRelativePath);
            if (string.IsNullOrWhiteSpace(normalizedRelativePath))
            {
                var rootFolderPath = item?.RootFolderPath ?? string.Empty;
                normalizedRelativePath = BuildFallbackNormalizedRelativePath(rootFolderPath, sourcePath);
            }

            if (string.IsNullOrWhiteSpace(shopName))
            {
                shopName = UnknownShopName;
                Debug.LogWarning($"[AMARI] ShopName was empty. Using fallback '{UnknownShopName}'. sourcePath={sourcePath}");
            }

            if (string.IsNullOrWhiteSpace(productName))
            {
                productName = UnknownProductName;
                Debug.LogWarning($"[AMARI] ProductName was empty. Using fallback '{UnknownProductName}'. sourcePath={sourcePath}");
            }

            return new AmariBlmImportAmriCandidate
            {
                SourcePath = sourcePath,
                DisplayPath = BuildDisplayPath(shopName, productName, normalizedRelativePath)
            };
        }

        private static void NormalizeAndSortCandidates(List<AmariBlmImportAmriCandidate> candidates)
        {
            if (candidates == null)
            {
                return;
            }

            var unique = new Dictionary<string, AmariBlmImportAmriCandidate>(StringComparer.OrdinalIgnoreCase);
            foreach (var candidate in candidates.Where(candidate => candidate != null))
            {
                var sourcePath = NormalizeFilePath(candidate.SourcePath);
                if (string.IsNullOrWhiteSpace(sourcePath))
                {
                    continue;
                }

                if (!File.Exists(sourcePath))
                {
                    Debug.LogWarning($"[AMARI] .amri candidate was removed because file does not exist: {sourcePath}");
                    continue;
                }

                candidate.SourcePath = sourcePath;
                candidate.DisplayPath = string.IsNullOrWhiteSpace(candidate.DisplayPath)
                    ? (Path.GetFileName(sourcePath) ?? sourcePath)
                    : candidate.DisplayPath;

                if (!unique.ContainsKey(sourcePath))
                {
                    unique.Add(sourcePath, candidate);
                }
            }

            candidates.Clear();
            candidates.AddRange(unique.Values
                .OrderBy(candidate => candidate.DisplayPath ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(candidate => candidate.SourcePath ?? string.Empty, StringComparer.OrdinalIgnoreCase));
        }

        private static IReadOnlyList<AmariBlmImportAmriCandidate> CloneCandidates(IEnumerable<AmariBlmImportAmriCandidate> candidates)
        {
            return candidates?
                .Where(candidate => candidate != null)
                .Select(candidate => new AmariBlmImportAmriCandidate
                {
                    SourcePath = candidate.SourcePath ?? string.Empty,
                    DisplayPath = candidate.DisplayPath ?? string.Empty
                })
                .ToArray() ?? Array.Empty<AmariBlmImportAmriCandidate>();
        }

        private static string BuildDisplayPath(string shopName, string productName, string normalizedRelativePath)
        {
            var safeShopName = string.IsNullOrWhiteSpace(shopName) ? UnknownShopName : shopName.Trim();
            var safeProductName = string.IsNullOrWhiteSpace(productName) ? UnknownProductName : productName.Trim();
            var safeRelativePath = string.IsNullOrWhiteSpace(normalizedRelativePath)
                ? string.Empty
                : NormalizeRelativePath(normalizedRelativePath);

            if (string.IsNullOrWhiteSpace(safeRelativePath))
            {
                safeRelativePath = "unknown.amri";
            }
            else if (!safeRelativePath.EndsWith(AmriExtension, StringComparison.OrdinalIgnoreCase))
            {
                safeRelativePath += AmriExtension;
            }

            return $"{safeShopName}/{safeProductName}/{safeRelativePath}";
        }

        private static IReadOnlyList<string> ExtractFailedSourcePaths(IEnumerable<BlmImportRequestItem> failedItems)
        {
            if (failedItems == null)
            {
                return Array.Empty<string>();
            }

            return failedItems
                .Where(item => item != null)
                .Select(item => NormalizeFilePath(item.SourcePath))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
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

        private static string NormalizeRelativePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            return path.Replace('\\', '/').TrimStart('/');
        }

        private static string BuildFallbackNormalizedRelativePath(string rootFolderPath, string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(rootFolderPath))
            {
                return Path.GetFileName(sourcePath) ?? sourcePath;
            }

            try
            {
                var relativePath = Path.GetRelativePath(rootFolderPath, sourcePath);
                if (!string.IsNullOrWhiteSpace(relativePath) &&
                    !relativePath.StartsWith("..", StringComparison.Ordinal))
                {
                    return NormalizeRelativePath(relativePath);
                }
            }
            catch
            {
                // Ignore and use fallback below.
            }

            return Path.GetFileName(sourcePath) ?? sourcePath;
        }
#else
        private static bool SubscribeEvents()
        {
            return false;
        }

        private static void UnsubscribeEvents()
        {
        }

        private static void ClearFlowState()
        {
        }
#endif
    }
}
