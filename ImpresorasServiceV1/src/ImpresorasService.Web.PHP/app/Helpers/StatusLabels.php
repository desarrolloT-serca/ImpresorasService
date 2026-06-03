<?php

namespace App\Helpers;

class StatusLabels
{
    private const LABELS = [
        0 => 'Pendiente',
        1 => 'Enrutado',
        2 => 'Imprimiendo',
        3 => 'Aceptado',
        4 => 'Impreso confirmado',
        5 => 'Impreso sin confirmacion',
        6 => 'Reintento programado',
        7 => 'Cancelado',
        8 => 'Error final',
    ];

    public static function get(int|string|null $status): string
    {
        if ($status === null || $status === '') {
            return '-';
        }
        $key = is_numeric($status) ? (int) $status : null;
        return self::LABELS[$key] ?? '-';
    }

    public static function all(): array
    {
        return self::LABELS;
    }
}
