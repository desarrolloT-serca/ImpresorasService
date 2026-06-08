<?php

namespace App\Helpers;

class StoreFormat
{
    public static function label(int|string|null $storeId, ?string $storeName = null): string
    {
        $rawId = trim((string) ($storeId ?? ''));
        $id = is_numeric($rawId) ? (int) $rawId : null;
        $idLabel = $id !== null && $id > 0 ? sprintf('%02d', $id) : ($rawId !== '' ? $rawId : '-');

        $name = trim((string) ($storeName ?? ''));
        if ($idLabel === '-' && $name === '') {
            return '-';
        }

        if ($name === '') {
            $name = $id !== null && $id > 0 ? 'Tienda ' . $idLabel : 'Sin nombre';
        }

        if ($id !== null && $id > 0) {
            $name = (string) preg_replace(
                '/^\s*#?\s*0*' . preg_quote((string) $id, '/') . '\s*(?:[-_:\x{2013}\x{2014}]|\s)\s*/u',
                '',
                $name,
                1
            );
            $name = trim($name);
            if ($name === '') {
                $name = 'Tienda ' . $idLabel;
            }
        }

        return $idLabel . ' - ' . $name;
    }
}
