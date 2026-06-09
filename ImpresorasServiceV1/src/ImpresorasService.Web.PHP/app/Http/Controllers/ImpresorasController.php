<?php

namespace App\Http\Controllers;

use App\Helpers\AuthHelper;
use App\Helpers\StoreFormat;
use App\Services\ApiClient;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\RedirectResponse;
use Illuminate\Http\Request;
use Illuminate\View\View;

class ImpresorasController extends Controller
{
    public function __construct(private readonly ApiClient $api)
    {
    }

    public function index(Request $request): View
    {
        $effectiveStore = AuthHelper::getEffectiveStoreId();
        $selectedStoreId = $effectiveStore !== null
            ? $effectiveStore
            : ($request->filled('storeId') ? $request->integer('storeId') : null);

        $params = [];
        if ($effectiveStore !== null) {
            $params['storeId'] = $effectiveStore;
        }
        $path = 'api/printers' . (empty($params) ? '' : '?' . http_build_query($params));
        $printers = $this->api->get($path) ?? [];
        $stores = $this->api->get('api/stores?isActive=true') ?? [];

        $printers = is_array($printers) ? $printers : [];
        $stores = is_array($stores) ? $stores : [];

        $printersByStore = [];
        foreach ($stores as $store) {
            $storeId = $store['storeId'] ?? $store['StoreId'] ?? null;
            if ($storeId === null || (string) $storeId === '') {
                continue;
            }

            $storeId = (int) $storeId;
            if ($effectiveStore !== null && $storeId !== (int) $effectiveStore) {
                continue;
            }

            $storeName = (string) ($store['name'] ?? $store['Name'] ?? ('Tienda ' . $storeId));
            $printersByStore[(string) $storeId] = [
                'storeId' => $storeId,
                'storeName' => $storeName,
                'formattedStoreName' => StoreFormat::label($storeId, $storeName),
                'printersCount' => 0,
                'activePrintersCount' => 0,
                'errorPrintersCount' => 0,
                'warningPrintersCount' => 0,
                'uncheckedPrintersCount' => 0,
                'okPrintersCount' => 0,
                'connectionErrorCount' => 0,
                'visualStatus' => 'empty',
                'visualStatusLabel' => 'WARNING',
                'printers' => [],
            ];
        }

        foreach ($printers as $printer) {
            $printerStoreId = $printer['storeId'] ?? $printer['StoreId'] ?? null;
            if ($printerStoreId === null || (string) $printerStoreId === '') {
                continue;
            }

            $storeId = (int) $printerStoreId;
            $groupKey = (string) $storeId;
            if (!isset($printersByStore[$groupKey])) {
                $storeName = (string) ($printer['storeName'] ?? $printer['StoreName'] ?? ('Tienda ' . $storeId));
                $printersByStore[$groupKey] = [
                    'storeId' => $storeId,
                    'storeName' => $storeName,
                    'formattedStoreName' => StoreFormat::label($storeId, $storeName),
                    'printersCount' => 0,
                    'activePrintersCount' => 0,
                    'errorPrintersCount' => 0,
                    'warningPrintersCount' => 0,
                    'uncheckedPrintersCount' => 0,
                    'okPrintersCount' => 0,
                    'connectionErrorCount' => 0,
                    'visualStatus' => 'empty',
                    'visualStatusLabel' => 'WARNING',
                    'printers' => [],
                ];
            }

            $isActive = (bool) ($printer['isActive'] ?? $printer['IsActive'] ?? false);
            $connectivitySeverity = $this->printerConnectivitySeverity($printer);
            $connectivityStatus = $this->printerConnectivityStatus($printer);
            $hasConnectionError = $isActive && $connectivitySeverity === 'critical';
            $hasConnectionWarning = $isActive && $connectivitySeverity === 'warning';
            $isUnchecked = $isActive && $connectivityStatus === 'unchecked';
            $isOk = $isActive && $connectivitySeverity === 'healthy';

            $printersByStore[$groupKey]['printers'][] = $printer;
            $printersByStore[$groupKey]['printersCount']++;
            if ($isActive) {
                $printersByStore[$groupKey]['activePrintersCount']++;
            }
            if ($hasConnectionError) {
                $printersByStore[$groupKey]['errorPrintersCount']++;
                $printersByStore[$groupKey]['connectionErrorCount']++;
            }
            if ($hasConnectionWarning) {
                $printersByStore[$groupKey]['warningPrintersCount']++;
            }
            if ($isUnchecked) {
                $printersByStore[$groupKey]['uncheckedPrintersCount']++;
            }
            if ($isOk) {
                $printersByStore[$groupKey]['okPrintersCount']++;
            }
        }

        foreach ($printersByStore as &$storeGroup) {
            $storeGroup['visualStatus'] = $this->printerStoreVisualStatus($storeGroup);
            $storeGroup['visualStatusLabel'] = match ($storeGroup['visualStatus']) {
                'error' => 'ERROR',
                'empty' => 'SIN IMPRESORAS',
                'ok' => 'OK',
                default => 'WARNING',
            };
        }
        unset($storeGroup);

        uasort($printersByStore, static function (array $left, array $right): int {
            $priority = ['error' => 0, 'warning' => 1, 'empty' => 1, 'ok' => 2];
            $leftPriority = $priority[$left['visualStatus'] ?? 'ok'] ?? 2;
            $rightPriority = $priority[$right['visualStatus'] ?? 'ok'] ?? 2;

            return $leftPriority === $rightPriority
                ? ((int) $left['storeId']) <=> ((int) $right['storeId'])
                : $leftPriority <=> $rightPriority;
        });

        $selectedKey = $selectedStoreId !== null ? (string) (int) $selectedStoreId : null;
        if ($selectedKey === null || !isset($printersByStore[$selectedKey])) {
            $selectedKey = count($printersByStore) > 0 ? (string) array_key_first($printersByStore) : null;
        }

        $selectedStoreGroup = $selectedKey !== null ? ($printersByStore[$selectedKey] ?? null) : null;
        $selectedPrinters = is_array($selectedStoreGroup['printers'] ?? null) ? $selectedStoreGroup['printers'] : [];

        return view('impresoras.index', [
            'printers' => $selectedPrinters,
            'printersByStore' => array_values($printersByStore),
            'selectedStoreGroup' => $selectedStoreGroup,
            'selectedStoreId' => $selectedStoreGroup['storeId'] ?? null,
            'hasAnyPrinters' => count($printers) > 0,
            'pingIntervalSeconds' => config('impresoras.ping_interval_seconds', 30),
        ]);
    }

    private function nullableBool(mixed $value): ?bool
    {
        if ($value === null || $value === '') {
            return null;
        }

        if (is_bool($value)) {
            return $value;
        }

        return filter_var($value, FILTER_VALIDATE_BOOLEAN, FILTER_NULL_ON_FAILURE);
    }

    private function printerConnectivityStatus(array $printer): string
    {
        $status = strtolower(trim((string) ($printer['connectivityStatus'] ?? $printer['ConnectivityStatus'] ?? '')));
        if (in_array($status, ['ok', 'no_host', 'unchecked', 'error', 'inactive'], true)) {
            return $status;
        }

        $isActive = (bool) ($printer['isActive'] ?? $printer['IsActive'] ?? false);
        $lastConnectionOk = $this->nullableBool($printer['lastConnectionOk'] ?? $printer['LastConnectionOk'] ?? null);
        $lastConnectionError = trim((string) ($printer['lastConnectionError'] ?? $printer['LastConnectionError'] ?? ''));
        $lastConnectionCheckAtUtc = trim((string) ($printer['lastConnectionCheckAtUtc'] ?? $printer['LastConnectionCheckAtUtc'] ?? ''));
        $connectionFailuresStreak = (int) ($printer['connectionFailuresStreak'] ?? $printer['ConnectionFailuresStreak'] ?? 0);

        if (! $isActive) {
            return 'inactive';
        }
        if ($this->isHostNotConfiguredError($lastConnectionError)) {
            return 'no_host';
        }
        if ($lastConnectionCheckAtUtc === '') {
            return 'unchecked';
        }
        if ($lastConnectionOk === true) {
            return 'ok';
        }
        if ($lastConnectionOk === false && ($connectionFailuresStreak > 0 || $lastConnectionError !== '')) {
            return 'error';
        }

        return 'unchecked';
    }

    private function printerConnectivitySeverity(array $printer): string
    {
        $severity = strtolower(trim((string) ($printer['connectivitySeverity'] ?? $printer['ConnectivitySeverity'] ?? '')));
        if (in_array($severity, ['healthy', 'warning', 'critical', 'neutral'], true)) {
            return $severity;
        }

        return match ($this->printerConnectivityStatus($printer)) {
            'ok' => 'healthy',
            'error' => 'critical',
            'inactive' => 'neutral',
            default => 'warning',
        };
    }

    private function isHostNotConfiguredError(string $error): bool
    {
        $normalized = strtolower($error);

        return str_contains($normalized, 'sin host')
            || str_contains($normalized, 'host no configurado')
            || str_contains($normalized, 'host not configured')
            || str_contains($normalized, 'hostname no configurado')
            || trim($normalized) === 'host_not_configured';
    }

    private function printerStoreVisualStatus(array $storeGroup): string
    {
        $printersCount = (int) ($storeGroup['printersCount'] ?? 0);
        $activePrintersCount = (int) ($storeGroup['activePrintersCount'] ?? 0);
        $errorPrintersCount = (int) ($storeGroup['errorPrintersCount'] ?? 0);
        $warningPrintersCount = (int) ($storeGroup['warningPrintersCount'] ?? $storeGroup['uncheckedPrintersCount'] ?? 0);

        if ($printersCount === 0) {
            return 'empty';
        }

        if ($errorPrintersCount > 0) {
            return 'error';
        }

        if ($activePrintersCount === 0 || $warningPrintersCount > 0) {
            return 'warning';
        }

        return 'ok';
    }

    public function create(Request $request): View
    {
        $effectiveStore = AuthHelper::getEffectiveStoreId();
        $storeIdFromOld = $request->old('storeId');
        $storeIdFromRequest = $request->filled('storeId') ? $request->integer('storeId') : null;
        $selectedStoreId = ($effectiveStore !== null)
            ? $effectiveStore
            : ($storeIdFromOld !== null && (string) $storeIdFromOld !== '' ? (int) $storeIdFromOld : $storeIdFromRequest);

        return view('impresoras.form', [
            'printer' => null,
            'storeIdLocked' => $effectiveStore !== null,
            'effectiveStoreId' => $effectiveStore,
            'selectedStoreId' => $selectedStoreId,
        ]);
    }

    public function store(Request $request): RedirectResponse
    {
        $effectiveStore = AuthHelper::getEffectiveStoreId();
        $storeId = $effectiveStore ?? $request->integer('storeId');

        $request->validate([
            'printerName' => 'required|string|max:255',
            'spoolQueue' => 'required|string|max:255',
            'host' => 'nullable|string|max:255',
            'storeId' => $effectiveStore === null ? 'required|integer|min:1' : 'nullable',
            'isActive' => 'boolean',
        ]);

        try {
            $host = $request->filled('host') ? $request->input('host') : null;
            $this->api->post('api/printers', [
                'printerName' => $request->input('printerName'),
                'spoolQueue' => $request->input('spoolQueue'),
                'host' => $host,
                'storeId' => $storeId,
                'isActive' => $request->boolean('isActive', true),
            ]);
            return redirect()->route('impresoras.index', ['storeId' => $storeId])->with('success', 'Impresora creada.');
        } catch (\GuzzleHttp\Exception\RequestException $e) {
            return back()->withInput()->withErrors($this->extractApiErrors($e, 'printerName'));
        }
    }

    public function edit(Request $request, int $impresora): View|RedirectResponse
    {
        try {
            $printer = $this->api->get("api/printers/{$impresora}");
            $effectiveStore = AuthHelper::getEffectiveStoreId();
            $printerStoreId = $printer['storeId'] ?? $printer['StoreId'] ?? null;
            $selectedStoreId = $effectiveStore
                ?? ($request->filled('storeId') ? $request->integer('storeId') : null)
                ?? $printerStoreId;

            return view('impresoras.form', [
                'printer' => $printer,
                'storeIdLocked' => $effectiveStore !== null,
                'effectiveStoreId' => $effectiveStore,
                'selectedStoreId' => $selectedStoreId,
            ]);
        } catch (\Throwable) {
            return redirect()->route('impresoras.index')->with('error', 'Impresora no encontrada.');
        }
    }

    public function update(Request $request, int $impresora): RedirectResponse
    {
        $request->validate([
            'printerName' => 'required|string|max:255',
            'spoolQueue' => 'required|string|max:255',
            'host' => 'nullable|string|max:255',
            'storeId' => 'required|integer|min:1',
            'isActive' => 'boolean',
        ]);

        try {
            $host = $request->filled('host') ? $request->input('host') : null;
            $this->api->put("api/printers/{$impresora}", [
                'printerName' => $request->input('printerName'),
                'spoolQueue' => $request->input('spoolQueue'),
                'host' => $host,
                'storeId' => $request->integer('storeId'),
                'isActive' => $request->boolean('isActive'),
            ]);
            return redirect()->route('impresoras.index', ['storeId' => $request->integer('storeId')])->with('success', 'Impresora actualizada.');
        } catch (\GuzzleHttp\Exception\RequestException $e) {
            return back()->withInput()->withErrors($this->extractApiErrors($e, 'printerName'));
        }
    }

    public function destroy(Request $request, int $impresora): RedirectResponse
    {
        try {
            $this->api->delete("api/printers/{$impresora}");
            $storeId = $request->integer('storeId') ?: null;
            return redirect()->route('impresoras.index', array_filter(['storeId' => $storeId]))->with('success', 'Impresora eliminada.');
        } catch (\GuzzleHttp\Exception\RequestException $e) {
            return back()->withErrors($this->extractApiErrors($e, 'form'));
        }
    }

    public function ping(int $impresora): JsonResponse
    {
        try {
            $result = $this->api->post("api/printers/{$impresora}/ping", []);
            return response()->json($result);
        } catch (\GuzzleHttp\Exception\RequestException $e) {
            $body = $e->getResponse() ? json_decode((string) $e->getResponse()->getBody(), true) : [];
            return response()->json(['reachable' => false, 'error' => $body['error'] ?? $e->getMessage()], 500);
        }
    }

    public function netconnection(Request $request, int $impresora): JsonResponse
    {
        try {
            $path = "api/printers/{$impresora}/netconnection";
            if ($request->boolean('persist')) {
                $path .= '?persist=true';
            }

            $result = $this->api->post($path, []);
            return response()->json($result);
        } catch (\GuzzleHttp\Exception\RequestException $e) {
            $body = $e->getResponse() ? json_decode((string) $e->getResponse()->getBody(), true) : [];
            return response()->json(['reachable' => false, 'error' => $body['error'] ?? $e->getMessage()], 500);
        }
    }
}
