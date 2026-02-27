using System.Diagnostics;
using System.Runtime.InteropServices;
using ImpresorasService.Application.Abstractions;
using ImpresorasService.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ImpresorasService.Infrastructure.Services;

/// <summary>
/// Implementación que envía PDF a la cola de impresión de Windows usando un ejecutable externo.
/// Por defecto busca SumatraPDF en rutas habituales; configurable vía PrintExecutionOptions.
/// </summary>
public sealed class WindowsPrintSpooler : IPrinterSpooler
{
    private readonly ILogger<WindowsPrintSpooler> _logger;
    private readonly PrintExecutionOptions _options;

    private static readonly string[] DefaultSumatraPaths =
    [
        @"C:\Program Files\SumatraPDF\SumatraPDF.exe",
        @"C:\Program Files (x86)\SumatraPDF\SumatraPDF.exe",
        @"%LOCALAPPDATA%\SumatraPDF\SumatraPDF.exe"
    ];

    public WindowsPrintSpooler(
        ILogger<WindowsPrintSpooler> logger,
        IOptions<PrintExecutionOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public async Task<PrintSpoolResult> SendToPrinterAsync(
        byte[] pdfBlob,
        string spoolQueueName,
        CancellationToken cancellationToken = default)
    {
        if (pdfBlob.Length == 0)
            return new PrintSpoolResult(false, "PDF_INVALID", "PDF vacío", false);

        // Validar que sea un PDF válido (mínimo ~70 bytes para estructura mínima)
        if (pdfBlob.Length < 50 || !IsValidPdfHeader(pdfBlob))
            return new PrintSpoolResult(false, "PDF_INVALID", "PDF inválido o corrupto (tamaño o cabecera incorrecta)", false);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new PrintSpoolResult(false, "SPOOLER_DOWN", "Spooler solo soportado en Windows", false);

        var exePath = ResolvePdfPrinterExecutable();
        if (string.IsNullOrEmpty(exePath))
            return new PrintSpoolResult(false, "SPOOLER_DOWN", "No se encontró ejecutable para imprimir PDF (SumatraPDF)", false);

        string tempPath;
        try
        {
            tempPath = Path.Combine(Path.GetTempPath(), $"impresoras-{Guid.NewGuid():N}.pdf");
            await File.WriteAllBytesAsync(tempPath, pdfBlob, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error escribiendo PDF temporal");
            return new PrintSpoolResult(false, "PDF_INVALID", ex.Message, false);
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

            var result = await RunPrintProcessAsync(exePath, tempPath, spoolQueueName, cts.Token);
            return result;
        }
        finally
        {
            try { File.Delete(tempPath); } catch { /* ignore */ }
        }
    }

    private static bool IsValidPdfHeader(byte[] pdf)
    {
        if (pdf.Length < 5) return false;
        return pdf[0] == 0x25 && pdf[1] == 0x50 && pdf[2] == 0x44 && pdf[3] == 0x46; // %PDF
    }

    private string? ResolvePdfPrinterExecutable()
    {
        if (!string.IsNullOrWhiteSpace(_options.PdfPrinterExecutablePath) && File.Exists(_options.PdfPrinterExecutablePath))
            return _options.PdfPrinterExecutablePath;

        foreach (var p in DefaultSumatraPaths)
        {
            var expanded = Environment.ExpandEnvironmentVariables(p);
            if (File.Exists(expanded))
                return expanded;
        }

        return null;
    }

    private async Task<PrintSpoolResult> RunPrintProcessAsync(
        string exePath,
        string pdfPath,
        string printerName,
        CancellationToken cancellationToken)
    {
        var args = $"-silent -print-to \"{printerName}\" -print-settings \"n\" \"{pdfPath}\"";
        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = args,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process == null)
                return new PrintSpoolResult(false, "SPOOLER_DOWN", "No se pudo iniciar el proceso de impresión", true);

            using var reg = cancellationToken.Register(() => process.Kill(entireProcessTree: true));

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                var msg = string.IsNullOrWhiteSpace(error) ? output : error;
                _logger.LogWarning("Impresión fallida. ExitCode={ExitCode}, Msg={Msg}", process.ExitCode, msg);
                // PDF corrupto (no transient): no reintentar indefinidamente
                var isPdfCorrupt = msg.Contains("Couldn't open file", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("no objects found", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("cannot find startxref", StringComparison.OrdinalIgnoreCase);
                return new PrintSpoolResult(false, isPdfCorrupt ? "PDF_INVALID" : "SPOOLER_DOWN", msg.Trim(), !isPdfCorrupt);
            }

            _logger.LogInformation("PDF enviado a spooler correctamente. Printer={Printer}", printerName);
            return new PrintSpoolResult(true, null, null, false);
        }
        catch (OperationCanceledException)
        {
            return new PrintSpoolResult(false, "NET_TIMEOUT", "Timeout de impresión", true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enviando a spooler");
            return new PrintSpoolResult(false, "SPOOLER_DOWN", ex.Message, true);
        }
    }
}
