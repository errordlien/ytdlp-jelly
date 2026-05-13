using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace YtDlpJellyfin.Plugin;

public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    private static readonly Guid PluginGuid = new("8ea615e2-ba93-40a7-a44d-b53dbe985058");
    private readonly ILogger<Plugin> _logger;
    private readonly SemaphoreSlim _updateLock = new(1, 1);
    private readonly YtDlpBinaryManager _binaryManager;
    private readonly Timer _timer;

    public static Plugin? Instance { get; private set; }

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, ILogger<Plugin> logger)
        : base(applicationPaths, xmlSerializer)
    {
        _logger = logger;
        _binaryManager = new YtDlpBinaryManager(applicationPaths, logger);
        Instance = this;
        _timer = new Timer(TimerCallback, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public override string Name => "yt-dlp manager";

    public override Guid Id => PluginGuid;

    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = "ytdlpjellyfin",
            EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html"
        };
    }

    public void Dispose()
    {
        _timer.Dispose();
        _binaryManager.Dispose();
        _updateLock.Dispose();
    }

    private void TimerCallback(object? state)
    {
        _ = CheckForUpdateAsync();
    }

    private async Task CheckForUpdateAsync()
    {
        if (!await _updateLock.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            var configuration = Configuration;
            if (configuration.UpdateMode == UpdateMode.Disabled)
            {
                return;
            }

            var intervalHours = Math.Max(1, configuration.UpdateIntervalHours);
            var isDue = DateTime.UtcNow >= configuration.LastCheckedUtc.AddHours(intervalHours);
            if (!isDue)
            {
                return;
            }

            await _binaryManager.EnsureConfiguredVersionAsync(configuration, CancellationToken.None).ConfigureAwait(false);
            configuration.LastCheckedUtc = DateTime.UtcNow;
            SaveConfiguration();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed while checking or updating yt-dlp");
        }
        finally
        {
            _updateLock.Release();
        }
    }
}
