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

                if (await EnsurePreferredBinaryAsync(requestedVersion, cancellationToken).ConfigureAwait(false))
                {
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
                if (await EnsurePreferredBinaryAsync(version: null, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                await RunProcessAsync(BinaryPath, "-U", cancellationToken).ConfigureAwait(false);
                // Self-update can replace the file contents, so verify Linux still has the standalone binary afterwards.
                if (await EnsurePreferredBinaryAsync(version: null, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                TryExportToPath();
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

        TryExportToPath();
    }

    private static string GetDownloadUrl(string? version)
    {
        var assetName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) switch
        {
            true => "yt-dlp.exe",
            false when RuntimeInformation.IsOSPlatform(OSPlatform.Linux) => "yt-dlp_linux",
            _ => "yt-dlp"
        };
        return string.IsNullOrWhiteSpace(version)
            ? $"https://github.com/yt-dlp/yt-dlp/releases/latest/download/{assetName}"
            : $"https://github.com/yt-dlp/yt-dlp/releases/download/{version}/{assetName}";
    }

    private async Task<bool> EnsurePreferredBinaryAsync(string? version, CancellationToken cancellationToken)
    {
        if (IsPreferredInstalledBinary())
        {
            return false;
        }

        await DownloadBinaryAsync(version, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private bool IsPreferredInstalledBinary()
    {
        if (!File.Exists(BinaryPath))
        {
            return false;
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return true;
        }

        try
        {
            using var stream = new FileStream(BinaryPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            Span<byte> header = stackalloc byte[4];
            stream.ReadExactly(header);
            return header[0] == 0x7F
                && header[1] == (byte)'E'
                && header[2] == (byte)'L'
                && header[3] == (byte)'F';
        }
        catch (EndOfStreamException ex)
        {
            logger.LogWarning(ex, "The installed yt-dlp binary at {BinaryPath} is incomplete or corrupted", BinaryPath);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to inspect the installed yt-dlp binary at {BinaryPath}", BinaryPath);
            return false;
        }
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

    private void TryExportToPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            TryAddToWindowsPath();
        }
        else
        {
            TryCreateUnixSymlink();
        }
    }

    private void TryCreateUnixSymlink()
    {
        const string symlinkPath = "/usr/local/bin/yt-dlp";
        try
        {
            var existingInfo = new FileInfo(symlinkPath);
            if (existingInfo.Exists || Path.Exists(symlinkPath))
            {
                var linkTarget = existingInfo.ResolveLinkTarget(returnFinalTarget: false)?.FullName;
                if (string.Equals(linkTarget, BinaryPath, StringComparison.Ordinal))
                {
                    return;
                }

                if (linkTarget is null)
                {
                    logger.LogWarning(
                        "A file already exists at {SymlinkPath} and is not a symlink managed by this plugin. Skipping PATH export.",
                        symlinkPath);
                    return;
                }

                File.Delete(symlinkPath);
            }

            File.CreateSymbolicLink(symlinkPath, BinaryPath);
            logger.LogInformation("Created symlink {SymlinkPath} → {BinaryPath} so yt-dlp is available on PATH", symlinkPath, BinaryPath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Could not create symlink at {SymlinkPath}. To run yt-dlp without the full path, either run Jellyfin with elevated privileges or add {BinaryDir} to your PATH manually.",
                symlinkPath,
                _binaryDirectoryPath);
        }
    }

    private void TryAddToWindowsPath()
    {
        try
        {
            var normalizedBinaryDir = Path.GetFullPath(_binaryDirectoryPath);
            var machinePath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) ?? string.Empty;
            var entries = machinePath.Split(';', StringSplitOptions.RemoveEmptyEntries);
            if (entries.Any(e => string.Equals(Path.GetFullPath(e), normalizedBinaryDir, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            var newPath = string.Join(';', entries.Append(normalizedBinaryDir));
            Environment.SetEnvironmentVariable("PATH", newPath, EnvironmentVariableTarget.Machine);
            logger.LogInformation("Added {BinaryDir} to the system PATH so yt-dlp is available in any shell", normalizedBinaryDir);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Could not add {BinaryDir} to the system PATH. To run yt-dlp without the full path, add that directory to your PATH manually.",
                _binaryDirectoryPath);
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
