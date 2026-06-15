<?php

namespace App\Http\Controllers;

use App\Helpers\AuthHelper;
use App\Services\ApiClient;
use GuzzleHttp\Exception\RequestException;
use Illuminate\Http\RedirectResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\Log;
use Illuminate\Support\Facades\Storage;
use Illuminate\View\View;

class DashboardController extends Controller
{
    private const THRESHOLD_RULES_FILE = 'dashboard-threshold-rules.json';
    private const SEVERITIES = ['info', 'warning', 'critical'];
    private const RULE_METRICS = ['queue', 'failed', 'missingHost', 'conn'];

    public function __construct(private readonly ApiClient $api)
    {
    }

    public function index(Request $request)
    {
        $isAdminUser = AuthHelper::isAdmin();
        $effectiveStore = AuthHelper::getEffectiveStoreId();
        $isAdminDashboard = $isAdminUser && $effectiveStore === null;
        $window = $this->normalizeWindow((string) request()->query('window', 'today'));
        $tab = request()->query('tab', 'global');
        $health = request()->query('health', 'all');
        $showHealthy = request()->boolean('showHealthy', true);
        $storeSort = request()->query('storeSort', 'severity');
        $storeCols = max(1, min(4, (int) request()->query('storeCols', 4)));
        $autoRefreshSecondsRaw = (int) request()->query('autoRefresh', 30);
        $allowedRefresh = [0, 15, 30, 60, 120];
        $autoRefreshSeconds = in_array($autoRefreshSecondsRaw, $allowedRefresh, true) ? $autoRefreshSecondsRaw : 0;
        ['stores' => $storesRaw, 'thr' => $rawThresholdsResponse] =
            \GuzzleHttp\Promise\Utils::unwrap([
                'stores' => $this->api->getAsync('api/stores?isActive=true'),
                'thr'    => $this->api->getAsync('api/dashboard/thresholds'),
            ]);

        $thresholds = $this->resolveHealthThresholds($rawThresholdsResponse);
        $activeStoreIds = [];
        $storeNameById = [];
        foreach ($storesRaw as $storeRow) {
            $sid = (int) ($storeRow['storeId'] ?? $storeRow['StoreId'] ?? 0);
            $sname = trim((string) ($storeRow['name'] ?? $storeRow['Name'] ?? ''));
            if ($sid > 0) {
                $activeStoreIds[$sid] = true;
                if ($sname !== '') {
                    $storeNameById[$sid] = $sname;
                }
            }
        }

        // Si el admin tenia seleccionada una tienda ya inactiva/eliminada, limpiar filtro.
        if ($isAdminUser && $effectiveStore !== null && !isset($activeStoreIds[$effectiveStore])) {
            $effectiveStore = null;
            session()->forget('selected_store_id');
        }

        $isAdminDashboard = $isAdminUser && $effectiveStore === null;

        if (! $isAdminDashboard) {
            // Jefe/empleado o admin con tienda seleccionada: vista especifica de tienda.
            $tab = 'stores';
            $health = 'all';
            $showHealthy = true;
            $storeCols = 1;
            $storeSort = 'severity';
        }

        $jobsPath     = 'api/printjobs?limit=5000' . ($effectiveStore !== null ? '&storeId=' . $effectiveStore : '');
        $printersPath = 'api/printers?isActive=true' . ($effectiveStore !== null ? '&storeId=' . $effectiveStore : '');

        $batch2 = [
            'jobs'     => $this->api->getAsync($jobsPath),
            'printers' => $this->api->getAsync($printersPath),
        ];
        if ($isAdminDashboard) {
            $batch2['users'] = $this->api->getAsync('api/users');
        }
        $batch2Result = \GuzzleHttp\Promise\Utils::unwrap($batch2);
        $jobs     = $batch2Result['jobs'];
        $printers = $batch2Result['printers'];
        $users    = $isAdminDashboard ? ($batch2Result['users'] ?? []) : [];

        [$windowStart, $windowEnd] = $this->resolveWindowRange($window);
        $connWarnMin = (int) ($thresholds['connWarningFailuresMin'] ?? 2);
        $connCritMin = (int) ($thresholds['connCriticalFailuresMin'] ?? 3);
        $received = 0;
        $printed = 0;
        $failed = 0;
        $queueCurrent = 0;
        $failedWithoutRetryCurrent = 0;
        $storeStats = [];
        $printerStatsByStore = [];
        $unassignedQueueByStore = [];

        // Sembrar tiendas para dashboard:
        // - Admin global: todas las tiendas activas.
        // - Ambito tienda (jefe/empleado o admin filtrado): solo tienda efectiva.
        if ($effectiveStore !== null) {
            $storeStats[$effectiveStore] = $this->makeEmptyStore($effectiveStore, $storeNameById);
        } else {
            foreach (array_keys($activeStoreIds) as $storeId) {
                $storeStats[$storeId] = $this->makeEmptyStore($storeId, $storeNameById);
            }
        }

        foreach ($printers as $printer) {
            $storeId = (int) ($printer['storeId'] ?? $printer['StoreId'] ?? 0);
            $printerId = (int) ($printer['printerId'] ?? $printer['PrinterId'] ?? 0);
            if (!isset($storeStats[$storeId])) {
                $storeStats[$storeId] = $this->makeEmptyStore($storeId, $storeNameById);
            }

            $storeStats[$storeId]['connectedPrinters']++;
            $connectivityStatus = $this->printerConnectivityStatus($printer);
            $connectivitySeverity = $this->printerConnectivitySeverity($printer);
            if ($connectivityStatus === 'no_host') {
                $storeStats[$storeId]['printersMissingHost'] = ($storeStats[$storeId]['printersMissingHost'] ?? 0) + 1;
            }

            $streak = (int) ($printer['connectionFailuresStreak'] ?? $printer['ConnectionFailuresStreak'] ?? 0);
            $storeStats[$storeId]['printersConnMaxStreak'] = max((int) ($storeStats[$storeId]['printersConnMaxStreak'] ?? 0), $streak);
            if ($connectivitySeverity === 'critical' || $streak >= $connCritMin) {
                $storeStats[$storeId]['printersConnCritical'] = ($storeStats[$storeId]['printersConnCritical'] ?? 0) + 1;
            } elseif (($connectivitySeverity === 'warning' && $connectivityStatus !== 'no_host') || $streak >= $connWarnMin) {
                $storeStats[$storeId]['printersConnWarning'] = ($storeStats[$storeId]['printersConnWarning'] ?? 0) + 1;
            }

            $printerStatsByStore[$storeId][$printerId] = [
                'printerId' => $printerId,
                'printerName' => (string) ($printer['printerName'] ?? $printer['PrinterName'] ?? ('Impresora ' . $printerId)),
                'spoolQueue' => (string) ($printer['spoolQueue'] ?? $printer['SpoolQueue'] ?? '-'),
                'isActive' => (bool) ($printer['isActive'] ?? $printer['IsActive'] ?? false),
                'lastConnectionOk' => $printer['lastConnectionOk'] ?? $printer['LastConnectionOk'] ?? null,
                'lastConnectionCheckAtUtc' => (string) ($printer['lastConnectionCheckAtUtc'] ?? $printer['LastConnectionCheckAtUtc'] ?? ''),
                'lastConnectionTransport' => (string) ($printer['lastConnectionTransport'] ?? $printer['LastConnectionTransport'] ?? ''),
                'lastConnectionError' => (string) ($printer['lastConnectionError'] ?? $printer['LastConnectionError'] ?? ''),
                'connectionFailuresStreak' => (int) ($printer['connectionFailuresStreak'] ?? $printer['ConnectionFailuresStreak'] ?? 0),
                'connectivityStatus' => $connectivityStatus,
                'connectivityLabel' => (string) ($printer['connectivityLabel'] ?? $printer['ConnectivityLabel'] ?? $this->connectivityLabelFromStatus($connectivityStatus)),
                'connectivitySeverity' => $connectivitySeverity,
                'connectivityDetail' => (string) ($printer['connectivityDetail'] ?? $printer['ConnectivityDetail'] ?? $printer['lastConnectionError'] ?? $printer['LastConnectionError'] ?? ''),
                'queueCurrent' => 0,
                'failedWindow' => 0,
                'totalWindow' => 0,
            ];
        }

        foreach ($jobs as $job) {
            $status = $job['status'] ?? $job['Status'] ?? null;
            $attemptCount = (int) ($job['attemptCount'] ?? $job['AttemptCount'] ?? 0);
            $statusInt = $this->normalizePrintJobStatus($status);
            $isPrintedStatus = in_array($statusInt, [3, 4, 5], true);
            // Documento con señal de fallo: fallo final, reintento programado o impreso tras reintentos.
            $hasFailureSignal = $statusInt === 8 || $statusInt === 6 || ($isPrintedStatus && $attemptCount > 1);
            $storeId = (int) ($job['storeId'] ?? $job['StoreId'] ?? 0);
            $printerId = (int) ($job['printerId'] ?? $job['PrinterId'] ?? 0);
            $createdAtRaw = $job['createdAtUtc'] ?? $job['CreatedAtUtc'] ?? $job['created_at_utc'] ?? null;
            $updatedAtRaw = $job['updatedAtUtc'] ?? $job['UpdatedAtUtc'] ?? $job['updated_at_utc'] ?? $createdAtRaw;
            $createdAtTs = $this->parseUtcTimestamp($createdAtRaw);
            $updatedAtTs = $this->parseUtcTimestamp($updatedAtRaw);
            $createdInWindow = $this->isTimestampInWindow($createdAtTs, $windowStart, $windowEnd);
            $updatedInWindow = $this->isTimestampInWindow($updatedAtTs, $windowStart, $windowEnd);

            // El dashboard operativo solo debe mostrar tiendas activas.
            // Excluimos jobs historicos de tiendas inactivas/eliminadas.
            if (!isset($activeStoreIds[$storeId])) {
                continue;
            }

            if (!isset($storeStats[$storeId])) {
                $storeStats[$storeId] = $this->makeEmptyStore($storeId, $storeNameById);
            }

            if ($createdInWindow) {
                $received++;
                $storeStats[$storeId]['received']++;
                if ($printerId > 0) {
                    if (!isset($printerStatsByStore[$storeId][$printerId])) {
                        $printerStatsByStore[$storeId][$printerId] = [
                            'printerId' => $printerId,
                            'printerName' => 'Impresora ' . $printerId,
                            'spoolQueue' => '-',
                            'isActive' => false,
                            'lastConnectionOk' => null,
                            'lastConnectionCheckAtUtc' => '',
                            'lastConnectionTransport' => '',
                            'lastConnectionError' => '',
                            'connectionFailuresStreak' => 0,
                            'queueCurrent' => 0,
                            'failedWindow' => 0,
                            'totalWindow' => 0,
                        ];
                    }
                    $printerStatsByStore[$storeId][$printerId]['totalWindow'] = ($printerStatsByStore[$storeId][$printerId]['totalWindow'] ?? 0) + 1;
                }
            }

            if ($isPrintedStatus && $updatedInWindow) {
                $printed++;
                $storeStats[$storeId]['printed']++;
            }

            if ($hasFailureSignal && $updatedInWindow) {
                $failed++;
                $storeStats[$storeId]['failed']++;
                // "Sin reenviar": fallidos que siguen sin terminar impresos.
                if (!$isPrintedStatus && $statusInt !== 6) {
                    $failedWithoutRetryCurrent++;
                    $storeStats[$storeId]['failedWithoutRetryCurrent']++;
                    if ($printerId > 0) {
                        if (!isset($printerStatsByStore[$storeId][$printerId])) {
                            $printerStatsByStore[$storeId][$printerId] = [
                                'printerId' => $printerId,
                                'printerName' => 'Impresora ' . $printerId,
                                'spoolQueue' => '-',
                                'isActive' => false,
                                'lastConnectionOk' => null,
                                'lastConnectionCheckAtUtc' => '',
                                'lastConnectionTransport' => '',
                                'lastConnectionError' => '',
                                'connectionFailuresStreak' => 0,
                                'queueCurrent' => 0,
                                'failedWindow' => 0,
                                'totalWindow' => 0,
                            ];
                        }
                        $printerStatsByStore[$storeId][$printerId]['failedWindow'] = ($printerStatsByStore[$storeId][$printerId]['failedWindow'] ?? 0) + 1;
                    }
                }
            }

            if (in_array($statusInt, [0, 1, 2, 6], true)) {
                $queueCurrent++;
                $storeStats[$storeId]['queuedCurrent']++;
                if ($printerId > 0) {
                    if (!isset($printerStatsByStore[$storeId][$printerId])) {
                        $printerStatsByStore[$storeId][$printerId] = [
                            'printerId' => $printerId,
                            'printerName' => 'Impresora ' . $printerId,
                            'spoolQueue' => '-',
                            'isActive' => false,
                            'lastConnectionOk' => null,
                            'lastConnectionCheckAtUtc' => '',
                            'lastConnectionTransport' => '',
                            'lastConnectionError' => '',
                            'connectionFailuresStreak' => 0,
                            'queueCurrent' => 0,
                            'failedWindow' => 0,
                            'totalWindow' => 0,
                        ];
                    }
                    $printerStatsByStore[$storeId][$printerId]['queueCurrent'] = ($printerStatsByStore[$storeId][$printerId]['queueCurrent'] ?? 0) + 1;
                } else {
                    $unassignedQueueByStore[$storeId] = ($unassignedQueueByStore[$storeId] ?? 0) + 1;
                }
            }
        }

        foreach ($storeStats as &$row) {
            [$healthValue, $reason] = $this->computeHealth(
                (int) $row['connectedPrinters'],
                (int) $row['queuedCurrent'],
                (int) $row['failedWithoutRetryCurrent'],
                (int) ($row['printersMissingHost'] ?? 0),
                (int) ($row['printersConnWarning'] ?? 0),
                (int) ($row['printersConnCritical'] ?? 0),
                (int) ($row['printersConnMaxStreak'] ?? 0),
                $thresholds
            );
            $row['health'] = $healthValue;
            $row['healthReason'] = $reason;
            $row['unassignedQueueCurrent'] = $unassignedQueueByStore[$row['storeId']] ?? 0;

            $printersForStore = array_values($printerStatsByStore[$row['storeId']] ?? []);
            usort($printersForStore, fn (array $a, array $b): int => ($b['queueCurrent'] <=> $a['queueCurrent']) ?: strcmp($a['printerName'], $b['printerName']));
            $row['printers'] = $printersForStore;
        }
        unset($row);

        $stores = array_values($storeStats);
        usort($stores, function (array $a, array $b) use ($storeSort): int {
            if ($storeSort === 'name') {
                return strcmp((string) $a['storeName'], (string) $b['storeName']);
            }
            if ($storeSort === 'queue') {
                return ($b['queuedCurrent'] <=> $a['queuedCurrent']) ?: ($a['storeId'] <=> $b['storeId']);
            }

            $severityMap = ['critical' => 3, 'warning' => 2, 'healthy' => 1];
            $sa = $severityMap[$a['health'] ?? 'healthy'] ?? 1;
            $sb = $severityMap[$b['health'] ?? 'healthy'] ?? 1;
            return ($sb <=> $sa)
                ?: (($b['failedWithoutRetryCurrent'] ?? 0) <=> ($a['failedWithoutRetryCurrent'] ?? 0))
                ?: (($b['queuedCurrent'] ?? 0) <=> ($a['queuedCurrent'] ?? 0));
        });

        $alerts = $this->buildPrioritizedAlerts($stores, $thresholds);

        if (!$showHealthy) {
            $stores = array_values(array_filter($stores, fn (array $row): bool => ($row['health'] ?? 'healthy') !== 'healthy'));
        }

        $kpis = [
            'received' => $received,
            'printed' => $printed,
            'failed' => $failed,
            'queueCurrent' => $queueCurrent,
            'failedWithoutRetryCurrent' => $failedWithoutRetryCurrent,
            'activePrinters' => array_sum(array_map(fn (array $row): int => (int) ($row['connectedPrinters'] ?? 0), $stores)),
            'activeStores' => count(array_filter($stores, fn (array $row): bool => (int) ($row['connectedPrinters'] ?? 0) > 0)),
        ];
        $summary = [
            'storesTotal' => count($storeStats),
            'storesActive' => $kpis['activeStores'],
            'printersTotal' => is_array($printers) ? count($printers) : 0,
            'printersActive' => $kpis['activePrinters'],
            'usersTotal' => is_array($users) ? count($users) : 0,
        ];
        $generatedAtUtc = gmdate('c');

        if (!is_array($kpis)) {
            $kpis = [];
        }
        if (!is_array($alerts)) {
            $alerts = [];
        }
        if (!is_array($stores)) {
            $stores = [];
        }

        if (in_array($health, ['healthy', 'warning', 'critical'], true)) {
            $stores = array_values(array_filter($stores, fn (array $row): bool => ($row['health'] ?? '') === $health));
        }
        $viewName = $isAdminDashboard ? 'dashboard' : 'dashboard-local';

        $view = view($viewName, [
            'isAdminUser' => $isAdminUser,
            'isAdminDashboard' => $isAdminDashboard,
            'window' => $window,
            'tab' => $tab,
            'health' => $health,
            'showHealthy' => $showHealthy,
            'storeSort' => $storeSort,
            'storeCols' => $storeCols,
            'autoRefreshSeconds' => $autoRefreshSeconds,
            'thresholds' => $thresholds,
            'generatedAtUtc' => $generatedAtUtc,
            'kpis' => $kpis,
            'summary' => $summary,
            'alerts' => $alerts,
            'stores' => $stores,
        ]);

        return $request->ajax() && $viewName === 'dashboard'
            ? $view->fragment('dashboard-content')
            : $view;
    }

    public function ajustes(): View
    {
        $thresholds = $this->resolveHealthThresholds();

        return view('ajustes', [
            'thresholds' => $thresholds,
        ]);
    }

    public function updateThresholds(Request $request): RedirectResponse
    {
        $payload = $request->validate([
            'thresholdRules' => 'required|array',
            'thresholdRules.queue' => 'required|array|min:1|max:3',
            'thresholdRules.failed' => 'required|array|min:1|max:3',
            'thresholdRules.missingHost' => 'required|array|min:1|max:3',
            'thresholdRules.conn' => 'required|array|min:1|max:3',
            'thresholdRules.*.*.min' => 'required|integer|min:0',
            'thresholdRules.*.*.severity' => 'required|string|in:info,warning,critical',
        ]);

        [$rules, $ruleErrors] = $this->normalizeThresholdRules($payload['thresholdRules'] ?? []);
        if ($ruleErrors !== []) {
            return back()->withErrors(['thresholds' => implode(' ', $ruleErrors)])->withInput();
        }

        $legacyPayload = $this->deriveLegacyThresholdPayload($rules);

        try {
            $this->api->put('api/dashboard/thresholds', $legacyPayload);
            $this->storeDynamicThresholdRules($rules);

            return back()->with('success', 'Umbrales de salud actualizados.');
        } catch (\GuzzleHttp\Exception\RequestException $e) {
            return back()->withErrors($this->extractApiErrors($e, 'thresholds'))->withInput();
        } catch (\Throwable $e) {
            Log::error('No se pudieron guardar las reglas dinamicas de dashboard', ['error' => $e->getMessage()]);
            return back()->withErrors(['thresholds' => 'No se pudieron guardar las reglas de umbrales.'])->withInput();
        }
    }

    private function makeEmptyStore(int $storeId, array $storeNameById): array
    {
        $realName = trim((string) ($storeNameById[$storeId] ?? ''));
        return [
            'storeId' => $storeId,
            'storeName' => $realName !== '' ? $realName : ('Tienda ' . $storeId),
            'connectedPrinters' => 0,
            'received' => 0,
            'printed' => 0,
            'failed' => 0,
            'queuedCurrent' => 0,
            'failedWithoutRetryCurrent' => 0,
            'printersMissingHost' => 0,
            'printersConnWarning' => 0,
            'printersConnCritical' => 0,
            'printersConnMaxStreak' => 0,
            'health' => 'healthy',
            'healthReason' => 'Operacion dentro de umbrales',
            'printers' => [],
            'unassignedQueueCurrent' => 0,
        ];
    }

    /**
     * @return array{0:int,1:int}
     */
    private function resolveWindowRange(string $window): array
    {
        $timezone = new \DateTimeZone((string) config('app.timezone', 'Europe/Madrid'));
        $now = new \DateTimeImmutable('now', $timezone);
        $todayStart = new \DateTimeImmutable('today', $timezone);

        $start = match ($this->normalizeWindow($window)) {
            '7d' => $now->sub(new \DateInterval('P7D')),
            '30d' => $now->sub(new \DateInterval('P30D')),
            default => $todayStart,
        };

        return [$start->getTimestamp(), $now->getTimestamp()];
    }

    private function normalizeWindow(string $window): string
    {
        $normalized = strtolower(trim($window));

        return in_array($normalized, ['today', '7d', '30d'], true) ? $normalized : 'today';
    }

    private function isTimestampInWindow(int|false $timestamp, int $start, int $end): bool
    {
        return $timestamp !== false && $timestamp >= $start && $timestamp <= $end;
    }

    private function normalizePrintJobStatus(mixed $status): int
    {
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
            default => -1,
        };
    }

    private function printerConnectivityStatus(array $printer): string
    {
        $status = strtolower(trim((string) ($printer['connectivityStatus'] ?? $printer['ConnectivityStatus'] ?? '')));
        if (in_array($status, ['ok', 'no_host', 'unchecked', 'error', 'inactive'], true)) {
            return $status;
        }

        $isActive = (bool) ($printer['isActive'] ?? $printer['IsActive'] ?? false);
        $lastConnectionOk = $this->nullableBool($printer['lastConnectionOk'] ?? $printer['LastConnectionOk'] ?? null);
        $lastConnectionError = trim((string) ($printer['lastConnectionError'] ?? $printer['LastConnectionError'] ?? ''));
        $lastConnectionCheckAtUtc = trim((string) ($printer['lastConnectionCheckAtUtc'] ?? $printer['LastConnectionCheckAtUtc'] ?? ''));
        $connectionFailuresStreak = (int) ($printer['connectionFailuresStreak'] ?? $printer['ConnectionFailuresStreak'] ?? 0);

        if (! $isActive) {
            return 'inactive';
        }
        if ($this->isHostNotConfiguredError($lastConnectionError)) {
            return 'no_host';
        }
        if ($lastConnectionCheckAtUtc === '') {
            return 'unchecked';
        }
        if ($lastConnectionOk === true) {
            return 'ok';
        }
        if ($lastConnectionOk === false && ($connectionFailuresStreak > 0 || $lastConnectionError !== '')) {
            return 'error';
        }

        return 'unchecked';
    }

    private function printerConnectivitySeverity(array $printer): string
    {
        $severity = strtolower(trim((string) ($printer['connectivitySeverity'] ?? $printer['ConnectivitySeverity'] ?? '')));
        if (in_array($severity, ['healthy', 'warning', 'critical', 'neutral'], true)) {
            return $severity;
        }

        return match ($this->printerConnectivityStatus($printer)) {
            'ok' => 'healthy',
            'error' => 'critical',
            'inactive' => 'neutral',
            default => 'warning',
        };
    }

    private function connectivityLabelFromStatus(string $status): string
    {
        return match ($status) {
            'ok' => 'OK',
            'no_host' => 'Sin host',
            'error' => 'Error',
            'inactive' => 'Inactiva',
            default => 'No comprobada',
        };
    }

    private function nullableBool(mixed $value): ?bool
    {
        if ($value === null || $value === '') {
            return null;
        }

        if (is_bool($value)) {
            return $value;
        }

        return filter_var($value, FILTER_VALIDATE_BOOLEAN, FILTER_NULL_ON_FAILURE);
    }

    private function isHostNotConfiguredError(string $error): bool
    {
        $normalized = strtolower($error);

        return str_contains($normalized, 'sin host')
            || str_contains($normalized, 'host no configurado')
            || str_contains($normalized, 'host not configured')
            || str_contains($normalized, 'hostname no configurado')
            || trim($normalized) === 'host_not_configured';
    }

    private function parseUtcTimestamp(mixed $value): int|false
    {
        $raw = trim((string) $value);
        if ($raw === '') {
            return false;
        }

        try {
            $hasExplicitTimezone = (bool) preg_match('/(?:Z|[+-]\d{2}:?\d{2})$/', $raw);
            $date = $hasExplicitTimezone
                ? new \DateTimeImmutable($raw)
                : new \DateTimeImmutable($raw, new \DateTimeZone('UTC'));

            return $date->getTimestamp();
        } catch (\Throwable) {
            return false;
        }
    }

    private function computeHealth(
        int $connectedPrinters,
        int $queuedCurrent,
        int $failedWithoutRetryCurrent,
        int $printersMissingHost,
        int $printersConnWarning,
        int $printersConnCritical,
        int $printersConnMaxStreak,
        array $thresholds
    ): array
    {
        $rules = $thresholds['thresholdRules'] ?? $this->legacyRulesFromThresholds($thresholds);

        $toHealth = fn (string $sev): string => strtolower($sev) === 'critical'
            ? 'critical'
            : (strtolower($sev) === 'warning' ? 'warning' : 'healthy');

        $connRule = $this->matchThresholdRule($rules['conn'] ?? [], $printersConnMaxStreak);
        $queueRule = $this->matchThresholdRule($rules['queue'] ?? [], $queuedCurrent);
        $failedRule = $this->matchThresholdRule($rules['failed'] ?? [], $failedWithoutRetryCurrent);
        $missingHostRule = $this->matchThresholdRule($rules['missingHost'] ?? [], $printersMissingHost);

        if ($printersConnCritical > 0) {
            return ['critical', 'Impresora(s) sin conexion (conectividad)'];
        }
        if ($connRule && $connRule['severity'] === 'critical') {
            $sev = (string) $connRule['severity'];
            $health = $toHealth($sev);
            return [$health, ($sev === 'info' ? 'Info: ' : '') . 'Impresora(s) sin conexion (conectividad)'];
        }
        if ($connectedPrinters === 0 && $queuedCurrent > 0) {
            return ['critical', 'Hay cola pero no hay impresoras activas'];
        }
        if ($failedRule && $failedRule['severity'] === 'critical') {
            $sev = (string) $failedRule['severity'];
            $health = $toHealth($sev);
            return [$health, ($sev === 'info' ? 'Info: ' : '') . ('Acumula ' . $failedRule['min'] . ' o mas fallos sin reenviar')];
        }
        if ($queueRule && $queueRule['severity'] === 'critical') {
            $sev = (string) $queueRule['severity'];
            $health = $toHealth($sev);
            return [$health, ($sev === 'info' ? 'Info: ' : '') . ('Cola actual mayor o igual a ' . $queueRule['min'] . ' trabajos')];
        }
        if ($connectedPrinters === 0) {
            return ['warning', 'Sin impresoras activas en la tienda'];
        }
        if ($printersConnWarning > 0) {
            return ['warning', 'Impresora(s) pendientes de comprobacion (conectividad)'];
        }
        if ($connRule) {
            $sev = (string) $connRule['severity'];
            $health = $toHealth($sev);
            return [$health, ($sev === 'info' ? 'Info: ' : '') . 'Impresora(s) con fallos de conexion (conectividad)'];
        }
        if ($missingHostRule && $printersMissingHost > 0) {
            $sev = (string) $missingHostRule['severity'];
            $health = $toHealth($sev);
            return [$health, ($sev === 'info' ? 'Info: ' : '') . 'Impresora(s) sin host configurado'];
        }
        if ($failedRule) {
            $sev = (string) $failedRule['severity'];
            $health = $toHealth($sev);
            return [$health, ($sev === 'info' ? 'Info: ' : '') . 'Tiene fallos recientes sin reenviar'];
        }
        if ($queueRule) {
            $sev = (string) $queueRule['severity'];
            $health = $toHealth($sev);
            return [$health, ($sev === 'info' ? 'Info: ' : '') . ('Cola actual mayor o igual a ' . $queueRule['min'] . ' trabajos')];
        }

        return ['healthy', 'Operacion dentro de umbrales'];
    }

    private function resolveHealthThresholds(?array $rawApiResponse = null): array
    {
        $defaults = [
            'warningQueueMin' => 10,
            'criticalQueueMin' => 30,
            'warningFailedWithoutRetryMin' => 1,
            'criticalFailedWithoutRetryMin' => 5,
            'queueWarningSeverity' => 'warning',
            'queueCriticalSeverity' => 'critical',
            'failedWarningSeverity' => 'warning',
            'failedCriticalSeverity' => 'critical',
            'missingHostMin' => 1,
            'missingHostSeverity' => 'warning',
            'connWarningFailuresMin' => 2,
            'connCriticalFailuresMin' => 3,
            'connWarningSeverity' => 'warning',
            'connCriticalSeverity' => 'critical',
        ];
        $defaults['thresholdRules'] = $this->legacyRulesFromThresholds($defaults);

        try {
            $thresholds = $rawApiResponse ?? $this->api->get('api/dashboard/thresholds');
            if (!is_array($thresholds)) {
                return $defaults;
            }

            $resolved = [
                'warningQueueMin' => (int) ($thresholds['warningQueueMin'] ?? $thresholds['WarningQueueMin'] ?? $defaults['warningQueueMin']),
                'criticalQueueMin' => (int) ($thresholds['criticalQueueMin'] ?? $thresholds['CriticalQueueMin'] ?? $defaults['criticalQueueMin']),
                'warningFailedWithoutRetryMin' => (int) ($thresholds['warningFailedWithoutRetryMin'] ?? $thresholds['WarningFailedWithoutRetryMin'] ?? $defaults['warningFailedWithoutRetryMin']),
                'criticalFailedWithoutRetryMin' => (int) ($thresholds['criticalFailedWithoutRetryMin'] ?? $thresholds['CriticalFailedWithoutRetryMin'] ?? $defaults['criticalFailedWithoutRetryMin']),
                'queueWarningSeverity' => (string) ($thresholds['queueWarningSeverity'] ?? $thresholds['QueueWarningSeverity'] ?? $defaults['queueWarningSeverity']),
                'queueCriticalSeverity' => (string) ($thresholds['queueCriticalSeverity'] ?? $thresholds['QueueCriticalSeverity'] ?? $defaults['queueCriticalSeverity']),
                'failedWarningSeverity' => (string) ($thresholds['failedWarningSeverity'] ?? $thresholds['FailedWarningSeverity'] ?? $defaults['failedWarningSeverity']),
                'failedCriticalSeverity' => (string) ($thresholds['failedCriticalSeverity'] ?? $thresholds['FailedCriticalSeverity'] ?? $defaults['failedCriticalSeverity']),
                'missingHostMin' => (int) ($thresholds['missingHostMin'] ?? $thresholds['MissingHostMin'] ?? $defaults['missingHostMin']),
                'missingHostSeverity' => (string) ($thresholds['missingHostSeverity'] ?? $thresholds['MissingHostSeverity'] ?? $defaults['missingHostSeverity']),
                'connWarningFailuresMin' => (int) ($thresholds['connWarningFailuresMin'] ?? $thresholds['ConnWarningFailuresMin'] ?? $defaults['connWarningFailuresMin']),
                'connCriticalFailuresMin' => (int) ($thresholds['connCriticalFailuresMin'] ?? $thresholds['ConnCriticalFailuresMin'] ?? $defaults['connCriticalFailuresMin']),
                'connWarningSeverity' => (string) ($thresholds['connWarningSeverity'] ?? $thresholds['ConnWarningSeverity'] ?? $defaults['connWarningSeverity']),
                'connCriticalSeverity' => (string) ($thresholds['connCriticalSeverity'] ?? $thresholds['ConnCriticalSeverity'] ?? $defaults['connCriticalSeverity']),
            ];

            $resolved['thresholdRules'] = $this->loadDynamicThresholdRules() ?? $this->legacyRulesFromThresholds($resolved);

            return $resolved;
        } catch (\Throwable) {
            return $defaults;
        }
    }

    private function legacyRulesFromThresholds(array $thresholds): array
    {
        return [
            'queue' => $this->normalizeRuleSet([
                ['min' => (int) ($thresholds['warningQueueMin'] ?? 10), 'severity' => (string) ($thresholds['queueWarningSeverity'] ?? 'warning')],
                ['min' => (int) ($thresholds['criticalQueueMin'] ?? 30), 'severity' => (string) ($thresholds['queueCriticalSeverity'] ?? 'critical')],
            ]),
            'failed' => $this->normalizeRuleSet([
                ['min' => (int) ($thresholds['warningFailedWithoutRetryMin'] ?? 1), 'severity' => (string) ($thresholds['failedWarningSeverity'] ?? 'warning')],
                ['min' => (int) ($thresholds['criticalFailedWithoutRetryMin'] ?? 5), 'severity' => (string) ($thresholds['failedCriticalSeverity'] ?? 'critical')],
            ]),
            'missingHost' => $this->normalizeRuleSet([
                ['min' => (int) ($thresholds['missingHostMin'] ?? 1), 'severity' => (string) ($thresholds['missingHostSeverity'] ?? 'warning')],
            ]),
            'conn' => $this->normalizeRuleSet([
                ['min' => (int) ($thresholds['connWarningFailuresMin'] ?? 2), 'severity' => (string) ($thresholds['connWarningSeverity'] ?? 'warning')],
                ['min' => (int) ($thresholds['connCriticalFailuresMin'] ?? 3), 'severity' => (string) ($thresholds['connCriticalSeverity'] ?? 'critical')],
            ]),
        ];
    }

    private function normalizeRuleSet(array $rules): array
    {
        $normalized = [];
        foreach ($rules as $rule) {
            $min = max(0, (int) ($rule['min'] ?? 0));
            $severity = strtolower(trim((string) ($rule['severity'] ?? 'warning')));
            if (!in_array($severity, self::SEVERITIES, true)) {
                $severity = 'warning';
            }
            $normalized[] = ['min' => $min, 'severity' => $severity];
        }

        usort($normalized, fn (array $a, array $b): int => ($a['min'] <=> $b['min']) ?: (array_search($a['severity'], self::SEVERITIES, true) <=> array_search($b['severity'], self::SEVERITIES, true)));
        return array_values($normalized);
    }

    private function normalizeThresholdRules(array $input): array
    {
        $rules = [];
        $errors = [];

        foreach (self::RULE_METRICS as $metric) {
            $rawRows = array_values(is_array($input[$metric] ?? null) ? $input[$metric] : []);
            if ($rawRows === []) {
                $errors[] = 'Cada grupo debe tener al menos un umbral.';
                $rules[$metric] = [];
                continue;
            }
            if (count($rawRows) > count(self::SEVERITIES)) {
                $errors[] = 'Cada grupo permite como maximo tres umbrales.';
            }

            $seenSeverity = [];
            $seenMin = [];
            $metricRules = [];
            foreach ($rawRows as $row) {
                $min = max(0, (int) ($row['min'] ?? 0));
                $severity = strtolower(trim((string) ($row['severity'] ?? '')));
                if (!in_array($severity, self::SEVERITIES, true)) {
                    $errors[] = 'Hay una severidad no valida.';
                    continue;
                }
                if (isset($seenSeverity[$severity])) {
                    $errors[] = 'No puede repetirse la misma severidad dentro de un grupo.';
                    continue;
                }
                if (isset($seenMin[$min])) {
                    $errors[] = 'No puede repetirse el mismo valor de umbral dentro de un grupo.';
                    continue;
                }
                $seenSeverity[$severity] = true;
                $seenMin[$min] = true;
                $metricRules[] = ['min' => $min, 'severity' => $severity];
            }

            if ($metricRules !== [] && !$this->hasAscendingSeverityThresholds($metricRules)) {
                $errors[] = 'Los umbrales deben crecer con la severidad: Info < Warning < Critica.';
            }

            $rules[$metric] = $this->normalizeRuleSet($metricRules);
        }

        return [$rules, array_values(array_unique($errors))];
    }

    private function hasAscendingSeverityThresholds(array $rules): bool
    {
        $severityOrder = array_flip(self::SEVERITIES);

        usort($rules, function (array $a, array $b) use ($severityOrder): int {
            return ($severityOrder[$a['severity']] ?? 0) <=> ($severityOrder[$b['severity']] ?? 0);
        });

        $previousMin = null;
        foreach ($rules as $rule) {
            $min = (int) ($rule['min'] ?? 0);
            if ($previousMin !== null && $min <= $previousMin) {
                return false;
            }

            $previousMin = $min;
        }

        return true;
    }

    private function deriveLegacyThresholdPayload(array $rules): array
    {
        $queue = $this->deriveTwoLevelLegacy($rules['queue'] ?? [], 10, 30);
        $failed = $this->deriveTwoLevelLegacy($rules['failed'] ?? [], 1, 5);
        $conn = $this->deriveTwoLevelLegacy($rules['conn'] ?? [], 2, 3);
        $missingHost = $this->deriveSingleLevelLegacy($rules['missingHost'] ?? [], 1, 'warning');

        return [
            'warningQueueMin' => $queue['warningMin'],
            'criticalQueueMin' => $queue['criticalMin'],
            'queueWarningSeverity' => $queue['warningSeverity'],
            'queueCriticalSeverity' => $queue['criticalSeverity'],
            'warningFailedWithoutRetryMin' => $failed['warningMin'],
            'criticalFailedWithoutRetryMin' => $failed['criticalMin'],
            'failedWarningSeverity' => $failed['warningSeverity'],
            'failedCriticalSeverity' => $failed['criticalSeverity'],
            'missingHostMin' => $missingHost['min'],
            'missingHostSeverity' => $missingHost['severity'],
            'connWarningFailuresMin' => $conn['warningMin'],
            'connCriticalFailuresMin' => $conn['criticalMin'],
            'connWarningSeverity' => $conn['warningSeverity'],
            'connCriticalSeverity' => $conn['criticalSeverity'],
        ];
    }

    private function deriveTwoLevelLegacy(array $rules, int $defaultWarning, int $defaultCritical): array
    {
        $rules = $this->normalizeRuleSet($rules ?: [
            ['min' => $defaultWarning, 'severity' => 'warning'],
            ['min' => $defaultCritical, 'severity' => 'critical'],
        ]);
        $bySeverity = [];
        foreach ($rules as $rule) {
            $bySeverity[$rule['severity']] = $rule;
        }

        $warning = $bySeverity['warning'] ?? $bySeverity['info'] ?? $rules[0];
        $critical = $bySeverity['critical'] ?? $rules[count($rules) - 1] ?? ['min' => $warning['min'] + 1, 'severity' => $warning['severity']];

        $warningMin = (int) $warning['min'];
        $criticalMin = (int) $critical['min'];
        if ($criticalMin <= $warningMin) {
            $criticalMin = $warningMin + 1;
        }

        return [
            'warningMin' => $warningMin,
            'criticalMin' => $criticalMin,
            'warningSeverity' => (string) $warning['severity'],
            'criticalSeverity' => (string) $critical['severity'],
        ];
    }

    private function deriveSingleLevelLegacy(array $rules, int $defaultMin, string $defaultSeverity): array
    {
        $rules = $this->normalizeRuleSet($rules ?: [['min' => $defaultMin, 'severity' => $defaultSeverity]]);
        $rule = $rules[0] ?? ['min' => $defaultMin, 'severity' => $defaultSeverity];
        return ['min' => (int) $rule['min'], 'severity' => (string) $rule['severity']];
    }

    private function matchThresholdRule(array $rules, int $value): ?array
    {
        $rules = $this->normalizeRuleSet($rules);
        for ($i = count($rules) - 1; $i >= 0; $i--) {
            if ($value >= (int) $rules[$i]['min']) {
                return $rules[$i];
            }
        }

        return null;
    }

    private function loadDynamicThresholdRules(): ?array
    {
        try {
            if (!Storage::disk('local')->exists(self::THRESHOLD_RULES_FILE)) {
                return null;
            }
            $decoded = json_decode((string) Storage::disk('local')->get(self::THRESHOLD_RULES_FILE), true);
            if (!is_array($decoded)) {
                return null;
            }
            [$rules, $errors] = $this->normalizeThresholdRules($decoded);
            return $errors === [] ? $rules : null;
        } catch (\Throwable $e) {
            Log::warning('No se pudieron cargar las reglas dinamicas de dashboard', ['error' => $e->getMessage()]);
            return null;
        }
    }

    private function storeDynamicThresholdRules(array $rules): void
    {
        Storage::disk('local')->put(self::THRESHOLD_RULES_FILE, json_encode($rules, JSON_PRETTY_PRINT | JSON_UNESCAPED_SLASHES));
    }

    /**
     * Build prioritized alerts allowing multiple alerts per store.
     */
    private function buildPrioritizedAlerts(array $stores, array $thresholds): array
    {
        $rules = $thresholds['thresholdRules'] ?? $this->legacyRulesFromThresholds($thresholds);

        $toHealth = function (string $severity): string {
            $normalized = strtolower(trim($severity));
            if ($normalized === 'critical') {
                return 'critical';
            }
            if ($normalized === 'warning') {
                return 'warning';
            }
            return 'info';
        };

        $alerts = [];
        foreach ($stores as $row) {
            $storeId = (int) ($row['storeId'] ?? 0);
            $storeName = (string) ($row['storeName'] ?? ('Tienda ' . $storeId));
            $connected = (int) ($row['connectedPrinters'] ?? 0);
            $queue = (int) ($row['queuedCurrent'] ?? 0);
            $failedNoRetry = (int) ($row['failedWithoutRetryCurrent'] ?? 0);
            $missingHost = (int) ($row['printersMissingHost'] ?? 0);
            $connMaxStreak = (int) ($row['printersConnMaxStreak'] ?? 0);
            $connCritical = (int) ($row['printersConnCritical'] ?? 0);
            $connWarning = (int) ($row['printersConnWarning'] ?? 0);

            $connRule = $this->matchThresholdRule($rules['conn'] ?? [], $connMaxStreak);
            $failedRule = $this->matchThresholdRule($rules['failed'] ?? [], $failedNoRetry);
            $queueRule = $this->matchThresholdRule($rules['queue'] ?? [], $queue);
            $missingHostRule = $this->matchThresholdRule($rules['missingHost'] ?? [], $missingHost);

            if ($connCritical > 0 || $connWarning > 0 || $connRule) {
                $connAlertHealth = $connCritical > 0
                    ? 'critical'
                    : ($connRule ? $toHealth((string) $connRule['severity']) : 'warning');
                $alerts[] = [
                    'storeId' => $storeId,
                    'storeName' => $storeName,
                    'health' => $connAlertHealth,
                    'healthReason' => $connAlertHealth === 'critical'
                        ? 'Impresora(s) sin conexion (conectividad)'
                        : 'Impresora(s) pendientes de comprobacion (conectividad)',
                    'queuedCurrent' => $queue,
                    'failedWithoutRetryCurrent' => $failedNoRetry,
                ];
            }

            if ($connected === 0 && $queue > 0) {
                $alerts[] = [
                    'storeId' => $storeId,
                    'storeName' => $storeName,
                    'health' => 'critical',
                    'healthReason' => 'Hay cola pero no hay impresoras activas',
                    'queuedCurrent' => $queue,
                    'failedWithoutRetryCurrent' => $failedNoRetry,
                ];
            }

            if ($failedRule) {
                $alerts[] = [
                    'storeId' => $storeId,
                    'storeName' => $storeName,
                    'health' => $toHealth((string) $failedRule['severity']),
                    'healthReason' => 'Acumula ' . $failedRule['min'] . ' o mas fallos sin reenviar',
                    'queuedCurrent' => $queue,
                    'failedWithoutRetryCurrent' => $failedNoRetry,
                ];
            }

            if ($queueRule) {
                $alerts[] = [
                    'storeId' => $storeId,
                    'storeName' => $storeName,
                    'health' => $toHealth((string) $queueRule['severity']),
                    'healthReason' => 'Cola actual mayor o igual a ' . $queueRule['min'] . ' trabajos',
                    'queuedCurrent' => $queue,
                    'failedWithoutRetryCurrent' => $failedNoRetry,
                ];
            }

            if ($connected === 0) {
                $alerts[] = [
                    'storeId' => $storeId,
                    'storeName' => $storeName,
                    'health' => 'warning',
                    'healthReason' => 'Sin impresoras activas en la tienda',
                    'queuedCurrent' => $queue,
                    'failedWithoutRetryCurrent' => $failedNoRetry,
                ];
            }

            if ($missingHostRule && $missingHost > 0) {
                $alerts[] = [
                    'storeId' => $storeId,
                    'storeName' => $storeName,
                    'health' => $toHealth((string) $missingHostRule['severity']),
                    'healthReason' => 'Impresora(s) sin host configurado',
                    'queuedCurrent' => $queue,
                    'failedWithoutRetryCurrent' => $failedNoRetry,
                ];
            }
        }

        $severityMap = ['critical' => 3, 'warning' => 2, 'info' => 1, 'healthy' => 0];
        usort($alerts, function (array $a, array $b) use ($severityMap): int {
            $sa = $severityMap[$a['health'] ?? 'healthy'] ?? 0;
            $sb = $severityMap[$b['health'] ?? 'healthy'] ?? 0;

            return ($sb <=> $sa)
                ?: (($b['failedWithoutRetryCurrent'] ?? 0) <=> ($a['failedWithoutRetryCurrent'] ?? 0))
                ?: (($b['queuedCurrent'] ?? 0) <=> ($a['queuedCurrent'] ?? 0))
                ?: (($a['storeId'] ?? 0) <=> ($b['storeId'] ?? 0));
        });

        return $alerts;
    }
}
