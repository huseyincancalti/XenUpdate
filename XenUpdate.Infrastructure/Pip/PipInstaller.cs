using XenUpdate.Core.Interfaces;
using XenUpdate.Core.Models;
using XenUpdate.Infrastructure.Winget;

namespace XenUpdate.Infrastructure.Pip;

/// <summary>
/// Installs a single Python package update using pip.
/// Implements <see cref="IPipInstaller"/>.
/// </summary>
public sealed class PipInstaller : IPipInstaller
{
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(10);

    private readonly ProcessRunner _processRunner;
    private readonly ILoggerService _logger;

    /// <summary>
    /// Initializes a new <see cref="PipInstaller"/> with its required dependencies.
    /// </summary>
    public PipInstaller(ProcessRunner processRunner, ILoggerService logger)
    {
        _processRunner = processRunner;
        _logger = logger;
    }

    /// <summary>Builds the pip arguments used to upgrade a single package by name.</summary>
    public static string BuildUpgradeArguments(string packageName) =>
        $"install --upgrade \"{packageName}\"";

    // PyPI package names are letters, digits, '.', '-', '_' (PEP 508). Reject anything else
    // so a malformed/hostile name can't inject extra pip arguments — same posture as
    // WingetInstaller.IsSafePackageId.
    private static bool IsSafePackageName(string packageName) =>
        packageName.All(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_');

    /// <inheritdoc />
    public async Task<bool> InstallUpdateAsync(
        PipPackageItem item,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(item.PackageName))
        {
            _logger.Warning($"Skipped {item.DisplayName} because it does not have a pip package name.");
            return false;
        }

        if (!IsSafePackageName(item.PackageName))
        {
            _logger.Warning($"Skipped {item.DisplayName}: package name '{item.PackageName}' has unexpected characters.");
            return false;
        }

        var arguments = BuildUpgradeArguments(item.PackageName);
        _logger.Info($"Install started for {item.DisplayName} ({item.PackageName}).");
        _logger.Info($"Running: pip {arguments}");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(InstallTimeout);

        progress.Report(new InstallProgress(0));

        ProcessExecutionResult result;
        try
        {
            result = await _processRunner.RunAsync("pip", arguments, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.Warning($"Install timed out for {item.DisplayName} ({item.PackageName}) after {InstallTimeout.TotalMinutes:0} minutes.");
            progress.Report(new InstallProgress(0, FailureReason: $"Timed out after {InstallTimeout.TotalMinutes:0} minutes"));
            return false;
        }
        catch (OperationCanceledException)
        {
            _logger.Info($"Install cancelled for {item.DisplayName} ({item.PackageName}).");
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to start pip for {item.DisplayName} ({item.PackageName}).", ex);
            progress.Report(new InstallProgress(0, FailureReason: "Could not start pip — is Python installed and on PATH?"));
            return false;
        }

        return LogAndReturnInstallResult(item, result, progress);
    }

    private bool LogAndReturnInstallResult(
        PipPackageItem item,
        ProcessExecutionResult result,
        IProgress<InstallProgress> progress)
    {
        if (result.Succeeded)
        {
            progress.Report(new InstallProgress(100));
            _logger.Info($"Install completed successfully for {item.DisplayName} ({item.PackageName}). Exit code: {result.ExitCode}.");
            return true;
        }

        _logger.Warning($"Install failed for {item.DisplayName} ({item.PackageName}). Exit code: {result.ExitCode}.");

        // pip's failures are open-ended free text (dependency conflicts, network errors,
        // permission errors, ...) — unlike winget's fixed, enumerable HRESULT set, there is
        // no small table of known codes to map and localize. The most honest, useful thing
        // is to surface pip's own last error line as-is (English, like any external tool's
        // diagnostic output) rather than force it through a localization key that doesn't fit.
        var reason = ExtractLastMeaningfulLine(result.StandardError) ?? $"pip exited with code {result.ExitCode}";
        progress.Report(new InstallProgress(0, FailureReason: reason));

        return false;
    }

    // Pip's stderr often ends with the actual "ERROR: ..." line after a longer traceback/log.
    // Walking from the end and preferring a line starting with "ERROR" gives the most useful
    // single line; otherwise falls back to the last non-blank line.
    private static string? ExtractLastMeaningfulLine(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return null;

        var lines = output
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        if (lines.Count == 0)
            return null;

        var errorLine = lines.LastOrDefault(l => l.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase));

        const int maxLength = 240;
        var chosen = errorLine ?? lines[^1];
        return chosen.Length <= maxLength ? chosen : chosen[..maxLength] + "...";
    }
}
