<?php

namespace App\View\Composers;

use App\Helpers\AuthHelper;
use App\Services\ApiClient;
use Illuminate\Support\Facades\Cache;
use Illuminate\View\View;

class LayoutComposer
{
    public function __construct(private readonly ApiClient $api)
    {
    }

    public function compose(View $view): void
    {
        $user = session('impresoras_user', []);
        $role = AuthHelper::normalizeRole($user['role'] ?? $user['Role'] ?? AuthHelper::ROLE_EMPLOYEE);
        $storeId = $user['storeId'] ?? $user['StoreId'] ?? null;

        $isAdmin = $role === AuthHelper::ROLE_ADMIN;
        $isStoreManager = $role === AuthHelper::ROLE_STORE_MANAGER;
        $isEmployee = $role === AuthHelper::ROLE_EMPLOYEE;

        $selectedStoreId = session('selected_store_id');
        if (! $isAdmin && $storeId !== null) {
            $effectiveStoreId = $storeId;
        } else {
            $effectiveStoreId = $selectedStoreId;
        }

        $stores = [];
        if (session()->has('impresoras_token')) {
            try {
                $stores = Cache::store('file')->remember('layout_stores_active', 30, function () {
                    return $this->api->get('api/stores?isActive=true');
                });
            } catch (\Throwable) {
                try { $stores = $this->api->get('api/stores?isActive=true'); } catch (\Throwable) {}
            }
        }

        $stores = is_array($stores) ? $stores : [];
        $storeOptions = [];
        foreach ($stores as $store) {
            $id = $store['storeId'] ?? $store['StoreId'] ?? null;
            $name = $store['name'] ?? $store['Name'] ?? null;
            if ($id !== null) {
                $storeOptions[] = [
                    'storeId' => (int) $id,
                    'name' => is_string($name) && $name !== '' ? $name : ('Tienda ' . $id),
                ];
            }
        }
        usort($storeOptions, fn (array $a, array $b) => $a['storeId'] <=> $b['storeId']);

        $authRoleLabel = AuthHelper::roleLabel([
            'storeId' => $storeId,
        ]);

        $view->with([
            'authUser' => $user,
            'authRole' => $role,
            'authRoleLabel' => $authRoleLabel,
            'authStoreId' => $storeId,
            'isAdmin' => $isAdmin,
            'isStoreManager' => $isStoreManager,
            'isEmployee' => $isEmployee,
            'effectiveStoreId' => $effectiveStoreId,
            'selectedStoreId' => $selectedStoreId,
            'storeOptions' => $storeOptions,
            'apiError' => session(ApiClient::SESSION_API_ERROR_KEY),
        ]);
    }
}
