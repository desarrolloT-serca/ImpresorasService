<?php

namespace App\Services;

use Illuminate\Support\Facades\Log;

/**
 * Cliente del endpoint agregado GET /api/dashboard/overview.
 */
class DashboardOverviewService
{
    private const KPI_KEYS = [
        'received',
        'printed',
        'failed',
        'queueCurrent',
        'failedWithoutRetryCurrent',
        'activePrinters',
        'activeStores',
    ];

    public function __construct(private readonly ApiClient $api)
    {
    }

    /**
     * @return array<string, mixed>|null null si la API no responde o el payload es invalido
     */
    public function fetch(?int $storeId, string $window): ?array
    {
        $params = ['window' => $window];
        if ($storeId !== null) {
            $params['storeId'] = $storeId;
        }

        $query = http_build_query($params);
        $path = 'api/dashboard/overview' . ($query !== '' ? '?' . $query : '');

        try {
            $response = $this->api->get($path);
            return is_array($response) && $response !== [] ? $response : null;
        } catch (\Throwable $e) {
            Log::warning('Dashboard overview no disponible', [
                'path' => $path,
                'error' => $e->getMessage(),
            ]);

            return null;
        }
    }

    /**
     * Compara KPIs legacy vs overview y registra diferencias (solo diagnostico).
     *
     * @param array<string, int|float> $legacyKpis
     */
    public function logKpiDiffIfAny(array $legacyKpis, ?int $storeId, string $window): void
    {
        $overview = $this->fetch($storeId, $window);
        if ($overview === null) {
            return;
        }

        $diffs = $this->diffKpis($legacyKpis, $overview);
        if ($diffs === []) {
            Log::debug('Dashboard overview: KPIs alineados con agregacion legacy', [
                'storeId' => $storeId,
                'window' => $window,
            ]);

            return;
        }

        Log::warning('Dashboard overview: diferencias KPI legacy vs API', [
            'storeId' => $storeId,
            'window' => $window,
            'diffs' => $diffs,
        ]);
    }

    /**
     * @param array<string, int|float> $legacyKpis
     * @return array<string, array{legacy:int, overview:int}>
     */
    public function diffKpis(array $legacyKpis, array $overview): array
    {
        $overviewKpis = $this->normalizeKpis($overview['kpis'] ?? $overview['Kpis'] ?? []);
        $diffs = [];

        foreach (self::KPI_KEYS as $key) {
            $legacy = (int) ($legacyKpis[$key] ?? 0);
            $fromOverview = (int) ($overviewKpis[$key] ?? 0);
            if ($legacy !== $fromOverview) {
                $diffs[$key] = ['legacy' => $legacy, 'overview' => $fromOverview];
            }
        }

        return $diffs;
    }

    /**
     * @param array<string, mixed> $raw
     * @return array<string, int>
     */
    private function normalizeKpis(array $raw): array
    {
        $normalized = [];
        foreach (self::KPI_KEYS as $key) {
            $pascal = ucfirst($key);
            $normalized[$key] = (int) ($raw[$key] ?? $raw[$pascal] ?? 0);
        }

        return $normalized;
    }
}
