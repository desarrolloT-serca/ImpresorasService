<?php

namespace App\Http\Controllers;

use App\Helpers\AuthHelper;
use App\Helpers\StoreFormat;
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
        $effectiveStore = AuthHelper::getEffectiveStoreId();
        $selectedStoreId = $effectiveStore !== null
            ? $effectiveStore
            : ($request->filled('storeId') ? $request->integer('storeId') : null);
        $isActiveFilter = $request->filled('isActive') ? $request->boolean('isActive') : null;

        $params = [];
        if ($effectiveStore !== null) {
            $params['storeId'] = $effectiveStore;
        }
        $path = 'api/routingrules' . (empty($params) ? '' : '?' . http_build_query($params));
        $rules = $this->api->get($path) ?? [];
        $printers = $this->api->get('api/printers') ?? [];
        $stores = $this->api->get('api/stores?isActive=true') ?? [];

        $rules = is_array($rules) ? $rules : [];
        $printers = is_array($printers) ? $printers : [];
        $stores = is_array($stores) ? $stores : [];

        $storesById = [];
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
            $storesById[(string) $storeId] = [
                'storeId' => $storeId,
                'storeName' => $storeName,
                'formattedStoreName' => StoreFormat::label($storeId, $storeName),
                'rulesCount' => 0,
                'activeCount' => 0,
                'inactiveCount' => 0,
                'rules' => [],
            ];
        }

        $globalKey = 'global';

        foreach ($rules as $rule) {
            $ruleStoreId = $rule['storeId'] ?? $rule['StoreId'] ?? null;
            $groupKey = ($ruleStoreId === null || (string) $ruleStoreId === '') ? $globalKey : (string) (int) $ruleStoreId;

            if ($groupKey === $globalKey) {
                if (!isset($storesById[$globalKey])) {
                    $storesById[$globalKey] = [
                        'storeId' => null,
                        'storeName' => 'Todas las tiendas',
                        'formattedStoreName' => 'Todas las tiendas',
                        'rulesCount' => 0,
                        'activeCount' => 0,
                        'inactiveCount' => 0,
                        'rules' => [],
                    ];
                }
            } elseif (!isset($storesById[$groupKey])) {
                $storeId = (int) $groupKey;
                $storeName = (string) ($rule['storeName'] ?? $rule['StoreName'] ?? ('Tienda ' . $storeId));
                $storesById[$groupKey] = [
                    'storeId' => $storeId,
                    'storeName' => $storeName,
                    'formattedStoreName' => StoreFormat::label($storeId, $storeName),
                    'rulesCount' => 0,
                    'activeCount' => 0,
                    'inactiveCount' => 0,
                    'rules' => [],
                ];
            }

            $isActive = (bool) ($rule['isActive'] ?? $rule['IsActive'] ?? false);
            $storesById[$groupKey]['rules'][] = $rule;
            $storesById[$groupKey]['rulesCount']++;
            if ($isActive) {
                $storesById[$groupKey]['activeCount']++;
            } else {
                $storesById[$groupKey]['inactiveCount']++;
            }
        }

        uasort($storesById, static function (array $left, array $right): int {
            if (($left['storeId'] ?? null) === null) {
                return -1;
            }
            if (($right['storeId'] ?? null) === null) {
                return 1;
            }

            return ((int) $left['storeId']) <=> ((int) $right['storeId']);
        });

        $selectedKey = $selectedStoreId !== null ? (string) (int) $selectedStoreId : null;
        if ($selectedKey === null || !isset($storesById[$selectedKey])) {
            $selectedKey = null;
            foreach ($storesById as $key => $group) {
                if ((int) ($group['rulesCount'] ?? 0) > 0) {
                    $selectedKey = (string) $key;
                    break;
                }
            }
            if ($selectedKey === null && count($storesById) > 0) {
                $selectedKey = (string) array_key_first($storesById);
            }
        }

        $selectedStoreGroup = $selectedKey !== null ? ($storesById[$selectedKey] ?? null) : null;
        $selectedRules = is_array($selectedStoreGroup['rules'] ?? null) ? $selectedStoreGroup['rules'] : [];
        if ($isActiveFilter !== null) {
            $selectedRules = array_values(array_filter($selectedRules, static function (array $rule) use ($isActiveFilter): bool {
                return (bool) ($rule['isActive'] ?? $rule['IsActive'] ?? false) === $isActiveFilter;
            }));
        }

        return view('reglas.index', [
            'rules' => $selectedRules,
            'rulesByStore' => array_values($storesById),
            'selectedStoreGroup' => $selectedStoreGroup,
            'selectedStoreId' => $selectedStoreGroup['storeId'] ?? null,
            'hasAnyRules' => count($rules) > 0,
            'isActiveFilter' => $request->query('isActive', ''),
            'printers' => $printers,
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
