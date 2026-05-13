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
    public sealed class AmariAmriApplySelectionWindow : EditorWindow
    {
        private const string UxmlPath = "Packages/com.amari-noa.avatar-modular-assistant/Editor/AvatarCustomize/AmariAmriApplySelectionWindow.uxml";
        private const string UssPath = "Packages/com.amari-noa.avatar-modular-assistant/Editor/AvatarCustomize/AmariAmriApplySelectionWindow.uss";

        public enum AmriApplyItemStatus
        {
            Info,
            Warning,
            Critical
        }

        public sealed class AmriApplyItem
        {
            public string AssetPath;
            public string DisplayPath;
            public AmriApplyItemStatus Status;
            public bool IsSelected = true;
        }

        private readonly List<AmriApplyItem> _items = new();
        private Func<string, string, string> _localize;
        private Action<bool, IReadOnlyList<string>> _onClosed;
        private bool _isResolved;

        private Label _headerLabel;
        private Button _selectAllButton;
        private Button _deselectAllButton;
        private ScrollView _itemsScrollView;
        private Button _cancelButton;
        private Button _applyButton;

        public static void Open(
            IReadOnlyList<AmriApplyItem> items,
            Func<string, string, string> localize,
            Action<bool, IReadOnlyList<string>> onClosed)
        {
            var window = CreateInstance<AmariAmriApplySelectionWindow>();
            window.Initialize(items, localize, onClosed);
            window.titleContent = new GUIContent(window.L("amari.window.avatarCustomize.amri_apply.title", "AMRI Apply Selection"));
            window.minSize = new Vector2(780f, 420f);
            window.maxSize = new Vector2(1400f, 900f);
            window.ShowUtility();
            window.Focus();
        }

        private void Initialize(
            IReadOnlyList<AmriApplyItem> items,
            Func<string, string, string> localize,
            Action<bool, IReadOnlyList<string>> onClosed)
        {
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
                _items.Add(new AmriApplyItem
                {
                    AssetPath = item.AssetPath,
                    DisplayPath = item.DisplayPath,
                    Status = item.Status,
                    IsSelected = item.IsSelected
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
            _cancelButton = root.Q<Button>("CancelButton");
            _applyButton = root.Q<Button>("ApplyButton");

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

            if (_cancelButton != null)
            {
                _cancelButton.clicked -= OnCancelClicked;
                _cancelButton.clicked += OnCancelClicked;
            }

            if (_applyButton != null)
            {
                _applyButton.clicked -= OnApplyClicked;
                _applyButton.clicked += OnApplyClicked;
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
            titleContent = new GUIContent(L("amari.window.avatarCustomize.amri_apply.title", "AMRI Apply Selection"));

            if (_headerLabel != null)
            {
                _headerLabel.text = string.Format(
                    L("amari.window.avatarCustomize.amri_apply.header_format", "{0} amri file(s) imported. Select which to apply to the current avatar."),
                    _items.Count);
            }

            if (_selectAllButton != null)
            {
                _selectAllButton.text = L("amari.window.avatarCustomize.amri_apply.select_all", "Select All");
            }

            if (_deselectAllButton != null)
            {
                _deselectAllButton.text = L("amari.window.avatarCustomize.amri_apply.deselect_all", "Deselect All");
            }

            if (_cancelButton != null)
            {
                _cancelButton.text = L("amari.window.avatarCustomize.amri_apply.cancel", "Cancel");
            }

            if (_applyButton != null)
            {
                _applyButton.text = L("amari.window.avatarCustomize.amri_apply.apply", "Apply");
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

        private VisualElement CreateItemRow(AmriApplyItem item)
        {
            var row = new VisualElement();
            row.AddToClassList("amri-apply-row");

            var toggle = new Toggle { value = item.IsSelected };
            toggle.AddToClassList("amri-apply-toggle");
            toggle.RegisterValueChangedCallback(evt => item.IsSelected = evt.newValue);
            row.Add(toggle);

            var statusContainer = new VisualElement();
            statusContainer.AddToClassList("amri-apply-status");

            var iconImage = new Image();
            iconImage.AddToClassList("amri-apply-status-icon");
            var iconName = item.Status switch
            {
                AmriApplyItemStatus.Info => "console.infoicon.sml",
                AmriApplyItemStatus.Warning => "console.warnicon.sml",
                _ => "console.erroricon.sml"
            };
            iconImage.image = EditorGUIUtility.IconContent(iconName)?.image;
            statusContainer.Add(iconImage);

            var statusText = item.Status switch
            {
                AmriApplyItemStatus.Info => L("amari.window.avatarCustomize.amri_apply.status_info", "Ready"),
                AmriApplyItemStatus.Warning => L("amari.window.avatarCustomize.amri_apply.status_warning", "Partial"),
                _ => L("amari.window.avatarCustomize.amri_apply.status_critical", "Issues")
            };
            var statusLabel = new Label(statusText);
            statusLabel.AddToClassList("amri-apply-status-label");
            statusContainer.Add(statusLabel);

            row.Add(statusContainer);

            var pathLabel = new Label(item.DisplayPath ?? item.AssetPath ?? string.Empty);
            pathLabel.AddToClassList("amri-apply-path-label");
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

        private void OnCancelClicked()
        {
            ResolveAndClose(false);
        }

        private void OnApplyClicked()
        {
            ResolveAndClose(true);
        }

        private void ResolveAndClose(bool shouldApply)
        {
            if (_isResolved)
            {
                return;
            }

            _isResolved = true;

            var selectedPaths = shouldApply
                ? _items
                    .Where(item => item != null && item.IsSelected && !string.IsNullOrWhiteSpace(item.AssetPath))
                    .Select(item => item.AssetPath)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : Array.Empty<string>();

            try
            {
                _onClosed?.Invoke(shouldApply, selectedPaths);
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
