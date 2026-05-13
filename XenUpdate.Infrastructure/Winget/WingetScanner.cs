using XenUpdate.Core.Interfaces;
using XenUpdate.Core.Models;

namespace XenUpdate.Infrastructure.Winget;

/// <summary>
/// Orchestrates the winget update scanning pipeline:
///   <see cref="ProcessRunner"/> → <see cref="WingetOutputParser"/> → blacklist filter → result.
///
/// This class is the only place that knows about how winget is invoked.
/// It delegates parsing to <see cref="WingetOutputParser"/> and filtering to
/// <see cref="IBlacklistRepository"/>, keeping each concern separate.
/// </summary>
public sealed class WingetScanner : IWingetScanner
{
    /// <summary>
    /// The winget command arguments used to list upgradeable packages.
    /// --accept-source-agreements: prevents interactive license prompts.
    /// </summary>
    private const string WingetArguments = "upgrade --accept-source-agreements";

    private readonly ProcessRunner _processRunner;
    private readonly WingetOutputParser _parser;
    private readonly IBlacklistRepository _blacklistRepository;
    private readonly ILoggerService _logger;

    /// <summary>
    /// Initializes the scanner with its pipeline dependencies.
    /// All are injected — no new() inside this class.
    /// </summary>
    public WingetScanner(
        ProcessRunner processRunner,
        WingetOutputParser parser,
        IBlacklistRepository blacklistRepository,
        ILoggerService logger)
    {
        _processRunner = processRunner;
        _parser        = parser;
        _blacklistRepository = blacklistRepository;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Pipeline:
    /// 1. Run winget upgrade and capture stdout.
    /// 2. Pass raw output to <see cref="WingetOutputParser"/>.
    /// 3. Load blacklisted IDs and filter them out.
    /// 4. Return the final list.
    /// </remarks>
    public async Task<IReadOnlyList<AppUpdateItem>> GetAvailableUpdatesAsync(CancellationToken cancellationToken)
    {
        _logger.Info("Winget scan started.");
        _logger.Info($"Running: winget {WingetArguments}");

        // Step 1: Run winget.
        ProcessExecutionResult result;
        try
        {
            result = await _processRunner.RunAsync("winget", WingetArguments, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.Info("Winget scan was cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to start winget. Make sure winget is installed and accessible.", ex);
            return Array.Empty<AppUpdateItem>();
        }

        // Step 2: Log the outcome of the winget process.
        if (!result.Succeeded)
        {
            // winget returns exit code 0 even when updates are found,
            // but non-zero may indicate no winget or a source error.
            _logger.Warning($"Winget exited with code {result.ExitCode}. Output may be partial.");

            if (!string.IsNullOrWhiteSpace(result.StandardError))
                _logger.Warning($"Winget stderr: {result.StandardError.Trim()}");
        }

        // Step 3: Parse the raw output into update items.
        var parsed = _parser.Parse(result.StandardOutput);
        _logger.Info($"Winget parse complete. Raw items found: {parsed.Count}.");

        // Step 4: Apply blacklist filter.
        var blacklistedIds = await _blacklistRepository.GetBlacklistedIdsAsync();

        var filtered = parsed
            .Where(item => !blacklistedIds.Any(b =>
                string.Equals(b, item.WingetPackageId, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        int removedByBlacklist = parsed.Count - filtered.Count;
        if (removedByBlacklist > 0)
            _logger.Info($"Blacklist filtered {removedByBlacklist} item(s).");

        _logger.Info($"Scan complete. {filtered.Count} update(s) available.");
        return filtered;
    }
}
