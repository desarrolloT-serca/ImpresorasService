<?php

namespace App\Http\Controllers;

use App\Services\ApiClient;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\Storage;
use Illuminate\Support\Str;
use Illuminate\View\View;

class PruebasController extends Controller
{
    private const SCENARIOS_DISK = 'local';
    private const SCENARIOS_DIR  = 'test-scenarios';
    private const PDFS_DIR       = 'test-pdfs';
    private const PDF_INDEX      = 'test-pdfs/library.json';

    private const DOC_TYPES = [
        'ALBARAN', 'FACTURA', 'TRASPASO', 'CALLCENTER', 'AD360', 'DEVOLUCION',
    ];

    private const TERMINAL_STATUSES = [
        'SpoolAccepted', 'ErrorFinal', 'PrintedConfirmed', 'PrintedUnknown',
    ];

    private const STATUS_NAMES = [
        0 => 'Pending', 1 => 'Routed', 2 => 'Printing', 3 => 'SpoolAccepted',
        4 => 'PrintedConfirmed', 5 => 'PrintedUnknown', 6 => 'RetryScheduled',
        7 => 'Cancelled', 8 => 'ErrorFinal', 9 => 'PrinterBlocked',
    ];

    public function __construct(private readonly ApiClient $api) {}

    // ── Helpers ───────────────────────────────────────────────────────────────

    private function loadScenarios(): array
    {
        $files = Storage::disk(self::SCENARIOS_DISK)->files(self::SCENARIOS_DIR);
        $scenarios = [];
        foreach ($files as $file) {
            if (!str_ends_with($file, '.json')) {
                continue;
            }
            $data = json_decode(Storage::disk(self::SCENARIOS_DISK)->get($file), true);
            if (is_array($data) && isset($data['id'])) {
                $scenarios[] = $data;
            }
        }
        usort($scenarios, fn ($a, $b) => strcmp($a['createdAt'] ?? '', $b['createdAt'] ?? ''));
        return $scenarios;
    }

    private function loadPdfLibrary(): array
    {
        if (!Storage::disk(self::SCENARIOS_DISK)->exists(self::PDF_INDEX)) {
            return [];
        }
        return json_decode(Storage::disk(self::SCENARIOS_DISK)->get(self::PDF_INDEX), true) ?? [];
    }

    private function savePdfLibrary(array $library): void
    {
        Storage::disk(self::SCENARIOS_DISK)->put(self::PDF_INDEX, json_encode($library, JSON_PRETTY_PRINT));
    }

    // ── Actions ───────────────────────────────────────────────────────────────

    public function index(): View
    {
        $scenarios = $this->loadScenarios();
        $pdfs      = $this->loadPdfLibrary();
        $stores    = $this->api->get('api/stores?isActive=true');

        return view('pruebas.index', [
            'scenarios' => $scenarios,
            'pdfs'      => $pdfs,
            'stores'    => is_array($stores) ? $stores : [],
            'docTypes'  => self::DOC_TYPES,
        ]);
    }

    public function saveScenario(Request $request): JsonResponse
    {
        $request->validate([
            'name'                   => 'required|string|max:100',
            'batches'                => 'required|array|min:1',
            'batches.*.storeId'      => 'required|integer|min:1',
            'batches.*.documentType' => 'required|string|max:50',
            'batches.*.channel'      => 'nullable|string|max:50',
            'batches.*.count'        => 'required|integer|min:1|max:50',
            'batches.*.pdfId'        => 'nullable|string|max:36',
        ]);

        $id       = $request->input('id') ?: (string) Str::uuid();
        $scenario = [
            'id'        => $id,
            'name'      => $request->input('name'),
            'createdAt' => $request->input('createdAt') ?: now()->toIso8601String(),
            'batches'   => array_map(fn ($b) => [
                'storeId'      => (int) $b['storeId'],
                'documentType' => strtoupper(trim($b['documentType'])),
                'channel'      => strtoupper(trim($b['channel'] ?? 'DEFAULT')) ?: 'DEFAULT',
                'count'        => (int) $b['count'],
                'pdfId'        => $b['pdfId'] ?? null,
            ], $request->input('batches')),
        ];

        Storage::disk(self::SCENARIOS_DISK)->put(
            self::SCENARIOS_DIR . '/' . $id . '.json',
            json_encode($scenario, JSON_PRETTY_PRINT)
        );

        return response()->json($scenario);
    }

    public function deleteScenario(string $id): JsonResponse
    {
        $path = self::SCENARIOS_DIR . '/' . $id . '.json';
        if (Storage::disk(self::SCENARIOS_DISK)->exists($path)) {
            Storage::disk(self::SCENARIOS_DISK)->delete($path);
        }
        return response()->json(['ok' => true]);
    }

    public function uploadPdf(Request $request): JsonResponse
    {
        $request->validate([
            'pdf' => 'required|file|mimes:pdf|max:20480',
        ]);

        $file   = $request->file('pdf');
        $id     = (string) Str::uuid();
        $stored = $file->storeAs(self::PDFS_DIR, $id . '.pdf', self::SCENARIOS_DISK);

        if (!$stored) {
            return response()->json(['error' => 'No se pudo guardar el PDF.'], 500);
        }

        $library   = $this->loadPdfLibrary();
        $library[] = [
            'id'         => $id,
            'name'       => $file->getClientOriginalName(),
            'size'       => $file->getSize(),
            'uploadedAt' => now()->toIso8601String(),
        ];
        $this->savePdfLibrary($library);

        return response()->json(end($library));
    }

    public function deletePdf(string $id): JsonResponse
    {
        foreach ($this->loadScenarios() as $scenario) {
            foreach ($scenario['batches'] ?? [] as $batch) {
                if (($batch['pdfId'] ?? null) === $id) {
                    return response()->json([
                        'error' => 'Este PDF está en uso por el escenario "' . ($scenario['name'] ?? $id) . '".',
                    ], 409);
                }
            }
        }

        $path = self::PDFS_DIR . '/' . $id . '.pdf';
        if (Storage::disk(self::SCENARIOS_DISK)->exists($path)) {
            Storage::disk(self::SCENARIOS_DISK)->delete($path);
        }

        $library = array_values(array_filter($this->loadPdfLibrary(), fn ($p) => $p['id'] !== $id));
        $this->savePdfLibrary($library);

        return response()->json(['ok' => true]);
    }

    public function run(Request $request): JsonResponse
    {
        $request->validate(['scenarioId' => 'required|string']);

        $path = self::SCENARIOS_DIR . '/' . $request->input('scenarioId') . '.json';
        if (!Storage::disk(self::SCENARIOS_DISK)->exists($path)) {
            return response()->json(['error' => 'Escenario no encontrado.'], 404);
        }

        $scenario = json_decode(Storage::disk(self::SCENARIOS_DISK)->get($path), true);
        $ts       = now()->format('YmdHis');
        $slug     = strtoupper(preg_replace('/[^A-Za-z0-9]/', '', $scenario['name'] ?? 'PRUEBA'));
        $slug     = substr($slug, 0, 20);
        // runId es el needle único que el endpoint de status usará para buscar via LIKE
        $runId    = 'PRUEBA-' . $slug . '-' . $ts;
        $jobIds   = [];
        $injected = 0;
        $errors   = [];
        $seq      = 0; // contador global entre todos los lotes para evitar IDs duplicados

        foreach ($scenario['batches'] ?? [] as $batch) {
            $pdfBase64 = null;
            $pdfId     = $batch['pdfId'] ?? null;
            if ($pdfId) {
                $pdfPath = self::PDFS_DIR . '/' . $pdfId . '.pdf';
                if (Storage::disk(self::SCENARIOS_DISK)->exists($pdfPath)) {
                    $pdfBase64 = base64_encode(Storage::disk(self::SCENARIOS_DISK)->get($pdfPath));
                }
            }

            $count = max(1, (int) ($batch['count'] ?? 1));
            for ($i = 0; $i < $count; $i++) {
                $seq++;
                $externalJobId = sprintf('%s-%03d', $runId, $seq);
                try {
                    $this->api->post('api/sourceprintjobs/test', [
                        'sourceSystem'  => 'PRUEBA',
                        'externalJobId' => $externalJobId,
                        'storeId'       => (int) $batch['storeId'],
                        'documentType'  => $batch['documentType'],
                        'channel'       => $batch['channel'] ?? 'DEFAULT',
                        'pdfBlob'       => $pdfBase64,
                    ]);
                    $jobIds[] = $externalJobId;
                    $injected++;
                } catch (\GuzzleHttp\Exception\RequestException $e) {
                    $status = $e->getResponse()?->getStatusCode();
                    if ($status === 409) {
                        $jobIds[] = $externalJobId;
                        $injected++;
                    } else {
                        $errors[] = $externalJobId . ': HTTP ' . ($status ?? 'unknown');
                    }
                }
            }
        }

        return response()->json([
            'runId'    => $runId,
            'injected' => $injected,
            'jobIds'   => $jobIds,
            'errors'   => $errors,
        ]);
    }

    public function cancelAll(): JsonResponse
    {
        $result = $this->api->post('api/sourceprintjobs/test/cancel-all', []);
        return response()->json($result);
    }

    public function jobsStatus(Request $request): JsonResponse
    {
        $runId = $request->input('runId', '');
        if ($runId === '') {
            return response()->json([]);
        }

        // El API .NET filtra con LIKE %needle% sobre ExternalJobId.
        // runId es "PRUEBA-{SLUG}-{ts}", que coincide con todos los jobs del run.
        $result = $this->api->get('api/printjobs?externalJobId=' . urlencode($runId) . '&limit=200');

        $mapped = array_map(function ($j) {
            $raw    = $j['status'] ?? $j['Status'] ?? null;
            $status = is_int($raw) || (is_string($raw) && ctype_digit($raw))
                ? (self::STATUS_NAMES[(int) $raw] ?? "Status{$raw}")
                : ($raw ?? 'Unknown');
            return [
                'externalJobId' => $j['externalJobId'] ?? $j['ExternalJobId'] ?? '',
                'status'        => $status,
                'printerId'     => $j['printerId'] ?? $j['PrinterId'] ?? null,
            ];
        }, is_array($result) ? $result : []);

        return response()->json($mapped);
    }
}
