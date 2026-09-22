using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FourFoldAccountManager.Desktop.Services;
using FourFoldAccountManager.Desktop.Views;

namespace FourFoldAccountManager.Desktop.Tests;

public sealed class XpTrackerRowAndPanelTests
{
    [Theory]
    [InlineData(0.001, "<1m time to next level")]
    [InlineData(1d / 60d, "1m time to next level")]
    [InlineData(1.01d / 60d, "2m time to next level")]
    [InlineData(1.5, "1h 30m time to next level")]
    [InlineData(25.25, "1d 1h time to next level")]
    public void From_state_formats_time_until_next_level_rounded_up_to_minutes(double hours, string expected)
    {
        var row = XpTrackerRow.FromState(1, "Account", State(hours));

        Assert.Equal(expected, row.TimeUntilNextLevelText);
    }

    [Fact]
    public void From_state_displays_unknown_time_until_next_level()
    {
        var row = XpTrackerRow.FromState(1, "Account", State(null));

        Assert.Equal("— time to next level", row.TimeUntilNextLevelText);
    }

    [Fact]
    public void Reset_rate_menu_raises_event_for_the_placement_target_row()
    {
        RunOnStaThread(() =>
        {
            var panel = CreatePanelForMenuHandlerTest();
            var row = XpTrackerRow.FromState(1, "Account", State(1));
            var target = new Border { DataContext = row };
            var menu = new ContextMenu { PlacementTarget = target };
            var item = new MenuItem();
            menu.Items.Add(item);
            Guid? requested = null;
            panel.ResetRateRequested += accountId => requested = accountId;

            InvokeMenuHandler(panel, "ResetRate_Click", item);

            Assert.Equal(row.AccountId, requested);
        });
    }

    [Fact]
    public void Reset_all_menu_ignores_a_placement_target_without_an_xp_tracker_row()
    {
        RunOnStaThread(() =>
        {
            var panel = CreatePanelForMenuHandlerTest();
            var menu = new ContextMenu { PlacementTarget = new Border { DataContext = new object() } };
            var item = new MenuItem();
            menu.Items.Add(item);
            var raised = false;
            panel.ResetAllRequested += _ => raised = true;

            InvokeMenuHandler(panel, "ResetAll_Click", item);

            Assert.False(raised);
        });
    }

    [Fact]
    public void Rendered_row_context_menu_has_reset_items_and_routes_the_clicked_row()
    {
        RunOnStaThread(() =>
        {
            var application = new FourFoldAccountManager.Desktop.App();
            application.InitializeComponent();
            var first = XpTrackerRow.FromState(1, "First", State(1));
            var second = XpTrackerRow.FromState(2, "Second", State(2));
            var panel = new XpTrackerPanel { ItemsSource = new[] { first, second } };
            var window = new Window { Content = panel };
            try
            {
                window.Show();
                panel.UpdateLayout();
                var rowBorder = FindVisualDescendant<Border>(panel, border =>
                    ReferenceEquals(border.DataContext, second) && border.ContextMenu is not null);
                Assert.NotNull(rowBorder);

                var menu = rowBorder!.ContextMenu!;
                var items = menu.Items.OfType<MenuItem>().ToArray();
                Assert.Collection(items,
                    item => Assert.Equal("Reset XP/hr", item.Header),
                    item => Assert.Equal("Reset all", item.Header));

                Guid? requested = null;
                panel.ResetRateRequested += accountId => requested = accountId;
                menu.PlacementTarget = rowBorder;
                items[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

                Assert.Equal(second.AccountId, requested);
            }
            finally
            {
                window.Close();
                application.Shutdown();
            }
        });
    }

    private static XpTrackerState State(double? hoursUntilNextLevel) => new(
        Guid.NewGuid(), "Player", 1_000, 200, "Scout", 800, hoursUntilNextLevel,
        DateTimeOffset.UtcNow, "Tracking", false);

    private static void InvokeMenuHandler(XpTrackerPanel panel, string methodName, MenuItem item) =>
        typeof(XpTrackerPanel).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(panel, [item, new RoutedEventArgs()]);

    private static XpTrackerPanel CreatePanelForMenuHandlerTest() =>
        (XpTrackerPanel)RuntimeHelpers.GetUninitializedObject(typeof(XpTrackerPanel));

    private static T? FindVisualDescendant<T>(DependencyObject parent, Func<T, bool> predicate)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T typed && predicate(typed)) return typed;

            if (FindVisualDescendant(child, predicate) is { } match) return match;
        }

        return null;
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new TargetInvocationException(failure);
    }
}
