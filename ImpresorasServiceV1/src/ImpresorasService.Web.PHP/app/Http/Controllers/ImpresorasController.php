<?php

namespace App\Http\Controllers;

use App\Helpers\AuthHelper;
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
        $params = [];
        $effectiveStore = AuthHelper::getEffectiveStoreId();
        if ($request->filled('storeId')) {
            $params['storeId'] = $request->integer('storeId');
        } elseif ($effectiveStore !== null) {
            $params['storeId'] = $effectiveStore;
        }
        if ($request->filled('isActive')) {
            // La API .NET espera bool en query: "true"/"false" (no 1/0).
            $raw = strtolower(trim((string) $request->input('isActive')));
            $params['isActive'] = in_array($raw, ['1', 'true', 'yes', 'on'], true) ? 'true' : 'false';
        }
        $path = 'api/printers' . (empty($params) ? '' : '?' . http_build_query($params));
        $printers = $this->api->get($path) ?? [];
        return view('impresoras.index', [
            'printers' => is_array($printers) ? $printers : [],
            'pingIntervalSeconds' => config('impresoras.ping_interval_seconds', 30),
        ]);
    }

    public function create(): View
    {
        $effectiveStore = AuthHelper::getEffectiveStoreId();
        return view('impresoras.form', [
            'printer' => null,
            'storeIdLocked' => $effectiveStore !== null,
            'effectiveStoreId' => $effectiveStore,
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
            return redirect()->route('impresoras.index')->with('success', 'Impresora creada.');
        } catch (\GuzzleHttp\Exception\RequestException $e) {
            return back()->withInput()->withErrors($this->extractApiErrors($e, 'printerName'));
        }
    }

    public function edit(int $impresora): View|RedirectResponse
    {
        try {
            $printer = $this->api->get("api/printers/{$impresora}");
            return view('impresoras.form', ['printer' => $printer]);
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
            return redirect()->route('impresoras.index')->with('success', 'Impresora actualizada.');
        } catch (\GuzzleHttp\Exception\RequestException $e) {
            return back()->withInput()->withErrors($this->extractApiErrors($e, 'printerName'));
        }
    }

    public function destroy(int $impresora): RedirectResponse
    {
        try {
            $this->api->delete("api/printers/{$impresora}");
            return redirect()->route('impresoras.index')->with('success', 'Impresora eliminada.');
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
