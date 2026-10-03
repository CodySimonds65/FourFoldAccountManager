using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Plugins;
using FourFoldAccountManager.Desktop.Plugins;
using FourFoldAccountManager.Desktop.Plugins.Web;

namespace FourFoldAccountManager.Desktop.Views;

// Hosts the plugin strip and the one page beside it: a plugin's panel, the plugin list, or a plugin's settings.
// It only renders the saved settings and reports what the user asked for; MainWindow saves and re-renders.
public partial class PluginSidebar : UserControl
{
    private const string IconFont = "Segoe Fluent Icons, Segoe MDL2 Assets";
    // A private drag format, so only a strip icon can be dropped on the strip.
    private const string DragFormat = "FourFold.PluginId";
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
        BackButton.Visibility = _showingList && _settingsFor is not null ? Visibility.Visible : Visibility.Collapsed;

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

    // A plugin list row's text: the name with an optional small tag beside it, and an optional line underneath.
    private static StackPanel CreateNameBlock(TextBlock name, string? badge, string? detail, string detailBrush)
    {
        var nameLine = new StackPanel { Orientation = Orientation.Horizontal };
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
