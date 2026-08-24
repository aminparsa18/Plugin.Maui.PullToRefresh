using Microsoft.Extensions.DependencyInjection;

namespace Plugin.Maui.PullToRefresh.Example;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell());
	}
}