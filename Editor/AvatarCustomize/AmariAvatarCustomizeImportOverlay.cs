using com.amari_noa.unitypackage_pipeline_core.editor;
using UnityEngine.UIElements;

// ReSharper disable once CheckNamespace
namespace com.amari_noa.avatar_modular_assistant.editor
{
    public partial class AmariAvatarCustomizeWindow
    {
        private VisualElement _importInProgressOverlay;
        private Label _importInProgressLabel;
        private int _amriApplyModalOpenCount;

        private void SetupImportInProgressOverlay(VisualElement root)
        {
            if (root == null)
            {
                return;
            }

            _amriApplyModalOpenCount = 0;

            _importInProgressOverlay = root.Q<VisualElement>("ImportInProgressOverlay");
            _importInProgressLabel = root.Q<Label>("ImportInProgressLabel");
            RefreshImportInProgressOverlayLocalizedTexts();
            SubscribePipelineEventsForOverlay();
            UpdateImportInProgressOverlayVisibility();
        }

        private void SubscribePipelineEventsForOverlay()
        {
            var pipeline = AmariUnityPackageImportPipeline.Service;
            if (pipeline == null)
            {
                return;
            }

            pipeline.QueueChanged -= OnPipelineQueueChangedForOverlay;
            pipeline.QueueChanged += OnPipelineQueueChangedForOverlay;
            pipeline.ImportRequestFinalized -= OnPipelineImportRequestFinalizedForOverlay;
            pipeline.ImportRequestFinalized += OnPipelineImportRequestFinalizedForOverlay;
        }

        internal void UnsubscribePipelineEventsForOverlay()
        {
            var pipeline = AmariUnityPackageImportPipeline.Service;
            if (pipeline == null)
            {
                return;
            }

            pipeline.QueueChanged -= OnPipelineQueueChangedForOverlay;
            pipeline.ImportRequestFinalized -= OnPipelineImportRequestFinalizedForOverlay;
        }

        private void OnPipelineQueueChangedForOverlay()
        {
            UpdateImportInProgressOverlayVisibility();
        }

        private void OnPipelineImportRequestFinalizedForOverlay(AmariUnityPackageImportResultContext result)
        {
            if (result != null &&
                result.ImportStatus == AmariUnityPackagePipelineOperationStatus.Cancelled &&
                result.CancellationReason == AmariUnityPackageImportCancellationReason.WindowClosedFallback)
            {
                HandleInteractiveImportWindowCancelled(result);
                return;
            }

            if (result != null &&
                result.ImportStatus == AmariUnityPackagePipelineOperationStatus.Completed)
            {
                _importSuccessCount++;
            }

            UpdateImportInProgressOverlayVisibility();
        }

        private void HandleInteractiveImportWindowCancelled(AmariUnityPackageImportResultContext result)
        {
            var sourcePath = result?.PackagePath ?? string.Empty;
            var failure = new DirectImportFailure
            {
                SourcePath = sourcePath,
                Status = result?.ImportStatus ?? AmariUnityPackagePipelineOperationStatus.Cancelled,
                CancellationReason = result?.CancellationReason ?? AmariUnityPackageImportCancellationReason.WindowClosedFallback,
                FailureReason = result?.FailureReason ?? AmariUnityPackageImportFailureReason.None,
                ErrorMessage = result?.ErrorMessage ?? string.Empty
            };
            var statusText = LocalizeStatusText(failure.Status);
            var reasonMessage = BuildLocalizedDirectImportReasonMessage(failure);
            var failedPaths = string.IsNullOrWhiteSpace(sourcePath)
                ? System.Array.Empty<string>()
                : new[] { sourcePath };

            AbortInProgressImportFlows();
            ShowImportFailureDialog(statusText, reasonMessage, failedPaths);
        }

        internal void UpdateImportInProgressOverlayVisibility()
        {
            if (_importInProgressOverlay == null)
            {
                return;
            }

            _importInProgressOverlay.style.display = IsAnyImportFlowRunning()
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }

        private void RefreshImportInProgressOverlayLocalizedTexts()
        {
            if (_importInProgressLabel == null)
            {
                return;
            }

            _importInProgressLabel.text = Localize(
                "amari.window.avatarCustomize.importInProgress.message",
                "Importing...");
        }

        internal void IncrementAmriApplyModalCount()
        {
            _amriApplyModalOpenCount++;
            UpdateImportInProgressOverlayVisibility();
        }

        internal void DecrementAmriApplyModalCount()
        {
            if (_amriApplyModalOpenCount > 0)
            {
                _amriApplyModalOpenCount--;
            }

            UpdateImportInProgressOverlayVisibility();
        }
    }
}
