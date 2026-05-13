using MediaBrowser.Model.Plugins;

namespace YtDlpJellyfin.Plugin;

public sealed class PluginConfiguration : BasePluginConfiguration
{
    public UpdateMode UpdateMode { get; set; } = UpdateMode.Latest;

    public int UpdateIntervalHours { get; set; } = 24;

    public string SpecificVersion { get; set; } = string.Empty;

    public DateTime LastCheckedUtc { get; set; } = DateTime.MinValue;
}

public enum UpdateMode
{
    Disabled = 0,
    Latest = 1,
    SpecificVersion = 2
}
