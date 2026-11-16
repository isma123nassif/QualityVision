using System.Text.Json;

using QualityVision.Core.Rules;

namespace QualityVision.Web.Pipeline;

/// <summary>
/// Reads and atomically writes <c>rules.json</c> in the content root (the file Program.cs watches). The existing
/// reloadOnChange + <c>OptionsRuleSetProvider</c> pick up changes; nothing else needs to be wired (ADR-002).
/// </summary>
public sealed class RulesFileService
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    // Same leniency as the configuration JSON provider that loads this file.
    private static readonly JsonDocumentOptions ReadOptions =
        new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private readonly string _path;
    private readonly ILogger<RulesFileService> _logger;
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    /// <summary>Creates the service for the <c>rules.json</c> in the content root.</summary>
    /// <param name="environment">Host environment (content root).</param>
    /// <param name="logger">Logger for unreadable files and skipped entries.</param>
    public RulesFileService(IHostEnvironment environment, ILogger<RulesFileService> logger)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _path = Path.Combine(environment.ContentRootPath, "rules.json");
    }

    /// <summary>Current file contents: class name → verdict (case-insensitive keys). Empty if missing or unreadable.</summary>
    public IReadOnlyDictionary<string, Verdict> Read()
    {
        TryRead(out IReadOnlyDictionary<string, Verdict> rules);
        return rules;
    }

    /// <summary>
    /// Reads the file with the same leniency as the configuration JSON provider (comments, trailing commas).
    /// A missing file counts as success with no rules; an unreadable or invalid file is logged and reported.
    /// </summary>
    /// <param name="rules">Class name → verdict (case-insensitive keys); empty on failure.</param>
    /// <returns><c>false</c> if the file exists but could not be read or parsed.</returns>
    public bool TryRead(out IReadOnlyDictionary<string, Verdict> rules)
    {
        var result = new Dictionary<string, Verdict>(StringComparer.OrdinalIgnoreCase);
        rules = result;
        try
        {
            using FileStream stream = new(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using JsonDocument doc = JsonDocument.Parse(stream, ReadOptions);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("Rules", out JsonElement ruleSection)
                && ruleSection.ValueKind == JsonValueKind.Object
                && ruleSection.TryGetProperty("Classes", out JsonElement classes)
                && classes.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty p in classes.EnumerateObject())
                {
                    if (p.Value.ValueKind == JsonValueKind.String
                        && Enum.TryParse(p.Value.GetString(), ignoreCase: true, out Verdict v))
                    {
                        result[p.Name] = v;
                    }
                    else
                    {
                        _logger.LogWarning("Ignoring rule entry '{Class}' in {Path}: not a valid verdict", p.Name, _path);
                    }
                }
            }
        }
        catch (FileNotFoundException)
        {
            // No file yet: nothing to read, not an error.
        }
        catch (DirectoryNotFoundException)
        {
            // Same as above.
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "rules.json could not be read ({Path})", _path);
            result.Clear();
            return false;
        }

        return true;
    }

    /// <summary>Validates and atomically writes the rules; the running app reloads them live.</summary>
    /// <param name="rules">Class name → verdict (Ok, Nok, Danger or Ignore).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentException">The rules are invalid (message in German).</exception>
    public async Task SaveAsync(IReadOnlyDictionary<string, Verdict> rules, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var sorted = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string rawName, Verdict verdict) in rules)
        {
            if (verdict == Verdict.Unknown || !Enum.IsDefined(verdict))
            {
                throw new ArgumentException(
                    $"Ungültige Bewertung für „{rawName}“: erlaubt sind Ok, Nok, Danger und Ignore.",
                    nameof(rules));
            }

            string name = rawName?.Trim() ?? string.Empty;
            if (name.Length == 0)
            {
                throw new ArgumentException("Klassenname darf nicht leer sein.", nameof(rules));
            }

            if (!sorted.TryAdd(name, verdict.ToString()))
            {
                throw new ArgumentException($"Die Klasse „{name}“ ist doppelt vorhanden.", nameof(rules));
            }
        }

        if (sorted.Count == 0)
        {
            throw new ArgumentException("Mindestens eine Regel ist erforderlich.", nameof(rules));
        }

        var document = new Dictionary<string, object>
        {
            ["Rules"] = new Dictionary<string, object> { ["Classes"] = sorted },
        };
        string json = JsonSerializer.Serialize(document, WriteOptions) + "\n";

        await _saveGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            string tmp = _path + ".tmp";
            try
            {
                await File.WriteAllTextAsync(tmp, json, ct).ConfigureAwait(false);
                File.Move(tmp, _path, overwrite: true);
            }
            catch
            {
                try
                {
                    File.Delete(tmp);
                }
                catch (IOException)
                {
                    // Best effort: a leftover .tmp is harmless (not watched).
                }
                catch (UnauthorizedAccessException)
                {
                    // Same.
                }

                throw;
            }
        }
        finally
        {
            _saveGate.Release();
        }
    }
}
