using System.IO;
using System.Text.Json;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;

namespace XenUpdate.App.Services;

/// <summary>
/// Singleton service that loads locale JSON files and exposes translated strings
/// via an indexer. WPF bindings using <c>Item[]</c> update automatically whenever
/// <see cref="ChangeLanguage"/> is called.
/// </summary>
public sealed class LocalizationManager : ObservableObject
{
    /// <summary>The single shared instance used across the application.</summary>
    public static LocalizationManager Instance { get; } = new();

    private Dictionary<string, string> _strings = new(StringComparer.OrdinalIgnoreCase);

    private LocalizationManager()
    {
        ChangeLanguage("en");
    }

    /// <summary>
    /// Returns the localized string for <paramref name="key"/>.
    /// Falls back to the key itself when no translation is found.
    /// </summary>
    public string this[string key] =>
        _strings.TryGetValue(key, out var val) ? val : key;

    /// <summary>
    /// Loads the JSON file for <paramref name="languageCode"/> and notifies
    /// all WPF bindings to re-read localized values.
    /// </summary>
    public void ChangeLanguage(string languageCode)
    {
        try
        {
            var filePath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Assets", "Locales",
                $"{languageCode}.json");

            if (!File.Exists(filePath))
            {
                return;
            }

            var json = File.ReadAllText(filePath);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (loaded is not null)
            {
                _strings = new Dictionary<string, string>(loaded, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch
        {
            // Keep existing strings if the file cannot be loaded.
        }

        // Notify WPF that every indexed binding is stale.
        OnPropertyChanged(Binding.IndexerName);
    }
}
