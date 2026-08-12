<?php

namespace App\Helpers;

class StatusLabels
{
    private const LABELS = [
        0 => 'Pendiente',
        1 => 'Enrutado',
        2 => 'Imprimiendo',
        3 => 'Aceptado',
        4 => 'Impreso',
        5 => 'Sin confirmar',
        6 => 'Reintento programado',
        7 => 'Cancelado',
        8 => 'Error',
        9 => 'Bloqueado',
    ];

    public static function get(int|string|null $status): string
    {
        $key = self::normalizeToInt($status);

        return $key === null ? '-' : (self::LABELS[$key] ?? '-');
    }

    public static function normalizeToInt(int|string|null $status): ?int
    {
        if ($status === null || $status === '') {
            return null;
        }

        if (is_numeric($status)) {
            return (int) $status;
        }

        return match (strtolower(trim((string) $status))) {
            'pending' => 0,
            'routed' => 1,
            'printing' => 2,
            'spoolaccepted' => 3,
            'printedconfirmed' => 4,
            'printedunknown' => 5,
            'retryscheduled' => 6,
            'cancelled', 'canceled' => 7,
            'errorfinal' => 8,
            'printerblocked' => 9,
            default => null,
        };
    }

    public static function all(): array
    {
        return self::LABELS;
    }
}
