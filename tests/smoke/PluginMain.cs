using VelaShell.PluginSdk;
using VelaShell.PluginSdk.Ui;

namespace Smoke;

/// <summary>
/// 冒烟夹具的插件入口。刻意与 velaplugin-ui 模板同形 —— 它要覆盖的是同一批链路:
/// 契约程序集可见、命令注册、面板工厂、以及编译期 AXAML。
/// </summary>
[VelaPlugin]
public sealed class SmokePlugin : IVelaPlugin
{
    private IPluginContext? _context;

    /// <inheritdoc />
    public Task ActivateAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        _context = context;
        context.Log.Info("Smoke activated.");
        context.Commands.Register(new(
            $"{context.PluginId}.open-panel",
            "Smoke: Open panel",
            "Smoke",
            OpenPanelAsync));
        return Task.CompletedTask;
    }

    private async Task OpenPanelAsync(CancellationToken cancellationToken)
    {
        IPluginContext context = _context!;
        await context.Ui.ShowPanelAsync(
            new() { Title = "Smoke", DisplayMode = PanelDisplayMode.Document },
            () => new DemoPanel(context),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task DeactivateAsync(CancellationToken cancellationToken)
    {
        _context = null;
        return Task.CompletedTask;
    }
}
