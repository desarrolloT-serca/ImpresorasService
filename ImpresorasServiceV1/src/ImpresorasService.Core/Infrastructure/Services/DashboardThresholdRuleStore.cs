using System.Text.Json;
using System.Text.Json.Serialization;
using ImpresorasService.Application.Abstractions;
using ImpresorasService.Application.Services;
using ImpresorasService.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ImpresorasService.Infrastructure.Services;

/// <summary>
/// Reglas de severidad de dashboard en un fichero JSON (G5.3) — mismo formato que el
/// dashboard-threshold-rules.json que usaba PHP, ahora la Api es la fuente única de verdad.
/// Compartido por Api y Worker vía la misma ruta configurada.
/// </summary>
public sealed class DashboardThresholdRuleStore : IDashboardThresholdRuleStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _filePath;
    private readonly ILogger<DashboardThresholdRuleStore>? _logger;

    public DashboardThresholdRuleStore(IOptions<DashboardThresholdRulesOptions> options, ILogger<DashboardThresholdRuleStore>? logger = null)
    {
        _logger = logger;
        _filePath = string.IsNullOrWhiteSpace(options.Value.ThresholdRulesFilePath)
            ? Path.Combine(AppContext.BaseDirectory, "dashboard-threshold-rules.json")
            : options.Value.ThresholdRulesFilePath;
    }

    public async Task<ThresholdRuleSet> LoadAsync(CancellationToken ct)
    {
        try
        {
            if (!File.Exists(_filePath))
                return ThresholdRuleSet.Default;

            await using var stream = File.OpenRead(_filePath);
            var dto = await JsonSerializer.DeserializeAsync<RuleSetDto>(stream, JsonOptions, ct);
            if (dto is null)
                return ThresholdRuleSet.Default;

            var rules = ToRuleSet(dto);
            var errors = ThresholdRuleEngine.Validate(rules);
            return errors.Count == 0 ? rules : ThresholdRuleSet.Default;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "No se pudieron cargar las reglas de dashboard desde {FilePath}; usando valores por defecto.", _filePath);
            return ThresholdRuleSet.Default;
        }
    }

    public async Task<IReadOnlyList<string>> SaveAsync(ThresholdRuleSet rules, CancellationToken ct)
    {
        var normalized = new ThresholdRuleSet(
            ThresholdRuleEngine.NormalizeRuleSet(rules.Queue).ToList(),
            ThresholdRuleEngine.NormalizeRuleSet(rules.Failed).ToList(),
            ThresholdRuleEngine.NormalizeRuleSet(rules.MissingHost).ToList(),
            ThresholdRuleEngine.NormalizeRuleSet(rules.Conn).ToList());

        var errors = ThresholdRuleEngine.Validate(normalized);
        if (errors.Count > 0)
            return errors;

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, ToDto(normalized), JsonOptions, ct);
        return Array.Empty<string>();
    }

    private static ThresholdRuleSet ToRuleSet(RuleSetDto dto) => new(
        Queue: ToRules(dto.Queue),
        Failed: ToRules(dto.Failed),
        MissingHost: ToRules(dto.MissingHost),
        Conn: ToRules(dto.Conn));

    private static List<ThresholdRule> ToRules(List<RuleDto>? rules) =>
        (rules ?? new List<RuleDto>()).Select(r => new ThresholdRule(r.Min, r.Severity)).ToList();

    private static RuleSetDto ToDto(ThresholdRuleSet rules) => new()
    {
        Queue = rules.Queue.Select(r => new RuleDto { Min = r.Min, Severity = r.Severity }).ToList(),
        Failed = rules.Failed.Select(r => new RuleDto { Min = r.Min, Severity = r.Severity }).ToList(),
        MissingHost = rules.MissingHost.Select(r => new RuleDto { Min = r.Min, Severity = r.Severity }).ToList(),
        Conn = rules.Conn.Select(r => new RuleDto { Min = r.Min, Severity = r.Severity }).ToList()
    };

    private sealed class RuleSetDto
    {
        public List<RuleDto>? Queue { get; set; }
        public List<RuleDto>? Failed { get; set; }
        public List<RuleDto>? MissingHost { get; set; }
        public List<RuleDto>? Conn { get; set; }
    }

    private sealed class RuleDto
    {
        public int Min { get; set; }
        public string Severity { get; set; } = "warning";
    }
}
