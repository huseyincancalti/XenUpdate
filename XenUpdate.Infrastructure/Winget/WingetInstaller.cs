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
        return $"upgrade --id \"{escapedPackageId}\" --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity";
    }

    /// <inheritdoc />
    public async Task<bool> InstallUpdateAsync(
        AppUpdateItem item,
        IProgress<int> progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(item.WingetPackageId))
        {
            _logger.Warning($"Skipped {item.DisplayName} because it does not have a winget package ID.");
            return false;
        }

        var arguments = BuildUpgradeArguments(item.WingetPackageId);
        _logger.Info($"Install started for {item.DisplayName} ({item.WingetPackageId}).");
        _logger.Info($"Running: winget {arguments}");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(InstallTimeout);

        ProcessExecutionResult result;
        try
        {
            result = await _processRunner.RunAsync("winget", arguments, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.Warning($"Install timed out for {item.DisplayName} ({item.WingetPackageId}) after {InstallTimeout.TotalMinutes:0} minutes.");
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
            return false;
        }

        return LogAndReturnInstallResult(item, result, progress);
    }

    private bool LogAndReturnInstallResult(
        AppUpdateItem item,
        ProcessExecutionResult result,
        IProgress<int> progress)
    {
        if (result.Succeeded)
        {
            progress.Report(100);
            _logger.Info($"Install completed successfully for {item.DisplayName} ({item.WingetPackageId}). Exit code: {result.ExitCode}.");
            return true;
        }

        _logger.Warning($"Install failed for {item.DisplayName} ({item.WingetPackageId}). Exit code: {result.ExitCode}.");

        var stderrSummary = BuildOutputSummary(result.StandardError);
        if (!string.IsNullOrWhiteSpace(stderrSummary))
        {
            _logger.Warning($"Winget stderr summary for {item.WingetPackageId}: {stderrSummary}");
        }

        return false;
    }

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
