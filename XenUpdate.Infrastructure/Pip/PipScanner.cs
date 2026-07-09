using XenUpdate.Core.Interfaces;
using XenUpdate.Core.Models;
using XenUpdate.Infrastructure.Winget;

namespace XenUpdate.Infrastructure.Pip;

/// <summary>
/// Orchestrates the pip update scanning pipeline:
///   <see cref="ProcessRunner"/> → <see cref="PipListOutputParser"/> → result.
///
/// This class is the only place that knows how pip is invoked. It calls the "pip" found
/// on PATH — the same minimal-resolution approach already used for "winget" — rather than
/// resolving a specific Python interpreter, keeping scope to the common single-interpreter
/// case for v1.
/// </summary>
public sealed class PipScanner : IPipScanner
{
    /// <summary>
    /// --format=json: gives a real, stable structure to parse instead of a plain-text table.
    /// </summary>
    private const string PipArguments = "list --outdated --format=json";

    private readonly ProcessRunner _processRunner;
    private readonly PipListOutputParser _parser;
    private readonly ILoggerService _logger;

    /// <summary>
    /// Initializes the scanner with its pipeline dependencies.
    /// </summary>
    public PipScanner(ProcessRunner processRunner, PipListOutputParser parser, ILoggerService logger)
    {
        _processRunner = processRunner;
        _parser = parser;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PipPackageItem>> GetAvailableUpdatesAsync(CancellationToken cancellationToken)
    {
        _logger.Info("Pip scan started.");
        _logger.Info($"Running: pip {PipArguments}");

        ProcessExecutionResult result;
        try
        {
            result = await _processRunner.RunAsync("pip", PipArguments, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.Info("Pip scan was cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to start pip. Make sure Python and pip are installed and on PATH.", ex);
            return Array.Empty<PipPackageItem>();
        }

        if (!result.Succeeded)
        {
            _logger.Warning($"Pip exited with code {result.ExitCode}. Output may be partial.");

            if (!string.IsNullOrWhiteSpace(result.StandardError))
                _logger.Warning($"Pip stderr: {result.StandardError.Trim()}");
        }

        var parsed = _parser.Parse(result.StandardOutput);
        _logger.Info($"Pip scan complete. {parsed.Count} outdated package(s) found.");

        return parsed;
    }
}
