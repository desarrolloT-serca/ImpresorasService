<?php

namespace App\Http\Controllers;

use App\Helpers\AuthHelper;
use App\Services\ApiClient;
use Illuminate\Http\RedirectResponse;
use Illuminate\Http\Request;
use Illuminate\View\View;
use Illuminate\Http\JsonResponse;

class ReglasController extends Controller
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
        if ($request->filled('isActive')) $params['isActive'] = $request->boolean('isActive');
        $path = 'api/routingrules' . (empty($params) ? '' : '?' . http_build_query($params));
        $rules = $this->api->get($path) ?? [];
        $printers = $this->api->get('api/printers') ?? [];
        return view('reglas.index', [
            'rules' => is_array($rules) ? $rules : [],
            'printers' => is_array($printers) ? $printers : [],
        ]);
    }

    public function create(Request $request): View
    {
        $effectiveStore = AuthHelper::getEffectiveStoreId();

        $storeIdFromOld = $request->old('storeId');
        $selectedStoreId = ($effectiveStore !== null)
            ? $effectiveStore
            : ($storeIdFromOld !== null && (string)$storeIdFromOld !== '' ? (int)$storeIdFromOld : null);

        $printersPath = 'api/printers' . ($selectedStoreId !== null ? '?storeId=' . $selectedStoreId : '');
        $printers = $this->api->get($printersPath) ?? [];

        return view('reglas.form', [
            'rule' => null,
            'printers' => is_array($printers) ? $printers : [],
            'storeIdLocked' => $effectiveStore !== null,
            'effectiveStoreId' => $effectiveStore,
        ]);
    }

    public function store(Request $request): RedirectResponse
    {
        $request->validate([
            'priority' => 'required|integer|min:0',
            'storeId' => 'nullable|integer',
            'documentType' => 'nullable|string|max:255',
            'channel' => 'nullable|string|max:255',
            'printerId' => 'required|integer|min:1',
            'isActive' => 'boolean',
        ]);

        $effectiveStore = AuthHelper::getEffectiveStoreId();
        $storeId = $effectiveStore ?? ($request->filled('storeId') ? $request->integer('storeId') : null);
        $printerId = $request->integer('printerId');

        if ($storeId !== null) {
            $printer = $this->api->get("api/printers/{$printerId}");
            $printerStoreId = $printer['storeId'] ?? $printer['StoreId'] ?? null;

            if ($printerStoreId === null || (int)$printerStoreId !== (int)$storeId) {
                return back()->withInput()->withErrors([
                    'printerId' => 'La impresora seleccionada no pertenece a la tienda seleccionada.',
                ]);
            }
        }

        try {
            $this->api->post('api/routingrules', [
                'priority' => $request->integer('priority'),
                'storeId' => $storeId,
                'documentType' => $request->input('documentType') ?: null,
                'channel' => $request->input('channel') ?: null,
                'printerId' => $printerId,
                'isActive' => $request->boolean('isActive', true),
                'createdBy' => session('impresoras_user')['login'] ?? session('impresoras_user')['Login'] ?? 'laravel',
            ]);
            return redirect()->route('reglas.index')->with('success', 'Regla creada.');
        } catch (\GuzzleHttp\Exception\RequestException $e) {
            return back()->withInput()->withErrors($this->extractApiErrors($e, 'priority'));
        }
    }

    public function edit(int $regla, Request $request): View|RedirectResponse
    {
        try {
            $rule = $this->api->get("api/routingrules/{$regla}");

            $effectiveStore = AuthHelper::getEffectiveStoreId();
            $ruleStoreId = $rule['storeId'] ?? $rule['StoreId'] ?? null;

            $storeIdFromOld = $request->old('storeId');
            $selectedStoreId = ($effectiveStore !== null)
                ? $effectiveStore
                : ($storeIdFromOld !== null && (string)$storeIdFromOld !== '' ? (int)$storeIdFromOld : ($ruleStoreId !== null && (string)$ruleStoreId !== '' ? (int)$ruleStoreId : null));

            $printersPath = 'api/printers' . ($selectedStoreId !== null ? '?storeId=' . $selectedStoreId : '');
            $printers = $this->api->get($printersPath) ?? [];

            return view('reglas.form', [
                'rule' => $rule,
                'printers' => is_array($printers) ? $printers : [],
                'storeIdLocked' => $effectiveStore !== null,
                'effectiveStoreId' => $effectiveStore,
            ]);
        } catch (\Throwable) {
            return redirect()->route('reglas.index')->with('error', 'Regla no encontrada.');
        }
    }

    public function update(Request $request, int $regla): RedirectResponse
    {
        $request->validate([
            'priority' => 'required|integer|min:0',
            'storeId' => 'nullable|integer',
            'documentType' => 'nullable|string|max:255',
            'channel' => 'nullable|string|max:255',
            'printerId' => 'required|integer|min:1',
            'isActive' => 'boolean',
        ]);

        $rule = $this->api->get("api/routingrules/{$regla}");
        $validFrom = $rule['validFromUtc'] ?? $rule['ValidFromUtc'] ?? now()->toIso8601String();
        $validTo = $rule['validToUtc'] ?? $rule['ValidToUtc'] ?? null;

        $effectiveStore = AuthHelper::getEffectiveStoreId();
        $storeId = $effectiveStore ?? ($request->filled('storeId') ? $request->integer('storeId') : null);
        $printerId = $request->integer('printerId');

        if ($storeId !== null) {
            $printer = $this->api->get("api/printers/{$printerId}");
            $printerStoreId = $printer['storeId'] ?? $printer['StoreId'] ?? null;
            if ($printerStoreId === null || (int)$printerStoreId !== (int)$storeId) {
                return back()->withInput()->withErrors([
                    'printerId' => 'La impresora seleccionada no pertenece a la tienda seleccionada.',
                ]);
            }
        }

        try {
            $body = [
                'priority' => $request->integer('priority'),
                'storeId' => $storeId,
                'documentType' => $request->input('documentType') ?: null,
                'channel' => $request->input('channel') ?: null,
                'printerId' => $printerId,
                'isActive' => $request->boolean('isActive'),
                'validFromUtc' => $validFrom,
            ];
            if ($validTo !== null) $body['validToUtc'] = $validTo;
            $this->api->put("api/routingrules/{$regla}", $body);
            return redirect()->route('reglas.index')->with('success', 'Regla actualizada.');
        } catch (\GuzzleHttp\Exception\RequestException $e) {
            return back()->withInput()->withErrors($this->extractApiErrors($e, 'priority'));
        }
    }

    public function destroy(int $regla): RedirectResponse
    {
        try {
            $this->api->delete("api/routingrules/{$regla}");
            return redirect()->route('reglas.index')->with('success', 'Regla eliminada.');
        } catch (\GuzzleHttp\Exception\RequestException $e) {
            return back()->withErrors($this->extractApiErrors($e, 'form'));
        }
    }

    public function printersByStore(Request $request): JsonResponse
    {
        $storeIdRaw = $request->query('storeId');
        $storeId = null;
        if ($storeIdRaw !== null && (string)$storeIdRaw !== '') {
            $storeId = filter_var($storeIdRaw, FILTER_VALIDATE_INT, ['options' => ['min_range' => 1]]);
            $storeId = $storeId === false ? null : (int)$storeId;
        }

        $printersPath = 'api/printers' . ($storeId !== null ? '?storeId=' . $storeId : '');
        $printers = $this->api->get($printersPath) ?? [];

        return response()->json([
            'printers' => is_array($printers) ? $printers : [],
            'storeId' => $storeId,
        ]);
    }
}
