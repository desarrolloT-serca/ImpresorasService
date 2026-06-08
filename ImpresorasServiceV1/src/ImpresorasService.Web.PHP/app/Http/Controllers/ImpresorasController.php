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
        $isActiveFilter = $request->filled('isActive') ? $request->boolean('isActive') : null;

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
                'connectionErrorCount' => 0,
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
                    'connectionErrorCount' => 0,
                    'printers' => [],
                ];
            }

            $isActive = (bool) ($printer['isActive'] ?? $printer['IsActive'] ?? false);
            $lastConnectionOk = $printer['lastConnectionOk'] ?? $printer['LastConnectionOk'] ?? null;
            $hasConnectionError = $lastConnectionOk === false || trim((string) ($printer['lastConnectionError'] ?? $printer['LastConnectionError'] ?? '')) !== '';

            $printersByStore[$groupKey]['printers'][] = $printer;
            $printersByStore[$groupKey]['printersCount']++;
            if ($isActive) {
                $printersByStore[$groupKey]['activePrintersCount']++;
            }
            if ($hasConnectionError) {
                $printersByStore[$groupKey]['connectionErrorCount']++;
            }
        }

        uasort($printersByStore, static fn (array $left, array $right): int => ((int) $left['storeId']) <=> ((int) $right['storeId']));

        $selectedKey = $selectedStoreId !== null ? (string) (int) $selectedStoreId : null;
        if ($selectedKey === null || !isset($printersByStore[$selectedKey])) {
            $selectedKey = count($printersByStore) > 0 ? (string) array_key_first($printersByStore) : null;
        }

        $selectedStoreGroup = $selectedKey !== null ? ($printersByStore[$selectedKey] ?? null) : null;
        $selectedPrinters = is_array($selectedStoreGroup['printers'] ?? null) ? $selectedStoreGroup['printers'] : [];
        if ($isActiveFilter !== null) {
            $selectedPrinters = array_values(array_filter($selectedPrinters, static function (array $printer) use ($isActiveFilter): bool {
                return (bool) ($printer['isActive'] ?? $printer['IsActive'] ?? false) === $isActiveFilter;
            }));
        }

        return view('impresoras.index', [
            'printers' => $selectedPrinters,
            'printersByStore' => array_values($printersByStore),
            'selectedStoreGroup' => $selectedStoreGroup,
            'selectedStoreId' => $selectedStoreGroup['storeId'] ?? null,
            'hasAnyPrinters' => count($printers) > 0,
            'isActiveFilter' => $request->query('isActive', ''),
            'pingIntervalSeconds' => config('impresoras.ping_interval_seconds', 30),
        ]);
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

    public function netconnection(int $impresora): JsonResponse
    {
        try {
            $result = $this->api->post("api/printers/{$impresora}/netconnection", []);
            return response()->json($result);
        } catch (\GuzzleHttp\Exception\RequestException $e) {
            $body = $e->getResponse() ? json_decode((string) $e->getResponse()->getBody(), true) : [];
            return response()->json(['reachable' => false, 'error' => $body['error'] ?? $e->getMessage()], 500);
        }
    }
}
