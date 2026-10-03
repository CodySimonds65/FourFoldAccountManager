using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Plugins;
using FourFoldAccountManager.Desktop.Plugins;

namespace FourFoldAccountManager.Desktop.Views;

// Hosts the plugin strip and the one page beside it: a plugin's panel, the plugin list, or a plugin's settings.
// It only renders the saved settings and reports what the user asked for; MainWindow saves and re-renders.
public partial class PluginSidebar : UserControl
{
    private const string IconFont = "Segoe Fluent Icons, Segoe MDL2 Assets";
    private IReadOnlyList<IFourFoldPlugin> _plugins = [];
    private PanelSettings _settings = PanelSettings.Default;
    private bool _showingList;
    private IFourFoldPlugin? _settingsFor;
    private IFourFoldPlugin? _lastShown;
    private bool _stayOpen;
    private Point _pressPoint;
    private string? _pressedId;

    public PluginSidebar()
    {
        InitializeComponent();
        PluginListButton.Content = CreateIconContent("", "Plugins");
    }

    public event Action<string>? OpenRequested;
    public event Action? CloseRequested;
    public event Action<string, int>? MoveRequested;
    public event Action<string, bool>? EnabledChangeRequested;

    // In the tools window the panel never closes, since a window that's just a strip would be odd.
    public bool StayOpen
    {
        set
        {
            _stayOpen = value;
            Render(_settings);
        }
    }

    public void SetPlugins(IReadOnlyList<IFourFoldPlugin> plugins)
    {
        _plugins = plugins;
        Render(_settings);
    }

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
                Content = CreateIconContent(descriptor.Icon, descriptor.ShortLabel),
                ToolTip = descriptor.Name,
                Tag = ReferenceEquals(plugin, shown) ? "lit" : null,
                DataContext = descriptor.Id
            };
            AutomationProperties.SetName(button, descriptor.Name);
            button.Click += (_, _) => PluginIcon_Click(descriptor.Id);
            button.PreviewMouseLeftButtonDown += StripIcon_PreviewMouseLeftButtonDown;
            button.PreviewMouseMove += StripIcon_PreviewMouseMove;
            StripIcons.Children.Add(button);
        }

        PluginListButton.Tag = _showingList ? "lit" : null;
    }

    private void RenderPage(IFourFoldPlugin? shown)
    {
        var visible = _showingList || shown is not null;
        PanelArea.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        PanelColumn.Width = new GridLength(visible ? 250 : 0);
        PanelGapColumn.Width = new GridLength(visible ? 6 : 0);
        BackButton.Visibility = _showingList && _settingsFor is not null ? Visibility.Visible : Visibility.Collapsed;

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
        else if (shown is not null)
        {
            KickerText.Text = "PLUGIN";
            TitleText.Text = shown.Descriptor.Name;
            PageHost.Content = shown.Panel;
        }
        else
        {
            PageHost.Content = null;
        }

        if (shown is not null && !ReferenceEquals(shown, _lastShown))
        {
            shown.Opened();
        }

        _lastShown = shown;
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
            row.Children.Add(name);

            if (plugin.SettingsPage is not null)
            {
                var cog = new Button
                {
                    Content = new TextBlock { Text = "", FontFamily = new FontFamily(IconFont), FontSize = 13 },
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

        var note = new TextBlock
        {
            Text = "Drag icons in the strip to reorder them.",
            FontSize = 11,
            Margin = new Thickness(2, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        note.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        list.Children.Add(note);
        return new ScrollViewer
        {
            Content = list,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
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
        if (_pressedId is not { } id || e.LeftButton != MouseButtonState.Pressed)
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
        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(typeof(string), id), DragDropEffects.Move);
    }

    private void StripIcons_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(string)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    // The new index counts the other icons above the drop point, so dropping an icon on itself changes nothing.
    private void StripIcons_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(string)) is not string id)
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
