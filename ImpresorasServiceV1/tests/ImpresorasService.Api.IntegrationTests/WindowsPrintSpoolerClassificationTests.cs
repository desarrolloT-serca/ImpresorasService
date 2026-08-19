using ImpresorasService.Infrastructure.Services;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests;

/// <summary>
/// H-11: el spooler de Windows no tenia ni una prueba, y esta es la decision que mas cara sale si se
/// equivoca. Un fallo marcado como transitorio se reintenta hasta MaxAttempts, y cada reintento es
/// otro envio al spooler; uno marcado como permanente cierra el trabajo sin volver a intentarlo.
/// </summary>
public sealed class WindowsPrintSpoolerClassificationTests
{
    [Theory]
    [InlineData(@"Error: Couldn't open file 'C:\temp\x.pdf'")]
    [InlineData("no objects found")]
    [InlineData("Error: cannot find startxref")]
    // El mensaje real de Sumatra no viene normalizado: se compara sin distinguir mayusculas.
    [InlineData("COULDN'T OPEN FILE")]
    public void CorruptPdf_IsPermanent(string processOutput)
    {
        var result = WindowsPrintSpooler.ClassifyFailedExit(processOutput);

        Assert.False(result.Success);
        Assert.Equal("PDF_INVALID", result.ErrorCode);
        // Lo importante: NO se reintenta. Un PDF roto no se arregla insistiendo.
        Assert.False(result.IsTransient);
    }

    [Theory]
    [InlineData("The printer is not responding")]
    [InlineData("")]
    [InlineData(null)]
    public void AnyOtherFailure_IsTransientAndRetried(string? processOutput)
    {
        var result = WindowsPrintSpooler.ClassifyFailedExit(processOutput);

        Assert.False(result.Success);
        Assert.Equal("SPOOLER_DOWN", result.ErrorCode);
        Assert.True(result.IsTransient);
    }

    [Fact]
    public void ErrorMessage_IsTrimmed_SoTheUiDoesNotShowStrayWhitespace()
    {
        var result = WindowsPrintSpooler.ClassifyFailedExit("  algo fallo  \r\n");

        Assert.Equal("algo fallo", result.ErrorMessage);
    }
}
