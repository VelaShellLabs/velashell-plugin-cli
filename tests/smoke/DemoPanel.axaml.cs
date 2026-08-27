using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using VelaShell.PluginSdk;

namespace Smoke;

/// <summary>面板视图。同时验证 Avalonia 与契约 SDK 在插件工程里都编译期可见。</summary>
public sealed partial class DemoPanel : UserControl
{
    private readonly IPluginContext _context;

    /// <summary>由 <c>ShowPanelAsync</c> 的工厂在 UI 线程构造。</summary>
    public DemoPanel(IPluginContext context)
    {
        _context = context;
        InitializeComponent();
        CountSessionsButton.Click += OnCountSessionsAsync;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private async void OnCountSessionsAsync(object? sender, RoutedEventArgs e)
    {
        try
        {
            var sessions = await _context.Sessions.ListAsync(_context.Shutdown).ConfigureAwait(true);
            StatusText.Text = $"{sessions.Count} session(s).";
        }
        catch (Exception ex)
        {
            _context.Log.Error("Counting sessions failed.", ex);
            StatusText.Text = "Failed - see the plugin log.";
        }
    }
}
