<?php

namespace App\Helpers;

use Carbon\CarbonImmutable;

class DateTimeFormat
{
    public static function localDateTime(mixed $value, string $format = 'd/m H:i'): string
    {
        $raw = trim((string) $value);
        if ($raw === '') {
            return '-';
        }

        try {
            $hasExplicitTimezone = (bool) preg_match('/(?:Z|[+-]\d{2}:?\d{2})$/', $raw);
            $date = $hasExplicitTimezone
                ? CarbonImmutable::parse($raw)
                : CarbonImmutable::parse($raw, 'UTC');

            return $date->setTimezone(config('app.display_timezone', 'Europe/Madrid'))->format($format);
        } catch (\Throwable) {
            return '-';
        }
    }
}
