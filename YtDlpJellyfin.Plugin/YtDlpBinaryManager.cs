using System.Diagnostics;
using System.Runtime.InteropServices;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace YtDlpJellyfin.Plugin;

public sealed class YtDlpBinaryManager(IApplicationPaths applicationPaths, ILogger logger) : IDisposable
{
    private readonly HttpClient _httpClient = new();
    private readonly string _binaryDirectoryPath = Path.Combine(applicationPaths.ProgramDataPath, "plugins", "ytdlp-jelly", "bin");

    public string BinaryPath => Path.Combine(_binaryDirectoryPath, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "yt-dlp.exe" : "yt-dlp");

    public async Task EnsureConfiguredVersionAsync(PluginConfiguration configuration, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_binaryDirectoryPath);

        switch (configuration.UpdateMode)
        {
            case UpdateMode.Disabled:
                return;
            case UpdateMode.SpecificVersion:
            {
                var requestedVersion = configuration.SpecificVersion.Trim();
                if (string.IsNullOrWhiteSpace(requestedVersion))
                {
                    logger.LogWarning("Specific yt-dlp version mode is configured, but no version is set.");
                    return;
                }

                var currentVersion = await GetInstalledVersionAsync(cancellationToken).ConfigureAwait(false);
                if (!string.Equals(currentVersion, requestedVersion, StringComparison.OrdinalIgnoreCase))
                {
                    await DownloadBinaryAsync(requestedVersion, cancellationToken).ConfigureAwait(false);
                }

                return;
            }
            case UpdateMode.Latest:
                if (!File.Exists(BinaryPath))
                {
                    await DownloadBinaryAsync(version: null, cancellationToken).ConfigureAwait(false);
                    return;
                }

                await RunProcessAsync(BinaryPath, "-U", cancellationToken).ConfigureAwait(false);
                return;
            default:
                return;
        }
    }

    private async Task<string?> GetInstalledVersionAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(BinaryPath))
        {
            return null;
        }

        var output = await RunProcessAsync(BinaryPath, "--version", cancellationToken).ConfigureAwait(false);
        return output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
    }

    private async Task DownloadBinaryAsync(string? version, CancellationToken cancellationToken)
    {
        var downloadUrl = GetDownloadUrl(version);
        logger.LogInformation("Downloading yt-dlp from {Url}", downloadUrl);

        await using var stream = await _httpClient.GetStreamAsync(downloadUrl, cancellationToken).ConfigureAwait(false);
        var tempPath = $"{BinaryPath}.tmp";

        try
        {
            await using var fileStream = File.Create(tempPath);
            await stream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, BinaryPath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            throw;
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            File.SetUnixFileMode(BinaryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute);
        }
    }

    private static string GetDownloadUrl(string? version)
    {
        var assetName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "yt-dlp.exe" : "yt-dlp";
        return string.IsNullOrWhiteSpace(version)
            ? $"https://github.com/yt-dlp/yt-dlp/releases/latest/download/{assetName}"
            : $"https://github.com/yt-dlp/yt-dlp/releases/download/{version}/{assetName}";
    }

    private async Task<string> RunProcessAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = processStartInfo };
        process.Start();

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await Task.WhenAll(standardOutputTask, standardErrorTask, process.WaitForExitAsync(cancellationToken)).ConfigureAwait(false);

        var output = await standardOutputTask.ConfigureAwait(false);
        var error = await standardErrorTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Command '{fileName} {arguments}' failed with code {process.ExitCode}: {error}");
        }

        return output;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
