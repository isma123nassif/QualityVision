using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

using QualityVision.Core.Rules;
using QualityVision.Infrastructure.Rules;
using QualityVision.Web.Pipeline;

namespace QualityVision.Tests.Web;

public sealed class RulesFileServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qv-rules-" + Guid.NewGuid().ToString("N"));
    private readonly RulesFileService _service;

    public RulesFileServiceTests()
    {
        Directory.CreateDirectory(_dir);
        _service = new RulesFileService(new TestEnvironment(_dir), NullLogger<RulesFileService>.Instance);
    }

    private string RulesPath => Path.Combine(_dir, "rules.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Read_MissingFile_ReturnsEmpty()
    {
        Assert.Empty(_service.Read());
    }

    [Fact]
    public void Read_InvalidJson_ReturnsEmpty()
    {
        File.WriteAllText(RulesPath, "{ not json");

        Assert.Empty(_service.Read());
    }

    [Fact]
    public async Task SaveAsync_ThenRead_RoundTripsRules()
    {
        var rules = new Dictionary<string, Verdict>
        {
            ["cup"] = Verdict.Ok,
            ["bottle"] = Verdict.Nok,
            ["knife"] = Verdict.Danger,
            ["book"] = Verdict.Ignore,
        };

        await _service.SaveAsync(rules, CancellationToken.None);
        IReadOnlyDictionary<string, Verdict> read = _service.Read();

        Assert.Equal(rules.OrderBy(p => p.Key), read.OrderBy(p => p.Key));
    }

    [Fact]
    public void Read_FileWithCommentsAndTrailingComma_ReturnsRules()
    {
        File.WriteAllText(
            RulesPath,
            """
            {
              // hand-edited during the demo
              "Rules": {
                "Classes": {
                  "cup": "Ok", /* inline */
                  "bottle": "Nok",
                },
              },
            }
            """);

        bool ok = _service.TryRead(out IReadOnlyDictionary<string, Verdict> rules);

        Assert.True(ok);
        Assert.Equal(2, rules.Count);
        Assert.Equal(Verdict.Ok, rules["cup"]);
        Assert.Equal(Verdict.Nok, rules["bottle"]);
    }

    [Fact]
    public void TryRead_InvalidJson_ReportsFailureWithEmptyRules()
    {
        File.WriteAllText(RulesPath, "{ not json");

        bool ok = _service.TryRead(out IReadOnlyDictionary<string, Verdict> rules);

        Assert.False(ok);
        Assert.Empty(rules);
    }

    [Fact]
    public void TryRead_MissingFile_IsNotAnError()
    {
        bool ok = _service.TryRead(out IReadOnlyDictionary<string, Verdict> rules);

        Assert.True(ok);
        Assert.Empty(rules);
    }

    [Theory]
    [InlineData(Verdict.Unknown)]
    [InlineData((Verdict)99)]
    public async Task SaveAsync_UnknownOrUndefinedVerdict_ThrowsGermanArgumentExceptionAndWritesNothing(Verdict verdict)
    {
        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.SaveAsync(new Dictionary<string, Verdict> { ["cup"] = verdict }, CancellationToken.None));

        Assert.Contains("Ungültige Bewertung", ex.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(RulesPath));
    }

    [Fact]
    public async Task Read_Keys_AreCaseInsensitive()
    {
        await _service.SaveAsync(new Dictionary<string, Verdict> { ["Cup"] = Verdict.Ok }, CancellationToken.None);

        Assert.Equal(Verdict.Ok, _service.Read()["cup"]);
    }

    [Fact]
    public async Task SaveAsync_NameWithWhitespace_IsTrimmed()
    {
        await _service.SaveAsync(new Dictionary<string, Verdict> { ["  cup "] = Verdict.Ok }, CancellationToken.None);

        Assert.Equal("cup", Assert.Single(_service.Read()).Key);
    }

    [Fact]
    public async Task SaveAsync_Overwrite_ReplacesPreviousContent()
    {
        await _service.SaveAsync(new Dictionary<string, Verdict> { ["cup"] = Verdict.Ok }, CancellationToken.None);
        await _service.SaveAsync(new Dictionary<string, Verdict> { ["bottle"] = Verdict.Nok }, CancellationToken.None);

        KeyValuePair<string, Verdict> only = Assert.Single(_service.Read());
        Assert.Equal("bottle", only.Key);
        Assert.Equal(Verdict.Nok, only.Value);
    }

    [Fact]
    public async Task SaveAsync_EmptyRules_ThrowsArgumentExceptionAndWritesNothing()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.SaveAsync(new Dictionary<string, Verdict>(), CancellationToken.None));

        Assert.False(File.Exists(RulesPath));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SaveAsync_BlankClassName_ThrowsArgumentException(string name)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.SaveAsync(new Dictionary<string, Verdict> { [name] = Verdict.Ok }, CancellationToken.None));

        Assert.False(File.Exists(RulesPath));
    }

    [Fact]
    public async Task SaveAsync_DuplicateAfterTrimAndCaseFolding_ThrowsArgumentException()
    {
        var rules = new Dictionary<string, Verdict>
        {
            ["cup"] = Verdict.Ok,
            [" CUP"] = Verdict.Nok,
        };

        await Assert.ThrowsAsync<ArgumentException>(() => _service.SaveAsync(rules, CancellationToken.None));

        Assert.False(File.Exists(RulesPath));
    }

    [Fact]
    public async Task SaveAsync_InvalidRules_LeaveExistingFileUntouched()
    {
        await _service.SaveAsync(new Dictionary<string, Verdict> { ["cup"] = Verdict.Ok }, CancellationToken.None);
        string before = await File.ReadAllTextAsync(RulesPath);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.SaveAsync(new Dictionary<string, Verdict>(), CancellationToken.None));

        Assert.Equal(before, await File.ReadAllTextAsync(RulesPath));
    }

    [Fact]
    public async Task SaveAsync_Success_LeavesNoTmpFile()
    {
        await _service.SaveAsync(new Dictionary<string, Verdict> { ["cup"] = Verdict.Ok }, CancellationToken.None);
        await _service.SaveAsync(new Dictionary<string, Verdict> { ["cup"] = Verdict.Nok }, CancellationToken.None);

        string file = Assert.Single(Directory.GetFiles(_dir));
        Assert.Equal(RulesPath, file);
        Assert.False(File.Exists(RulesPath + ".tmp"));
    }

    [Fact]
    public async Task SaveAsync_ConcurrentSaves_LeaveValidFileAndNoTmp()
    {
        Task[] saves = Enumerable.Range(0, 10)
            .Select(i => _service.SaveAsync(
                new Dictionary<string, Verdict> { [$"class{i}"] = Verdict.Ok },
                CancellationToken.None))
            .ToArray();

        await Task.WhenAll(saves);

        Assert.Single(_service.Read());
        Assert.Single(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task SaveAsync_CancelledToken_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _service.SaveAsync(new Dictionary<string, Verdict> { ["cup"] = Verdict.Ok }, cts.Token));
    }

    [Fact]
    public async Task SaveAsync_File_IsReadableByConfigurationAndBindsToSameRules()
    {
        var rules = new Dictionary<string, Verdict>
        {
            ["cup"] = Verdict.Ok,
            ["bottle"] = Verdict.Nok,
            ["knife"] = Verdict.Danger,
        };
        await _service.SaveAsync(rules, CancellationToken.None);

        IConfigurationRoot config = new ConfigurationBuilder()
            .SetBasePath(_dir)
            .AddJsonFile("rules.json", optional: false)
            .Build();
        RulesOptions options = config.GetSection(RulesOptions.SectionName).Get<RulesOptions>()!;

        Assert.Equal(rules.Count, options.Classes.Count);
        foreach ((string name, Verdict verdict) in rules)
        {
            Assert.Equal(verdict, Enum.Parse<Verdict>(options.Classes[name], ignoreCase: true));
        }
    }

    private sealed class TestEnvironment(string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";

        public string ApplicationName { get; set; } = "QualityVision.Tests";

        public string ContentRootPath { get; set; } = contentRoot;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
