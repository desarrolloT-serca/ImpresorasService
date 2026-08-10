<?php

namespace App\Http\Controllers;

use App\Services\ApiClient;
use Illuminate\Http\RedirectResponse;
use Illuminate\Http\Request;
use Illuminate\View\View;

class TiendasController extends Controller
{
    public function __construct(private readonly ApiClient $api)
    {
    }

    public function index(Request $request): View
    {
        $search = trim((string) $request->query('q', ''));

        try {
            $stores = $this->api->get('api/stores') ?? [];
        } catch (\Throwable) {
            return view('tiendas.index', [
                'stores'      => [],
                'allUsers'    => [],
                'allPrinters' => [],
            ])->with('error', 'No se pudieron cargar las tiendas desde la API.');
        }

        $stores = is_array($stores) ? $stores : [];

        if ($search !== '') {
            $needle = mb_strtolower($search);
            $stores = array_values(array_filter($stores, static function ($store) use ($needle) {
                $id   = (string) ($store['storeId'] ?? $store['StoreId'] ?? '');
                $name = (string) ($store['name']    ?? $store['Name']    ?? '');

                return mb_stripos($id, $needle) !== false
                    || mb_stripos($name, $needle) !== false;
            }));
        }

        try {
            $allUsers    = $this->api->get('api/users')    ?? [];
            $allPrinters = $this->api->get('api/printers') ?? [];
        } catch (\Throwable) {
            $allUsers    = [];
            $allPrinters = [];
        }

        return view('tiendas.index', [
            'stores'      => $stores,
            'allUsers'    => is_array($allUsers)    ? $allUsers    : [],
            'allPrinters' => is_array($allPrinters) ? $allPrinters : [],
        ]);
    }

    public function create(): View
    {
        return view('tiendas.form', ['store' => null]);
    }

    public function store(Request $request): RedirectResponse
    {
        $request->validate([
            'storeId' => 'required|integer|min:0',
            'name' => 'required|string|max:120',
            'isActive' => 'boolean',
        ]);

        try {
            $this->api->post('api/stores', [
                'storeId' => $request->integer('storeId'),
                'name' => $request->input('name'),
                'isActive' => $request->boolean('isActive', true),
            ]);
            return redirect()->route('tiendas.index')->with('success', 'Tienda creada.');
        } catch (\GuzzleHttp\Exception\RequestException $e) {
            return back()->withInput()->withErrors($this->extractApiErrors($e, 'storeId'));
        }
    }

    public function edit(int $tienda): View|RedirectResponse
    {
        try {
            $store = $this->api->get("api/stores/{$tienda}");
            return view('tiendas.form', ['store' => $store]);
        } catch (\Throwable) {
            return redirect()->route('tiendas.index')->with('error', 'Tienda no encontrada.');
        }
    }

    public function update(Request $request, int $tienda): RedirectResponse
    {
        $request->validate([
            'name' => 'required|string|max:120',
            'isActive' => 'boolean',
        ]);

        try {
            $this->api->put("api/stores/{$tienda}", [
                'name' => $request->input('name'),
                'isActive' => $request->boolean('isActive'),
            ]);
            return redirect()->route('tiendas.index')->with('success', 'Tienda actualizada.');
        } catch (\GuzzleHttp\Exception\RequestException $e) {
            return back()->withInput()->withErrors($this->extractApiErrors($e, 'name'));
        }
    }

    public function destroy(Request $request, int $tienda): RedirectResponse
    {
        $hardDelete = $request->boolean('hardDelete', false);
        $purgeHistory = $request->boolean('purgeHistory', false);
        $query = [];
        if ($hardDelete) {
            $query['hardDelete'] = 'true';
            if ($purgeHistory) {
                $query['purgeHistory'] = 'true';
            }
        }

        $apiPath = "api/stores/{$tienda}";
        if (!empty($query)) {
            $apiPath .= '?' . http_build_query($query);
        }

        try {
            $response = $this->api->delete($apiPath);
            $successMessage = (string) ($response['message'] ?? ($hardDelete ? 'Tienda eliminada definitivamente.' : 'Tienda desactivada.'));
            return redirect()->route('tiendas.index')->with('success', $successMessage);
        } catch (\GuzzleHttp\Exception\RequestException $e) {
            return back()->withErrors($this->extractApiErrors($e, 'form'));
        }
    }

    public function activate(int $tienda): RedirectResponse
    {
        try {
            $store = $this->api->get("api/stores/{$tienda}");
            $name = (string) ($store['name'] ?? $store['Name'] ?? '');
            if ($name === '') {
                return back()->withErrors(['form' => 'No se pudo activar la tienda: nombre no disponible.']);
            }

            $this->api->put("api/stores/{$tienda}", [
                'name' => $name,
                'isActive' => true,
            ]);

            return redirect()->route('tiendas.index')->with('success', 'Tienda activada.');
        } catch (\GuzzleHttp\Exception\RequestException $e) {
            return back()->withErrors($this->extractApiErrors($e, 'form'));
        }
    }
}
