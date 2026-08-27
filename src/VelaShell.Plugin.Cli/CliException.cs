namespace VelaShell.Plugin.Cli;

/// <summary>
/// 可读的用法错误。与 <c>VpxFormatException</c> / <c>PluginManifestException</c> 同等对待:
/// <c>Main</c> 只打印消息、返回 1,不打印堆栈 —— 用错命令的人不需要看调用栈。
/// </summary>
/// <param name="message">给人看的错误消息。</param>
internal sealed class CliException(string message) : Exception(message);
