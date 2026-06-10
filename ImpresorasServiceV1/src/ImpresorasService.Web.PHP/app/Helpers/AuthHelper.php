<?php

namespace App\Helpers;

use Illuminate\Support\Facades\Session;

class AuthHelper
{
    public const ROLE_ADMIN = 'Admin';
    public const ROLE_STORE_MANAGER = 'StoreManager';
    public const ROLE_EMPLOYEE = 'Employee';
    public const ROLE_LEGACY_SUPERVISOR = 'Supervisor';

    public static function normalizeRole(?string $role): string
    {
        if (strcasecmp((string) $role, self::ROLE_ADMIN) === 0) {
            return self::ROLE_ADMIN;
        }

        if (
            strcasecmp((string) $role, self::ROLE_STORE_MANAGER) === 0
            || strcasecmp((string) $role, self::ROLE_LEGACY_SUPERVISOR) === 0
        ) {
            return self::ROLE_STORE_MANAGER;
        }

        return self::ROLE_EMPLOYEE;
    }

    public static function getRole(): string
    {
        $rawRole = self::getUser()['role'] ?? self::getUser()['Role'] ?? self::ROLE_EMPLOYEE;
        return self::normalizeRole($rawRole);
    }

    public static function getUser(): array
    {
        return Session::get('impresoras_user', []);
    }

    public static function isAdmin(): bool
    {
        return self::getRole() === self::ROLE_ADMIN;
    }

    public static function isStoreManager(): bool
    {
        return self::getRole() === self::ROLE_STORE_MANAGER;
    }

    public static function isEmployee(): bool
    {
        return self::getRole() === self::ROLE_EMPLOYEE;
    }

    public static function isStoreManagerOrAdmin(): bool
    {
        return self::isAdmin() || self::isStoreManager();
    }

    public static function getEffectiveStoreId(): ?int
    {
        $user = self::getUser();
        $storeId = $user['storeId'] ?? $user['StoreId'] ?? null;
        $role = self::getRole();

        if ($role !== self::ROLE_ADMIN) {
            return $storeId !== null ? (int) $storeId : null;
        }
        return Session::get('selected_store_id');
    }

    public static function roleLabel(?array $store = null): string
    {
        $role = self::getRole();
        if ($role === self::ROLE_ADMIN) {
            return 'Administrador';
        }

        if ($role === self::ROLE_STORE_MANAGER) {
            $storeNumber = $store['storeId'] ?? $store['StoreId'] ?? self::getEffectiveStoreId();
            return 'Jefe de tienda ' . ($storeNumber ?? '-');
        }

        return 'Empleado';
    }
}
