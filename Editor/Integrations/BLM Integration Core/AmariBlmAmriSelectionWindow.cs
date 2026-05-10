using System;
using System.Collections.Generic;
using System.Linq;
using com.amari_noa.unity_editor_localization_core.editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// ReSharper disable once CheckNamespace
namespace com.amari_noa.avatar_modular_assistant.editor
{
    public sealed class AmariBlmAmriSelectionWindow : EditorWindow
    {
        private const string UxmlPath = "Packages/com.amari-noa.avatar-modular-assistant/Editor/Integrations/BLM Integration Core/AmariBlmAmriSelectionWindow.uxml";
        private const string UssPath = "Packages/com.amari-noa.avatar-modular-assistant/Editor/Integrations/BLM Integration Core/AmariBlmAmriSelectionWindow.uss";

        public enum AmriModalItemStatus
        {
            Info,
            Warning,
            Critical
        }

        public sealed class AmriModalItem
        {
            public string SourcePath;
            public string DisplayPath;
            public AmriModalItemStatus Status;
            public bool IsSelected = true;
        }

        private readonly List<AmriModalItem> _items = new();
        private string _batchId = string.Empty;
        private Func<string, string, string> _localize;
        private Action<bool, IReadOnlyList<string>> _onClosed;
        private bool _isResolved;

        private Label _headerLabel;
        private Button _selectAllButton;
        private Button _deselectAllButton;
        private ScrollView _itemsScrollView;
        private Button _skipButton;
        private Button _importSelectedButton;

        public static void Open(
            string batchId,
            IReadOnlyList<AmriModalItem> items,
            Func<string, string, string> localize,
            Action<bool, IReadOnlyList<string>> onClosed)
        {
            var window = CreateInstance<AmariBlmAmriSelectionWindow>();
            window.Initialize(batchId, items, localize, onClosed);
            window.titleContent = new GUIContent(window.L("amari.window.avatarCustomize.blm.amri_modal.title", "AMRI Import Confirmation"));
            window.minSize = new Vector2(780f, 420f);
            window.maxSize = new Vector2(1400f, 900f);
            window.ShowUtility();
            window.Focus();
        }

        private void Initialize(
            string batchId,
            IReadOnlyList<AmriModalItem> items,
            Func<string, string, string> localize,
            Action<bool, IReadOnlyList<string>> onClosed)
        {
            _batchId = batchId ?? string.Empty;
            _localize = localize;
            _onClosed = onClosed;
            _isResolved = false;
            _items.Clear();

            if (items == null)
            {
                return;
            }

            foreach (var item in items.Where(item => item != null))
            {
                _items.Add(new AmriModalItem
                {
                    SourcePath = item.SourcePath,
                    DisplayPath = item.DisplayPath,
                    Status = item.Status,
                    IsSelected = true
                });
            }
        }

        private void OnEnable()
        {
            EditorLocalization.Service.LanguageChanged += OnLanguageChanged;
        }

        private void OnDisable()
        {
            EditorLocalization.Service.LanguageChanged -= OnLanguageChanged;
            if (_isResolved)
            {
                return;
            }

            ResolveAndClose(false);
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.Clear();

            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            if (tree == null)
            {
                Debug.LogError($"[AMARI] UXML not found: {UxmlPath}");
                return;
            }

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
            if (styleSheet != null)
            {
                root.styleSheets.Add(styleSheet);
            }

            root.Add(tree.Instantiate());
            BindUi(root);
            RegisterEvents();
            RefreshLocalizedTexts();
            RebuildRows();
        }

        private void BindUi(VisualElement root)
        {
            _headerLabel = root.Q<Label>("HeaderLabel");
            _selectAllButton = root.Q<Button>("SelectAllButton");
            _deselectAllButton = root.Q<Button>("DeselectAllButton");
            _itemsScrollView = root.Q<ScrollView>("ItemsScrollView");
            _skipButton = root.Q<Button>("SkipButton");
            _importSelectedButton = root.Q<Button>("ImportSelectedButton");

            if (_itemsScrollView != null)
            {
                _itemsScrollView.mode = ScrollViewMode.Vertical;
                _itemsScrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            }
        }

        private void RegisterEvents()
        {
            if (_selectAllButton != null)
            {
                _selectAllButton.clicked -= OnSelectAllClicked;
                _selectAllButton.clicked += OnSelectAllClicked;
            }

            if (_deselectAllButton != null)
            {
                _deselectAllButton.clicked -= OnDeselectAllClicked;
                _deselectAllButton.clicked += OnDeselectAllClicked;
            }

            if (_skipButton != null)
            {
                _skipButton.clicked -= OnSkipClicked;
                _skipButton.clicked += OnSkipClicked;
            }

            if (_importSelectedButton != null)
            {
                _importSelectedButton.clicked -= OnImportSelectedClicked;
                _importSelectedButton.clicked += OnImportSelectedClicked;
            }
        }

        private void OnLanguageChanged(string _)
        {
            RefreshLocalizedTexts();
            RebuildRows();
            Repaint();
        }

        private void RefreshLocalizedTexts()
        {
            titleContent = new GUIContent(L("amari.window.avatarCustomize.blm.amri_modal.title", "AMRI Import Confirmation"));

            if (_headerLabel != null)
            {
                _headerLabel.text = string.Format(
                    L("amari.window.avatarCustomize.blm.amri_modal.header_format", "Batch: {0} / {1} file(s)"),
                    _batchId,
                    _items.Count);
            }

            if (_selectAllButton != null)
            {
                _selectAllButton.text = L("amari.window.avatarCustomize.blm.amri_modal.select_all", "Select All");
            }

            if (_deselectAllButton != null)
            {
                _deselectAllButton.text = L("amari.window.avatarCustomize.blm.amri_modal.deselect_all", "Deselect All");
            }

            if (_skipButton != null)
            {
                _skipButton.text = L("amari.window.avatarCustomize.blm.amri_modal.skip", "Skip");
            }

            if (_importSelectedButton != null)
            {
                _importSelectedButton.text = L("amari.window.avatarCustomize.blm.amri_modal.import_selected", "Import Selected");
            }
        }

        private void RebuildRows()
        {
            if (_itemsScrollView == null)
            {
                return;
            }

            _itemsScrollView.Clear();
            foreach (var item in _items)
            {
                if (item == null)
                {
                    continue;
                }

                _itemsScrollView.Add(CreateItemRow(item));
            }
        }

        private VisualElement CreateItemRow(AmriModalItem item)
        {
            var row = new VisualElement();
            row.AddToClassList("amri-row");

            var toggle = new Toggle { value = item.IsSelected };
            toggle.AddToClassList("amri-toggle");
            toggle.RegisterValueChangedCallback(evt => item.IsSelected = evt.newValue);
            row.Add(toggle);

            var statusContainer = new VisualElement();
            statusContainer.AddToClassList("amri-status");

            var iconImage = new Image();
            iconImage.AddToClassList("amri-status-icon");
            var iconName = item.Status switch
            {
                AmriModalItemStatus.Info => "console.infoicon.sml",
                AmriModalItemStatus.Warning => "console.warnicon.sml",
                _ => "console.erroricon.sml"
            };
            iconImage.image = EditorGUIUtility.IconContent(iconName)?.image;
            statusContainer.Add(iconImage);

            var statusText = item.Status switch
            {
                AmriModalItemStatus.Info => L("amari.window.avatarCustomize.blm.amri_modal.status_info", "Imported"),
                AmriModalItemStatus.Warning => L("amari.window.avatarCustomize.blm.amri_modal.status_warning", "Partially Imported"),
                _ => L("amari.window.avatarCustomize.blm.amri_modal.status_critical", "Not Imported")
            };
            var statusLabel = new Label(statusText);
            statusLabel.AddToClassList("amri-status-label");
            statusContainer.Add(statusLabel);

            row.Add(statusContainer);

            var pathLabel = new Label(item.DisplayPath ?? item.SourcePath ?? string.Empty);
            pathLabel.AddToClassList("amri-path-label");
            row.Add(pathLabel);

            return row;
        }

        private void OnSelectAllClicked()
        {
            foreach (var item in _items)
            {
                if (item != null)
                {
                    item.IsSelected = true;
                }
            }

            RebuildRows();
        }

        private void OnDeselectAllClicked()
        {
            foreach (var item in _items)
            {
                if (item != null)
                {
                    item.IsSelected = false;
                }
            }

            RebuildRows();
        }

        private void OnSkipClicked()
        {
            ResolveAndClose(false);
        }

        private void OnImportSelectedClicked()
        {
            ResolveAndClose(true);
        }

        private void ResolveAndClose(bool shouldImportSelected)
        {
            if (_isResolved)
            {
                return;
            }

            _isResolved = true;

            var selectedPaths = shouldImportSelected
                ? _items
                    .Where(item => item != null && item.IsSelected && !string.IsNullOrWhiteSpace(item.SourcePath))
                    .Select(item => item.SourcePath)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : Array.Empty<string>();

            try
            {
                _onClosed?.Invoke(shouldImportSelected, selectedPaths);
            }
            finally
            {
                Close();
            }
        }

        private string L(string key, string fallback)
        {
            return _localize?.Invoke(key, fallback) ?? fallback;
        }
    }
}
