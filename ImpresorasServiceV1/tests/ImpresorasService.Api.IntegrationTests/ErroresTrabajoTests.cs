using System.Text.RegularExpressions;
using ImpresorasService.Domain;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests;

/// <summary>Cada código de error que puede dejar el servicio tiene texto y qué hacer (ErroresTrabajo).</summary>
public sealed class ErroresTrabajoTests
{
    // Los códigos en mayúsculas que aparecen en el código fuente como último error de un trabajo.
    public static TheoryData<string> CodigosDelServicio()
    {
        var src = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src"));
        var codigos = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), "\"((?:IPP|PDF|SPOOL|SPOOLER|PRINTER|PRINTING|ROUTE|NET|NO|RETRIES)_[A-Z_]+)\"")
                .Select(m => m.Groups[1].Value))
            // De impresoras (conectividad), no de trabajos.
            .Where(c => c is not "NO_CONNECTION")
            .ToHashSet();
        var datos = new TheoryData<string>();
        foreach (var c in codigos.Order()) datos.Add(c);
        return datos;
    }

    [Theory]
    [MemberData(nameof(CodigosDelServicio))]
    public void Cada_codigo_tiene_texto_y_que_hacer(string codigo)
    {
        Assert.False(string.IsNullOrWhiteSpace(ErroresTrabajo.Texto(codigo)), codigo);
        Assert.False(string.IsNullOrWhiteSpace(ErroresTrabajo.QueHacer(codigo)), codigo);
    }

    [Theory]
    [InlineData("media-empty-error", "Sin papel en la bandeja")]
    [InlineData("media-jam", "Atasco de papel")]
    [InlineData("toner-low-warning", "El tóner está bajo")]
    [InlineData("document-format-error", "La impresora no entiende el formato del documento")]
    public void Los_motivos_de_IPP_se_traducen(string motivo, string texto)
    {
        Assert.Equal(texto, ErroresTrabajo.Texto(motivo));
        Assert.NotNull(ErroresTrabajo.QueHacer(motivo));
    }

    [Fact]
    public void Lo_desconocido_se_queda_tal_cual()
    {
        Assert.Null(ErroresTrabajo.Texto("ALGO_RARO"));
        Assert.Null(ErroresTrabajo.Texto(null));
    }
}
