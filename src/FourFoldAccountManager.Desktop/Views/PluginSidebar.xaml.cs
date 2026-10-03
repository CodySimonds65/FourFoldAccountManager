using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Overlay;
using FourFoldAccountManager.Core.Plugins;
using FourFoldAccountManager.Core.Plugins.Hub;
using FourFoldAccountManager.Desktop.Plugins;
using FourFoldAccountManager.Desktop.Plugins.Hub;
using FourFoldAccountManager.Desktop.Plugins.Web;

namespace FourFoldAccountManager.Desktop.Views;

// Hosts the plugin strip and the one page beside it: a plugin's panel, the plugin list, or a plugin's settings.
// It only renders the saved settings and reports what the user asked for; MainWindow saves and re-renders.
public partial class PluginSidebar : UserControl
{
    private const string IconFont = "Segoe Fluent Icons, Segoe MDL2 Assets";
    // A private drag format, so only a strip icon can be dropped on the strip.
    private const string DragFormat = "FourFold.PluginId";
    // The hub page builds every row it shows each time it refreshes, so it shows only this many matches. Installed
    // plugins sort first, so they are always among them. A virtualising list is the upgrade if the hub outgrows this.
    private const int HubRowLimit = 50;
    private IReadOnlyList<IFourFoldPlugin> _plugins = [];
    private PanelSettings _settings = PanelSettings.Default;
    private bool _showingList;
    private IFourFoldPlugin? _settingsFor;
    private IFourFoldPlugin? _lastShown;
    private bool _stayOpen;
    private Point _pressPoint;
    private string? _pressedId;
    private IReadOnlyList<RejectedPlugin> _rejected = [];
    private string? _communityError;
    private HubViewState _hub = HubViewState.Empty;
    private bool _showingHub;
    private string? _hubExpandedId;
    private Grid? _hubPage;
    private TextBox? _hubSearch;
    private StackPanel? _hubRows;

    public PluginSidebar()
    {
        InitializeComponent();
        PluginListButton.Content = CreateIconContent("\uE90F", "Plugins");
    }

    public event Action<string>? OpenRequested;
    public event Action? CloseRequested;
    public event Action<string, int>? MoveRequested;
    public event Action<string, bool>? EnabledChangeRequested;
    public event Action<bool>? DeveloperModeChangeRequested;
    public event Action? OpenDevFolderRequested;
    // Opening the plugin list from a closed panel counts as opening the panel.
    public event Action? PanelOpenRequested;
    // The hub page was opened; a good moment to check the hub.
    public event Action? HubOpened;
    public event Action? HubRetryRequested;
    public event Action<string>? HubInstallRequested;
    public event Action<string>? HubUninstallRequested;
    public event Action<Uri>? HubSourceRequested;

    // In the tools window the panel never closes, since a window that's just a strip would be odd. Moving between the
    // main window and the tools window starts from the plugin view, not the plugin list or a settings page.
    public bool StayOpen
    {
        set
        {
            if (_stayOpen == value)
            {
                return;
            }

            _stayOpen = value;
            _showingList = false;
            _settingsFor = null;
            _showingHub = false;
            Render(_settings);
        }
    }

    public void SetPlugins(IReadOnlyList<IFourFoldPlugin> plugins)
    {
        _plugins = plugins;
        Render(_settings);
    }

    public void SetCommunityState(IReadOnlyList<RejectedPlugin> rejected, string? startupError)
    {
        _rejected = rejected;
        _communityError = startupError;
        Render(_settings);
    }

    public void SetHubState(HubViewState state)
    {
        _hub = state;
        Render(_settings);
    }

    // True while this plugin's panel is the page on screen.
    public bool IsShowing(string pluginId) =>
        IsVisible && !_showingList && ShownPlugin()?.Descriptor.Id == pluginId;

    public void Render(PanelSettings settings)
    {
        _settings = settings;
        if (_settingsFor is { } settingsPlugin && !_plugins.Contains(settingsPlugin))
        {
            _settingsFor = null;
        }

        var shown = ShownPlugin();
        // The tools window always shows something: the plugin list when no plugin can show.
        if (_stayOpen && shown is null)
        {
            _showingList = true;
        }

        RenderStrip(shown);
        RenderPage(shown);
    }

    private IReadOnlyList<PluginDescriptor> Descriptors => _plugins.Select(plugin => plugin.Descriptor).ToArray();

    private IFourFoldPlugin? Find(string id) => _plugins.FirstOrDefault(plugin => plugin.Descriptor.Id == id);

    private IReadOnlyList<IFourFoldPlugin> StripPlugins() =>
        PluginLayoutPolicy.Ordered(_settings, Descriptors)
            .Where(descriptor => PluginLayoutPolicy.IsEnabled(_settings, descriptor.Id))
            .Select(descriptor => Find(descriptor.Id)!)
            .ToArray();

    // The plugin whose panel shows, or null when the plugin list shows or the panel is closed.
    private IFourFoldPlugin? ShownPlugin()
    {
        if (_showingList)
        {
            return null;
        }

        if (PluginLayoutPolicy.OpenPlugin(_settings, Descriptors) is { } open &&
            (_settings.PluginsSidebarExpanded || _stayOpen))
        {
            return Find(open.Id);
        }

        return _stayOpen ? StripPlugins().FirstOrDefault() : null;
    }

    private void RenderStrip(IFourFoldPlugin? shown)
    {
        StripIcons.Children.Clear();
        foreach (var plugin in StripPlugins())
        {
            var descriptor = plugin.Descriptor;
            var button = new Button
            {
                Style = (Style)FindResource("StripButtonStyle"),
                Content = CreateIconContent(descriptor, descriptor.ShortLabel),
                ToolTip = descriptor.Name,
                Tag = ReferenceEquals(plugin, shown) ? "lit" : null,
                DataContext = descriptor.Id
            };
            AutomationProperties.SetName(button, descriptor.Name);
            button.Click += (_, _) => PluginIcon_Click(descriptor.Id);
            button.PreviewMouseLeftButtonDown += StripIcon_PreviewMouseLeftButtonDown;
            button.PreviewMouseLeftButtonUp += (_, _) => _pressedId = null;
            button.PreviewMouseMove += StripIcon_PreviewMouseMove;
            StripIcons.Children.Add(button);
        }

        PluginListButton.Tag = _showingList ? "lit" : null;
    }

    private void RenderPage(IFourFoldPlugin? shown)
    {
        var visible = _showingList || shown is not null;
        PanelArea.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        // The tools window gives the panel whatever width the strip leaves, so the strip stays at the window's edge.
        PanelColumn.Width = !visible ? new GridLength(0)
            : _stayOpen ? new GridLength(1, GridUnitType.Star) : new GridLength(250);
        PanelGapColumn.Width = new GridLength(visible ? 6 : 0);
        BackButton.Visibility = _showingList && (_settingsFor is not null || _showingHub)
            ? Visibility.Visible
            : Visibility.Collapsed;

        // A plugin's panel carries its own header, so only the plugin list and settings pages get the host's.
        var showingPlugin = !_showingList && shown is not null;
        PageCard.Visibility = _showingList ? Visibility.Visible : Visibility.Collapsed;
        PluginHost.Content = showingPlugin ? shown!.Panel : null;
        if (_showingList && _settingsFor is { } settingsPlugin)
        {
            KickerText.Text = settingsPlugin.Descriptor.Name.ToUpperInvariant();
            TitleText.Text = "Settings";
            PageHost.Content = settingsPlugin.SettingsPage;
        }
        else if (_showingList && _showingHub)
        {
            KickerText.Text = "PLUGINS";
            TitleText.Text = "Plugin hub";
            PageHost.Content = HubPage();
        }
        else if (_showingList)
        {
            KickerText.Text = "PLUGINS";
            TitleText.Text = "Plugin list";
            PageHost.Content = BuildPluginList();
        }
        else
        {
            PageHost.Content = null;
        }

        // Set before Opened(), which can ask MainWindow to refresh and render again.
        var previous = _lastShown;
        _lastShown = shown;
        if (shown is not null && !ReferenceEquals(shown, previous))
        {
            shown.Opened();
        }
    }

    private UIElement BuildPluginList()
    {
        var list = new StackPanel();
        var hubButton = new Button
        {
            Content = "Plugin hub",
            Height = 30,
            Margin = new Thickness(0, 0, 0, 10),
            Style = (Style)FindResource("AppButtonStyle")
        };
        hubButton.Click += (_, _) =>
        {
            _showingHub = true;
            _hubExpandedId = null;
            Render(_settings);
            HubOpened?.Invoke();
        };
        list.Children.Add(hubButton);
        foreach (var descriptor in PluginLayoutPolicy.Ordered(_settings, Descriptors))
        {
            var plugin = Find(descriptor.Id)!;
            var enabled = PluginLayoutPolicy.IsEnabled(_settings, descriptor.Id);
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var border = new Border
            {
                Padding = new Thickness(10, 8, 10, 8),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                Child = row
            };
            border.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceRaised");
            border.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");

            var name = new TextBlock { Text = descriptor.Name, VerticalAlignment = VerticalAlignment.Center };
            name.SetResourceReference(TextBlock.ForegroundProperty, enabled ? "Brush.TextPrimary" : "Brush.TextMuted");
            row.Children.Add(CreateNameBlock(name, descriptor.Badge, descriptor.Detail, detailBrush: "Brush.TextMuted"));

            if (plugin.SettingsPage is not null)
            {
                var cog = new Button
                {
                    Content = new TextBlock { Text = "\uE713", FontFamily = new FontFamily(IconFont), FontSize = 13 },
                    Width = 28,
                    Height = 26,
                    Padding = new Thickness(0),
                    Margin = new Thickness(6, 0, 6, 0),
                    Style = (Style)FindResource("AppButtonStyle"),
                    ToolTip = $"{descriptor.Name} settings"
                };
                AutomationProperties.SetName(cog, $"{descriptor.Name} settings");
                cog.Click += (_, _) =>
                {
                    _settingsFor = plugin;
                    Render(_settings);
                };
                Grid.SetColumn(cog, 1);
                row.Children.Add(cog);
            }

            var toggle = new CheckBox
            {
                IsChecked = enabled,
                VerticalAlignment = VerticalAlignment.Center,
                Style = (Style)FindResource("PluginSwitchStyle")
            };
            AutomationProperties.SetName(toggle, $"{descriptor.Name} on");
            toggle.Click += (_, _) => EnabledChangeRequested?.Invoke(descriptor.Id, toggle.IsChecked == true);
            Grid.SetColumn(toggle, 2);
            row.Children.Add(toggle);
            list.Children.Add(border);
        }

        foreach (var rejected in _rejected)
        {
            var name = new TextBlock { Text = rejected.FolderName, VerticalAlignment = VerticalAlignment.Center };
            name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            var border = new Border
            {
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 6),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                Child = CreateNameBlock(name, "DEV", rejected.Reason, detailBrush: "Brush.Danger")
            };
            border.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceRaised");
            border.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            list.Children.Add(border);
        }

        // An installed plugin the hub no longer lists: it can't run, so it has no switch, only its reason and a way out.
        foreach (var pulled in _hub.Pulled)
        {
            var name = new TextBlock { Text = pulled.Name, VerticalAlignment = VerticalAlignment.Center };
            name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            var uninstall = new Button
            {
                Content = "Uninstall",
                Padding = new Thickness(8, 0, 8, 0),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                IsEnabled = !_hub.Busy.Contains(pulled.Id),
                Style = (Style)FindResource("AppButtonStyle")
            };
            AutomationProperties.SetName(uninstall, $"Uninstall {pulled.Name}");
            uninstall.Click += (_, _) => HubUninstallRequested?.Invoke(pulled.Id);
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            // The hub's stock reasons already say it; only a maintainer's own reason needs the lead-in.
            var block = CreateNameBlock(
                name,
                null,
                pulled.Reason is HubPolicy.RemovedReason or HubPolicy.UnlistedReason
                    ? pulled.Reason
                    : "Removed from the hub. " + pulled.Reason,
                detailBrush: "Brush.Danger");
            // A failed removal shows why, on its own line under the reason.
            if (_hub.Errors.TryGetValue(pulled.Id, out var removalError))
            {
                block.Children.Add(HubNote(removalError, "Brush.Danger"));
            }

            row.Children.Add(block);
            Grid.SetColumn(uninstall, 1);
            row.Children.Add(uninstall);
            var border = new Border
            {
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 6),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                Child = row
            };
            border.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceRaised");
            border.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            list.Children.Add(border);
        }

        if (_communityError is not null)
        {
            var error = new TextBlock { Text = _communityError, FontSize = 11, TextWrapping = TextWrapping.Wrap };
            error.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Danger");
            list.Children.Add(error);
        }

        var note = new TextBlock
        {
            Text = "Drag icons in the strip to reorder them.",
            FontSize = 11,
            Margin = new Thickness(2, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        note.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        list.Children.Add(note);

        var developerRow = new Grid { Margin = new Thickness(2, 16, 0, 0) };
        developerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        developerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var developerLabel = new TextBlock { Text = "Developer mode", VerticalAlignment = VerticalAlignment.Center };
        developerRow.Children.Add(developerLabel);
        var developerToggle = new CheckBox
        {
            IsChecked = _settings.PluginDeveloperMode,
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)FindResource("PluginSwitchStyle")
        };
        AutomationProperties.SetName(developerToggle, "Developer mode");
        developerToggle.Click += (_, _) => DeveloperModeChangeRequested?.Invoke(developerToggle.IsChecked == true);
        Grid.SetColumn(developerToggle, 1);
        developerRow.Children.Add(developerToggle);
        list.Children.Add(developerRow);

        var developerNote = new TextBlock
        {
            Text = "Loads your own plugins from the dev plugins folder. They aren't reviewed.",
            FontSize = 11,
            Margin = new Thickness(2, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        developerNote.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        list.Children.Add(developerNote);
        if (_settings.PluginDeveloperMode)
        {
            var openFolder = new Button
            {
                Content = "Open dev plugins folder",
                Height = 30,
                Margin = new Thickness(0, 8, 0, 0),
                Style = (Style)FindResource("AppButtonStyle")
            };
            openFolder.Click += (_, _) => OpenDevFolderRequested?.Invoke();
            list.Children.Add(openFolder);
        }

        return new ScrollViewer
        {
            Content = list,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
    }

    // The hub page is built once and kept, so what is typed in the search box survives every refresh of the rows.
    private UIElement HubPage()
    {
        if (_hubPage is null)
        {
            _hubSearch = new TextBox { Style = (Style)FindResource("InputStyle") };
            AutomationProperties.SetName(_hubSearch, "Search plugins");
            var hint = new TextBlock
            {
                Text = "Search plugins",
                IsHitTestVisible = false,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            _hubSearch.TextChanged += (_, _) =>
            {
                hint.Visibility = string.IsNullOrWhiteSpace(_hubSearch.Text) ? Visibility.Visible : Visibility.Collapsed;
                RebuildHubRows();
            };
            var searchArea = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            searchArea.Children.Add(_hubSearch);
            searchArea.Children.Add(hint);

            _hubRows = new StackPanel();
            var scroll = new ScrollViewer
            {
                Content = _hubRows,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            Grid.SetRow(scroll, 1);
            _hubPage = new Grid();
            _hubPage.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _hubPage.RowDefinitions.Add(new RowDefinition());
            _hubPage.Children.Add(searchArea);
            _hubPage.Children.Add(scroll);
        }

        RebuildHubRows();
        return _hubPage;
    }

    // Rebuilding drops the button that has the keyboard focus, so the focus is put back afterwards. Focus that was
    // somewhere else (the search box while typing) is left alone.
    private void RebuildHubRows()
    {
        if (_hubRows is null || _hubSearch is null)
        {
            return;
        }

        string? focusedName = null;
        string? focusedRowId = null;
        if (_hubRows.IsKeyboardFocusWithin && Keyboard.FocusedElement is DependencyObject focused)
        {
            focusedName = AutomationProperties.GetName(focused);
            focusedRowId = _hubRows.Children.OfType<Border>().FirstOrDefault(row => row.IsKeyboardFocusWithin)?.Tag as string;
        }

        FillHubRows();
        if (focusedName is not null)
        {
            RestoreHubFocus(focusedName, focusedRowId);
        }
    }

    // Focus goes to the button with the same name in the same row, else that row's Details button, else (the row is
    // gone) the search box. A Details button keeps its name when it reads "Less", so it is found again.
    private void RestoreHubFocus(string name, string? rowId)
    {
        FrameworkElement? scope = _hubRows;
        if (rowId is not null)
        {
            scope = _hubRows!.Children.OfType<Border>().FirstOrDefault(row => row.Tag as string == rowId);
        }

        var buttons = scope is null ? [] : HubButtons(scope).Where(button => button.IsEnabled).ToList();
        UIElement? target = buttons.FirstOrDefault(button => AutomationProperties.GetName(button) == name) ??
                            (rowId is null ? null : buttons.FirstOrDefault());
        (target ?? _hubSearch)?.Focus();
    }

    // Every button below the element, in order. A row's first button is its Details button.
    private static IEnumerable<Button> HubButtons(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            if (child is Button button)
            {
                yield return button;
            }

            foreach (var inner in HubButtons(child))
            {
                yield return inner;
            }
        }
    }

    private void FillHubRows()
    {
        _hubRows!.Children.Clear();
        if (_hub.Unreachable)
        {
            // A list that is still showing is the last one that was fetched.
            _hubRows.Children.Add(HubNote(
                "The hub couldn't be reached. The plugins you have installed keep working." +
                (_hub.Plugins.Count > 0 ? " This list may be out of date." : ""),
                "Brush.Danger"));
            var retry = new Button
            {
                Content = "Try again",
                Padding = new Thickness(10, 0, 10, 0),
                Margin = new Thickness(0, 6, 0, 10),
                HorizontalAlignment = HorizontalAlignment.Left,
                IsEnabled = !_hub.Loading,
                Style = (Style)FindResource("AppButtonStyle")
            };
            retry.Click += (_, _) => HubRetryRequested?.Invoke();
            _hubRows.Children.Add(retry);
        }

        if (_hub.Plugins.Count == 0)
        {
            if (_hub.Loading)
            {
                _hubRows.Children.Add(HubNote("Loading the hub…", "Brush.TextMuted"));
            }
            else if (!_hub.Unreachable)
            {
                _hubRows.Children.Add(HubNote("The hub has no plugins yet.", "Brush.TextMuted"));
            }

            return;
        }

        var matches = HubPolicy.Search(_hub.Plugins, _hubSearch!.Text, _hub.Installed);
        if (matches.Count == 0)
        {
            _hubRows.Children.Add(HubNote("No plugins match.", "Brush.TextMuted"));
        }

        foreach (var plugin in matches.Take(HubRowLimit))
        {
            _hubRows.Children.Add(CreateHubRow(plugin));
        }

        if (matches.Count > HubRowLimit)
        {
            _hubRows.Children.Add(HubNote(
                $"Showing the first {HubRowLimit} of {matches.Count}. Search to narrow it down.", "Brush.TextMuted"));
        }
    }

    // One small wrapping line of text. Everything from the catalog is shown this way: as plain text.
    private static TextBlock HubNote(string text, string brush)
    {
        var note = new TextBlock { Text = text, FontSize = 11, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap };
        note.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return note;
    }

    private Border CreateHubRow(HubPlugin plugin)
    {
        var installed = _hub.Installed.Contains(plugin.Id);
        var busy = _hub.Busy.Contains(plugin.Id);
        var expanded = _hubExpandedId == plugin.Id;
        void Toggle()
        {
            _hubExpandedId = expanded ? null : plugin.Id;
            RebuildHubRows();
        }

        // The first letter of the name stands in for an icon; the plugin's own icon shows in the strip once installed.
        var tileText = new TextBlock
        {
            Text = StringInfo.GetNextTextElement(plugin.Name).ToUpperInvariant(),
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        tileText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.AccentGold");
        var tile = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(5),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Child = tileText
        };
        tile.SetResourceReference(Border.BackgroundProperty, "Brush.BorderStrong");

        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = plugin.Name,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        text.Children.Add(HubNote($"by {plugin.Author} · v{plugin.Version}", "Brush.TextMuted"));
        // Seen before Install is pressed; an expanded row says it below instead, so it isn't said twice.
        if (plugin.AnySite && !expanded)
        {
            text.Children.Add(HubNote("Can contact any website", "Brush.AccentGold"));
        }

        if (_hub.Errors.TryGetValue(plugin.Id, out var error))
        {
            text.Children.Add(HubNote(error, "Brush.Danger"));
        }

        if (expanded)
        {
            if (plugin.Description.Length > 0)
            {
                text.Children.Add(HubNote(plugin.Description, "Brush.TextSecondary"));
            }

            // SiteLabel shows the real xn-- host name, so a look-alike Unicode host name can't pass for another site.
            text.Children.Add(HubNote(
                plugin.AnySite ? "Can contact: any website"
                : plugin.Sites.Count == 0 ? "Can contact: no websites"
                : "Can contact: " + string.Join(", ", plugin.Sites.Select(PluginNetworkPolicy.SiteLabel)),
                plugin.AnySite ? "Brush.AccentGold" : "Brush.TextMuted"));
            if (plugin.Cards.Count > 0)
            {
                text.Children.Add(HubNote(
                    "Adds cards: " + string.Join(", ", plugin.Cards.Select(card =>
                        $"{card.Name} ({(card.Scope == OverlayAddOnScope.Account ? "per account" : "global")})")),
                    "Brush.TextMuted"));
            }

            if (DateOnly.TryParseExact(
                    plugin.Reviewed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var reviewed))
            {
                text.Children.Add(HubNote(
                    "Reviewed " + reviewed.ToString("d MMM yyyy", CultureInfo.InvariantCulture), "Brush.TextMuted"));
            }
        }

        var actions = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        // A button as well as the row click, so the details can be opened from the keyboard.
        var details = new Button
        {
            Content = expanded ? "Less" : "Details",
            Padding = new Thickness(8, 0, 8, 0),
            Style = (Style)FindResource("AppButtonStyle")
        };
        AutomationProperties.SetName(details, $"{plugin.Name} details");
        details.Click += (_, _) => Toggle();
        actions.Children.Add(details);
        if (expanded)
        {
            var source = new Button
            {
                Content = "Source",
                Padding = new Thickness(8, 0, 8, 0),
                Margin = new Thickness(6, 0, 0, 0),
                Style = (Style)FindResource("AppButtonStyle")
            };
            AutomationProperties.SetName(source, $"{plugin.Name} source");
            source.Click += (_, _) => HubSourceRequested?.Invoke(plugin.Repository);
            actions.Children.Add(source);
            if (installed)
            {
                var uninstall = new Button
                {
                    Content = "Uninstall",
                    Padding = new Thickness(8, 0, 8, 0),
                    Margin = new Thickness(6, 0, 0, 0),
                    IsEnabled = !busy,
                    Style = (Style)FindResource("AppButtonStyle")
                };
                AutomationProperties.SetName(uninstall, $"Uninstall {plugin.Name}");
                uninstall.Click += (_, _) => HubUninstallRequested?.Invoke(plugin.Id);
                actions.Children.Add(uninstall);
            }
        }

        text.Children.Add(actions);
        Grid.SetColumn(text, 1);

        FrameworkElement action;
        if (installed && !busy)
        {
            action = HubNote("Installed", "Brush.TextMuted");
        }
        else
        {
            var install = new Button
            {
                Content = busy ? "Working…" : "Install",
                Padding = new Thickness(10, 0, 10, 0),
                IsEnabled = !busy,
                Style = (Style)FindResource("PrimaryButtonStyle")
            };
            // The name follows what the button says, so a screen reader doesn't offer "Install" while it is working.
            AutomationProperties.SetName(install, busy ? $"{plugin.Name}: working" : $"Install {plugin.Name}");
            install.Click += (_, _) => HubInstallRequested?.Invoke(plugin.Id);
            action = install;
        }

        action.Margin = new Thickness(8, 0, 0, 0);
        action.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(action, 2);

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(tile);
        row.Children.Add(text);
        row.Children.Add(action);
        var border = new Border
        {
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 6),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            // Clips a name of stacked combining marks, which would otherwise draw over the row above.
            ClipToBounds = true,
            // Which plugin this row is, so a rebuild can find it again to put the keyboard focus back.
            Tag = plugin.Id,
            Child = row
        };
        border.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceRaised");
        border.SetResourceReference(Border.BorderBrushProperty, expanded ? "Brush.AccentGold" : "Brush.Border");
        // A button inside the row handles its own click, so this only sees clicks on the row itself.
        border.MouseLeftButtonUp += (_, _) => Toggle();
        return border;
    }

    // A plugin list row's text: the name with an optional small tag beside it, and an optional line underneath.
    private static StackPanel CreateNameBlock(TextBlock name, string? badge, string? detail, string detailBrush)
    {
        // Wraps, so a long name beside its tag (or a long folder name) never draws over the switch.
        var nameLine = new WrapPanel();
        name.TextWrapping = TextWrapping.Wrap;
        nameLine.Children.Add(name);
        if (badge is not null)
        {
            var tagText = new TextBlock { Text = badge, FontSize = 9, FontWeight = FontWeights.Bold };
            tagText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.AccentGold");
            var tag = new Border
            {
                Margin = new Thickness(6, 0, 0, 0),
                Padding = new Thickness(4, 1, 4, 1),
                CornerRadius = new CornerRadius(3),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = tagText
            };
            tag.SetResourceReference(Border.BorderBrushProperty, "Brush.AccentGold");
            nameLine.Children.Add(tag);
        }

        var block = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        block.Children.Add(nameLine);
        if (!string.IsNullOrEmpty(detail))
        {
            var detailText = new TextBlock
            {
                Text = detail,
                FontSize = 11,
                Margin = new Thickness(0, 3, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            detailText.SetResourceReference(TextBlock.ForegroundProperty, detailBrush);
            block.Children.Add(detailText);
        }

        return block;
    }

    // A community plugin's icon image, or the glyph when it has none or the image can't be read.
    private static StackPanel CreateIconContent(PluginDescriptor descriptor, string label)
    {
        if (descriptor.IconPath is { } path)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                // Not the shared image cache, so a hot-reloaded plugin shows its new icon.
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path);
                bitmap.DecodePixelWidth = 40;
                bitmap.EndInit();
                bitmap.Freeze();
                var content = CreateIconContent(string.Empty, label);
                content.Children[0] = new Image
                {
                    Source = bitmap,
                    Width = 20,
                    Height = 20,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                return content;
            }
            catch (Exception)
            {
                // Any unreadable image (missing, locked, corrupt, not an image) falls back to the glyph.
            }
        }

        return CreateIconContent(descriptor.Icon, label);
    }

    private static StackPanel CreateIconContent(string glyph, string label)
    {
        var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        content.Children.Add(new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily(IconFont),
            FontSize = 16,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        content.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 9,
            Margin = new Thickness(0, 3, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        return content;
    }

    private void PluginIcon_Click(string id)
    {
        var wasShown = !_showingList && ReferenceEquals(ShownPlugin(), Find(id));
        _showingList = false;
        _settingsFor = null;
        _showingHub = false;
        if (wasShown)
        {
            // Clicking the lit icon closes the panel, except in the tools window.
            if (!_stayOpen)
            {
                CloseRequested?.Invoke();
            }

            return;
        }

        // MainWindow saves and re-renders; rendering here first would briefly show (and "open") the old plugin.
        OpenRequested?.Invoke(id);
    }

    private void PluginListButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_showingList)
        {
            _showingList = true;
            Render(_settings);
            if (!_settings.PluginsSidebarExpanded && !_stayOpen)
            {
                PanelOpenRequested?.Invoke();
            }

            return;
        }

        _showingList = false;
        _settingsFor = null;
        _showingHub = false;
        // Clicking the lit wrench closes the panel; the tools window goes back to the last plugin instead.
        if (_stayOpen)
        {
            Render(_settings);
            return;
        }

        CloseRequested?.Invoke();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        _settingsFor = null;
        _showingHub = false;
        Render(_settings);
    }

    private void StripIcon_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pressPoint = e.GetPosition(StripIcons);
        _pressedId = (sender as FrameworkElement)?.DataContext as string;
    }

    private void StripIcon_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        // A press released off the icon never reaches its button-up, so the released button clears the press too.
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _pressedId = null;
            return;
        }

        if (_pressedId is not { } id)
        {
            return;
        }

        var moved = e.GetPosition(StripIcons) - _pressPoint;
        if (Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance &&
            Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance)
        {
            return;
        }

        _pressedId = null;
        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(DragFormat, id), DragDropEffects.Move);
    }

    private void StripIcons_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DragFormat) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    // The new index counts the other icons above the drop point, so dropping an icon on itself changes nothing.
    private void StripIcons_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DragFormat) is not string id)
        {
            return;
        }

        var dropY = e.GetPosition(StripIcons).Y;
        var index = StripIcons.Children.OfType<FrameworkElement>()
            .Where(icon => icon.DataContext as string != id)
            .Count(icon => icon.TranslatePoint(new Point(0, icon.ActualHeight / 2), StripIcons).Y < dropY);
        MoveRequested?.Invoke(id, index);
    }
}
