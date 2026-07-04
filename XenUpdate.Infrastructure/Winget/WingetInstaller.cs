using XenUpdate.Core.Interfaces;
using XenUpdate.Core.Models;

namespace XenUpdate.Infrastructure.Winget;

/// <summary>
/// Installs a single application update using the winget package manager.
/// Implements <see cref="IWingetInstaller"/>.
/// </summary>
public sealed class WingetInstaller : IWingetInstaller
{
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(10);

    private readonly ProcessRunner _processRunner;
    private readonly ILoggerService _logger;

    /// <summary>
    /// Initializes a new <see cref="WingetInstaller"/> with its required dependencies.
    /// </summary>
    public WingetInstaller(ProcessRunner processRunner, ILoggerService logger)
    {
        _processRunner = processRunner;
        _logger = logger;
    }

    /// <summary>
    /// Builds the winget arguments used to update a single package by ID.
    /// </summary>
    /// <param name="packageId">The exact winget package ID.</param>
    /// <returns>A command-line argument string for <c>winget upgrade</c>.</returns>
    public static string BuildUpgradeArguments(string packageId)
    {
        var escapedPackageId = packageId.Replace("\"", string.Empty);
        // Note: we deliberately do NOT pass --disable-interactivity. That flag also suppresses
        // winget's download progress (the "12.4 MB / 84.0 MB" lines we parse for the live size).
        // --silent keeps the *installer* quiet and the --accept-* flags pre-answer the only
        // prompts an --exact --id upgrade can raise, so dropping it does not risk a hang.
        return $"upgrade --id \"{escapedPackageId}\" --exact --silent --accept-package-agreements --accept-source-agreements";
    }

    // winget package ids are vendor.product style — letters, digits, '.', '-', '_', '+'.
    // Reject anything else so a malformed/hostile id can't inject extra winget arguments.
    private static bool IsSafePackageId(string packageId) =>
        packageId.All(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' or '+');

    /// <inheritdoc />
    public async Task<bool> InstallUpdateAsync(
        AppUpdateItem item,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(item.WingetPackageId))
        {
            _logger.Warning($"Skipped {item.DisplayName} because it does not have a winget package ID.");
            return false;
        }

        if (!IsSafePackageId(item.WingetPackageId))
        {
            _logger.Warning($"Skipped {item.DisplayName}: package id '{item.WingetPackageId}' has unexpected characters.");
            return false;
        }

        var arguments = BuildUpgradeArguments(item.WingetPackageId);
        _logger.Info($"Install started for {item.DisplayName} ({item.WingetPackageId}).");
        _logger.Info($"Running: winget {arguments}");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(InstallTimeout);

        // winget redraws its download/install progress with carriage returns. Reading stdout
        // line-by-line surfaces each redraw, so we can report the live "downloaded / total"
        // size and percentage. If winget emits no such lines (some silent installers), the
        // progress simply stays indeterminate until the final 100% — no regression.
        var lineProgress = new Progress<string>(line => ReportLine(line, progress));

        ProcessExecutionResult result;
        try
        {
            result = await _processRunner.RunAsync("winget", arguments, timeoutCts.Token, lineProgress);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.Warning($"Install timed out for {item.DisplayName} ({item.WingetPackageId}) after {InstallTimeout.TotalMinutes:0} minutes.");
            progress.Report(new InstallProgress(0, FailureReason: $"Timed out after {InstallTimeout.TotalMinutes:0} minutes"));
            return false;
        }
        catch (OperationCanceledException)
        {
            _logger.Info($"Install cancelled for {item.DisplayName} ({item.WingetPackageId}).");
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to start winget for {item.DisplayName} ({item.WingetPackageId}).", ex);
            progress.Report(new InstallProgress(0, FailureReason: "Could not start winget — is the App Installer package present?"));
            return false;
        }

        return LogAndReturnInstallResult(item, result, progress);
    }

    // Parses one stdout line and forwards a progress update when it carries a size or percent.
    private static void ReportLine(string line, IProgress<InstallProgress> progress)
    {
        var downloadText = WingetProgressParser.TryParseDownload(line, out var size) ? size : null;
        var percent = WingetProgressParser.TryParsePercent(line, out var value) ? value : -1;

        if (downloadText is not null || percent >= 0)
        {
            progress.Report(new InstallProgress(percent, downloadText));
        }
    }

    private bool LogAndReturnInstallResult(
        AppUpdateItem item,
        ProcessExecutionResult result,
        IProgress<InstallProgress> progress)
    {
        if (result.Succeeded)
        {
            progress.Report(new InstallProgress(100));
            _logger.Info($"Install completed successfully for {item.DisplayName} ({item.WingetPackageId}). Exit code: {result.ExitCode}.");
            return true;
        }

        _logger.Warning($"Install failed for {item.DisplayName} ({item.WingetPackageId}). Exit code: {result.ExitCode}.");

        var stderrSummary = BuildOutputSummary(result.StandardError);
        if (!string.IsNullOrWhiteSpace(stderrSummary))
        {
            _logger.Warning($"Winget stderr summary for {item.WingetPackageId}: {stderrSummary}");
        }

        var failureReason = MapWingetExitCode(result.ExitCode);
        progress.Report(new InstallProgress(0, FailureReason: failureReason));

        return false;
    }

    // Maps known winget and Windows INTERNET_* HRESULTs to human-readable strings.
    // Covers the most common real-world failures: network issues, policy blocks, hash errors.
    private static string MapWingetExitCode(int exitCode) => exitCode switch
    {
        -2147012889 => "No internet — server could not be reached",
        -2147012867 => "Connection timed out",
        -2147012895 => "Connection refused by server",
        -2147012894 => "Connection dropped",
        -2147012696 => "Secure connection failed (TLS/SSL error)",
        -1978335220 => "No applicable installer for this system",
        -1978335191 => "Package is already up to date",
        -1978335215 => "Install blocked by policy or security software",
        -1978335212 => "Installer hash mismatch — download may be corrupt",
        -1978335210 => "Installer returned an error",
        _ => $"Install failed (exit code {exitCode})"
    };

    private static string BuildOutputSummary(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return string.Empty;
        }

        var singleLine = output
            .Replace(Environment.NewLine, " ")
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();

        const int maxLength = 240;
        return singleLine.Length <= maxLength
            ? singleLine
            : singleLine[..maxLength] + "...";
    }
}
