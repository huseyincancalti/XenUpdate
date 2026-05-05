using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ZenUpdate.Core.Enums;
using ZenUpdate.Core.Interfaces;
using ZenUpdate.Core.Models;

namespace ZenUpdate.Infrastructure.Storage;

/// <summary>
/// Reads and writes the application settings JSON file at <c>%APPDATA%\XenUpdate\settings.json</c>.
/// Implements <see cref="ISettingsRepository"/>.
///
/// Designed to be unconditionally safe to call during application startup:
///   * A missing file returns defaults.
///   * Invalid JSON, unknown enum strings, numeric enum values, partial schemas
///     and fully corrupted contents all produce a usable <see cref="AppSettings"/>
///     instance instead of throwing.
///   * Saves are atomic (temp file + replace) so a crash mid-write cannot leave
///     a half-written settings.json that would crash the next launch.
/// </summary>
public sealed class JsonSettingsRepository : ISettingsRepository
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "XenUpdate", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        // Order matters: ResilientAppThemeConverter is checked before the
        // generic JsonStringEnumConverter, so a junk theme value like "Banana"
        // falls back to Dark instead of throwing a JsonException that would
        // discard the rest of the file.
        Converters =
        {
            new ResilientAppThemeConverter(),
            new JsonStringEnumConverter()
        }
    };

    private readonly ILoggerService _logger;

    /// <summary>
    /// Initializes a new <see cref="JsonSettingsRepository"/>.
    /// </summary>
    public JsonSettingsRepository(ILoggerService logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AppSettings> LoadAsync()
    {
        // ConfigureAwait(false) means the awaited continuations never try to
        // resume on a captured SynchronizationContext. This makes it safe for
        // App.OnStartup to block on this task via Task.Run + GetResult without
        // risking a dispatcher deadlock.
        try
        {
            if (!File.Exists(FilePath))
            {
                SafeLogInfo("Settings file not found. Using default settings.");
                return new AppSettings();
            }

            string json;
            try
            {
                json = await File.ReadAllTextAsync(FilePath).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                SafeLogWarning($"Settings file could not be read. Using defaults. Reason: {ex.Message}");
                return new AppSettings();
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                SafeLogWarning("Settings file was empty. Using default settings.");
                return new AppSettings();
            }

            try
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (settings is null)
                {
                    SafeLogWarning("Settings file deserialized to null. Using defaults.");
                    return new AppSettings();
                }

                NormalizeSettings(settings);
                SafeLogInfo("Settings loaded successfully.");
                return settings;
            }
            catch (JsonException ex)
            {
                SafeLogWarning($"Settings file was corrupted or had an incompatible schema. " +
                               $"Backing it up and using defaults. Reason: {ex.Message}");
                BackupCorruptedFile();
                return new AppSettings();
            }
        }
        catch (Exception ex)
        {
            // Absolute last resort. The app must still open even if something
            // unexpected (e.g. a security exception on %APPDATA%) blows up here.
            Debug.WriteLine($"[XenUpdate] LoadAsync hit an unexpected error: {ex}");
            SafeLogWarning($"Could not load settings. Using defaults. Reason: {ex.Message}");
            return new AppSettings();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(AppSettings settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempFilePath = FilePath + ".tmp";
            var json = JsonSerializer.Serialize(settings, JsonOptions);

            // Clean up any leftover temp file from a previous failed save so
            // File.WriteAllTextAsync starts from a known state.
            if (File.Exists(tempFilePath))
            {
                try { File.Delete(tempFilePath); } catch { /* best effort */ }
            }

            await File.WriteAllTextAsync(tempFilePath, json).ConfigureAwait(false);

            // File.Move with overwrite=true is atomic on the same volume on
            // Windows (uses MoveFileEx with MOVEFILE_REPLACE_EXISTING), which
            // prevents a half-written settings.json from ever appearing.
            File.Move(tempFilePath, FilePath, overwrite: true);

            SafeLogInfo("Settings saved successfully.");
        }
        catch (Exception ex)
        {
            SafeLogWarning($"Could not save settings. Reason: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Brings deserialized settings back into a known-good state so any field
    /// that survived as garbage (e.g. negative MaxLogConsoleEntries) is replaced
    /// with a sensible default before the rest of the app sees it.
    /// </summary>
    private static void NormalizeSettings(AppSettings settings)
    {
        if (!Enum.IsDefined(typeof(AppTheme), settings.Theme))
        {
            settings.Theme = AppTheme.Dark;
        }

        if (string.IsNullOrWhiteSpace(settings.LogDirectory))
        {
            settings.LogDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "XenUpdate", "logs");
        }

        if (settings.MaxLogConsoleEntries <= 0)
        {
            settings.MaxLogConsoleEntries = 200;
        }
    }

    private void BackupCorruptedFile()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return;
            }

            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var backupPath = FilePath.Replace(".json", $".invalid.{timestamp}.json");
            File.Move(FilePath, backupPath, overwrite: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[XenUpdate] Failed to back up corrupted settings file: {ex.Message}");
        }
    }

    private void SafeLogInfo(string message)
    {
        try { _logger.Info(message); }
        catch { Debug.WriteLine($"[XenUpdate] {message}"); }
    }

    private void SafeLogWarning(string message)
    {
        try { _logger.Warning(message); }
        catch { Debug.WriteLine($"[XenUpdate] {message}"); }
    }

    /// <summary>
    /// Tolerant <see cref="AppTheme"/> JSON converter. Accepts:
    ///   * Strings: "Dark" / "Light" (case-insensitive).
    ///   * Numbers: 0 / 1 (legacy schema where the enum was serialized as int).
    ///   * Anything else: silently falls back to <see cref="AppTheme.Dark"/>.
    /// This means a manually-edited settings.json with <c>"theme":"Banana"</c>
    /// no longer crashes startup, and the rest of the file is preserved
    /// (whereas the default JsonStringEnumConverter throws and the whole file
    /// gets discarded).
    /// </summary>
    private sealed class ResilientAppThemeConverter : JsonConverter<AppTheme>
    {
        public override AppTheme Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            try
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.String:
                        var raw = reader.GetString();
                        if (!string.IsNullOrWhiteSpace(raw)
                            && Enum.TryParse<AppTheme>(raw, ignoreCase: true, out var parsed)
                            && Enum.IsDefined(typeof(AppTheme), parsed))
                        {
                            return parsed;
                        }
                        break;

                    case JsonTokenType.Number:
                        if (reader.TryGetInt32(out var n)
                            && Enum.IsDefined(typeof(AppTheme), n))
                        {
                            return (AppTheme)n;
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                    Debug.WriteLine($"[XenUpdate] Theme value could not be parsed, falling back to Dark: {ex.Message}");
            }

            return AppTheme.Dark;
        }

        public override void Write(Utf8JsonWriter writer, AppTheme value, JsonSerializerOptions options)
        {
            // Always persist as a human-readable string so future schemas stay forward-compatible.
            writer.WriteStringValue(value.ToString());
        }
    }
}
