using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Plugin.Maui.PullToRefresh.Example;

/// <summary>
/// Demonstrates that PullToRefreshView isn't limited to CollectionView/ScrollView: this page wraps
/// a plain Grid of stat tiles directly. There's no scrollable descendant at all, so the handler
/// falls back to treating the content as always "at rest" — a downward drag from anywhere in the
/// tile grid starts a pull immediately.
/// </summary>
public partial class DashboardPage : ContentPage
{
    public DashboardPage()
    {
        InitializeComponent();
        BindingContext = new DashboardPageViewModel();
    }
}

/// <summary>Minimal ViewModel — same lightweight pattern as MainPageViewModel, no MVVM Toolkit.</summary>
public class DashboardPageViewModel : INotifyPropertyChanged
{
    private static readonly Random Rng = new();

    private string _statusText = "Pull down to refresh";
    private string _activeSessions = "1,284";
    private string _avgLatency = "112ms";
    private string _uptime = "99.98%";
    private string _errorRate = "0.03%";

    public ICommand RefreshCommand { get; }

    public string StatusText
    {
        get => _statusText;
        private set { _statusText = value; OnPropertyChanged(); }
    }

    public string ActiveSessions
    {
        get => _activeSessions;
        private set { _activeSessions = value; OnPropertyChanged(); }
    }

    public string AvgLatency
    {
        get => _avgLatency;
        private set { _avgLatency = value; OnPropertyChanged(); }
    }

    public string Uptime
    {
        get => _uptime;
        private set { _uptime = value; OnPropertyChanged(); }
    }

    public string ErrorRate
    {
        get => _errorRate;
        private set { _errorRate = value; OnPropertyChanged(); }
    }

    public DashboardPageViewModel()
    {
        RefreshCommand = new Command(async () => await RefreshAsync());
    }

    private async Task RefreshAsync()
    {
        StatusText = "Refreshing…";

        // Simulate a network call.
        await Task.Delay(800);

        ActiveSessions = $"{Rng.Next(900, 2000):N0}";
        AvgLatency = $"{Rng.Next(60, 180)}ms";
        Uptime = $"{99.90 + Rng.NextDouble() * 0.09:F2}%";
        ErrorRate = $"{Rng.NextDouble() * 0.1:F2}%";

        StatusText = $"Last refreshed: {DateTime.Now:T}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
