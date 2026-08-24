using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Plugin.Maui.PullToRefresh.Example;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
        BindingContext = new MainPageViewModel();
    }
}

/// <summary>A single row in the demo feed — enough fields to make a real-looking card list.</summary>
public class FeedItem
{
    public string Initials { get; init; } = "";
    public Color AccentColor { get; init; } = Colors.Gray;
    public string Sender { get; init; } = "";
    public string Subject { get; init; } = "";
    public string Preview { get; init; } = "";
    public string TimeLabel { get; init; } = "";
    public bool IsUnread { get; init; }
}

/// <summary>
/// Minimal ViewModel — enough to show PullToRefreshView driving a real refresh, without pulling
/// in MVVM Toolkit as a dependency for the sample.
/// </summary>
public class MainPageViewModel : INotifyPropertyChanged
{
    // Cycled across avatars so the list reads as a real feed rather than a single flat color.
    private static readonly Color[] Palette =
    [
        Color.FromArgb("#5B80C1"), // brand — matches the pull strip
        Color.FromArgb("#8B7FE0"),
        Color.FromArgb("#3FA9A0"),
        Color.FromArgb("#E0A339"),
        Color.FromArgb("#D9738F"),
    ];

    private static readonly (string Sender, string Subject, string Preview)[] Seed =
    [
        ("Ava Chen", "Release 2.4 notes", "Elastic curve tuning shipped for both platforms, changelog attached."),
        ("Build Bot", "iOS build #482 passed", "All 61 checks green. Artifact uploaded to TestFlight."),
        ("Marcus Liu", "Re: strip color theming", "Proposed a ThemeColor bindable property, PR incoming this week."),
        ("Design Team", "Curve easing review", "Comparing sqrt vs. cubic damping — recording attached."),
        ("Priya Nair", "Android touch listener fix", "Slop threshold bumped to 8dp, resolves the false-positive drags."),
        ("Sam Okafor", "Weekly standup notes", "Demo went well, next up: Mac Catalyst handler."),
        ("Build Bot", "Android build #217 passed", "Instrumented tests green, APK ready for review."),
        ("Ava Chen", "Docs pass on README", "Added usage snippet and platform support table."),
        ("Jonas Weber", "Question about ScrollView support", "Confirmed — auto-discovery walks the native tree either way."),
        ("Priya Nair", "Bezier tip-following demo", "Clamped to the middle 60% width, feels natural now."),
    ];

    private string _statusText = "Pull down to refresh";
    private int _refreshCount;

    public ObservableCollection<FeedItem> Items { get; } = new(
        Seed.Select((s, i) => new FeedItem
        {
            Initials = Initials(s.Sender),
            AccentColor = Palette[i % Palette.Length],
            Sender = s.Sender,
            Subject = s.Subject,
            Preview = s.Preview,
            TimeLabel = $"{9 + i / 2}:{(i % 2 == 0 ? "00" : "30")} AM",
            IsUnread = i < 3,
        }));

    public ICommand RefreshCommand { get; }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            _statusText = value;
            OnPropertyChanged();
        }
    }

    public MainPageViewModel()
    {
        RefreshCommand = new Command(async () => await RefreshAsync());
    }

    private async Task RefreshAsync()
    {
        _refreshCount++;
        StatusText = "Refreshing…";

        // Simulate a network call.
        await Task.Delay(800);

        Items.Insert(0, new FeedItem
        {
            Initials = "•",
            AccentColor = Palette[_refreshCount % Palette.Length],
            Sender = "Notifications",
            Subject = $"Synced refresh #{_refreshCount}",
            Preview = $"Pulled fresh data at {DateTime.Now:T}.",
            TimeLabel = "now",
            IsUnread = true,
        });
        StatusText = $"Last refreshed: {DateTime.Now:T}";
    }

    private static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2
            ? $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant()
            : name.Length > 0 ? name[..1].ToUpperInvariant() : "?";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
