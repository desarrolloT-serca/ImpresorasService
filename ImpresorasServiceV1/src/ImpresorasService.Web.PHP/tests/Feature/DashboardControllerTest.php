<?php

namespace Tests\Feature;

use App\Services\ApiClient;
use GuzzleHttp\Client;
use GuzzleHttp\Promise\Create;
use GuzzleHttp\Promise\PromiseInterface;
use Tests\TestCase;

class DashboardControllerTest extends TestCase
{
    public function test_dashboard_uses_overview_totals_when_printjobs_list_is_partial(): void
    {
        $api = new class extends ApiClient
        {
            /** @var list<string> */
            public array $paths = [];

            public function __construct()
            {
                parent::__construct(new Client(['base_uri' => 'http://api.test/']), 'http://api.test');
            }

            public function getAsync(string $path): PromiseInterface
            {
                $this->paths[] = $path;

                return Create::promiseFor($this->responseFor($path));
            }

            public function get(string $path): array
            {
                $this->paths[] = $path;

                return $this->responseFor($path);
            }

            public function getQuiet(string $path): array
            {
                $this->paths[] = $path;

                return $this->responseFor($path);
            }

            /**
             * @return array<int|string, mixed>
             */
            private function responseFor(string $path): array
            {
                return match ($path) {
                    'api/stores?isActive=true' => [
                        ['storeId' => 10, 'name' => 'Tienda 10'],
                    ],
                    'api/dashboard/thresholds' => [],
                    'api/printjobs?limit=500' => [],
                    'api/printers?isActive=true' => [
                        ['printerId' => 99, 'storeId' => 10, 'printerName' => 'Caja', 'isActive' => true],
                    ],
                    'api/users' => [],
                    'api/dashboard/overview?window=today' => [
                        'kpis' => [
                            'received' => 37,
                            'printed' => 30,
                            'failed' => 4,
                            'queueCurrent' => 3,
                            'failedWithoutRetryCurrent' => 2,
                            'activePrinters' => 1,
                            'activeStores' => 1,
                        ],
                        'stores' => [
                            [
                                'storeId' => 10,
                                'storeName' => 'Tienda 10',
                                'connectedPrinters' => 1,
                                'received' => 37,
                                'printed' => 30,
                                'failed' => 4,
                                'queuedCurrent' => 3,
                                'failedWithoutRetryCurrent' => 2,
                                'health' => 'warning',
                                'healthReason' => '2 trabajos fallidos sin reenviar',
                            ],
                        ],
                    ],
                    default => [],
                };
            }
        };

        $this->app->instance(ApiClient::class, $api);

        $response = $this
            ->withSession([
                'impresoras_token' => 'token',
                'impresoras_user' => ['role' => 'Admin', 'login' => 'admin'],
            ])
            ->get('/');

        $response->assertOk();
        $response->assertViewHas('kpis', function (array $kpis): bool {
            return $kpis['received'] === 37
                && $kpis['printed'] === 30
                && $kpis['failed'] === 4
                && $kpis['queueCurrent'] === 3
                && $kpis['failedWithoutRetryCurrent'] === 2;
        });
        $response->assertViewHas('stores', function (array $stores): bool {
            $store = $stores[0] ?? [];

            return ($store['storeId'] ?? null) === 10
                && ($store['received'] ?? null) === 37
                && ($store['printed'] ?? null) === 30
                && ($store['failed'] ?? null) === 4
                && ($store['queuedCurrent'] ?? null) === 3
                && ($store['failedWithoutRetryCurrent'] ?? null) === 2;
        });
    }

    /**
     * VAL-P1-004 / F2.1: los chips por impresora y la cola sin asignar deben salir del overview,
     * no del fetch legacy `api/printjobs?limit=500` que aquí se simula vacío como si hubiera sido
     * truncado antes de alcanzar los jobs de esta tienda.
     */
    public function test_dashboard_uses_overview_printer_chips_when_printjobs_list_is_partial(): void
    {
        $api = new class extends ApiClient
        {
            public function __construct()
            {
                parent::__construct(new Client(['base_uri' => 'http://api.test/']), 'http://api.test');
            }

            public function getAsync(string $path): PromiseInterface
            {
                return Create::promiseFor($this->responseFor($path));
            }

            public function get(string $path): array
            {
                return $this->responseFor($path);
            }

            public function getQuiet(string $path): array
            {
                return $this->responseFor($path);
            }

            /**
             * @return array<int|string, mixed>
             */
            private function responseFor(string $path): array
            {
                return match ($path) {
                    'api/stores?isActive=true' => [
                        ['storeId' => 10, 'name' => 'Tienda 10'],
                    ],
                    'api/dashboard/thresholds' => [],
                    // Simula el fetch legacy truncado: sin jobs, aunque en BD haya 600 en cola.
                    'api/printjobs?limit=500' => [],
                    'api/printers?isActive=true' => [
                        ['printerId' => 99, 'storeId' => 10, 'printerName' => 'Caja', 'isActive' => true],
                    ],
                    'api/users' => [],
                    'api/dashboard/overview?window=today' => [
                        'kpis' => [
                            'received' => 600, 'printed' => 0, 'failed' => 0,
                            'queueCurrent' => 600, 'failedWithoutRetryCurrent' => 0,
                            'activePrinters' => 1, 'activeStores' => 1,
                        ],
                        'stores' => [
                            [
                                'storeId' => 10,
                                'storeName' => 'Tienda 10',
                                'connectedPrinters' => 1,
                                'received' => 600,
                                'printed' => 0,
                                'failed' => 0,
                                'queuedCurrent' => 600,
                                'failedWithoutRetryCurrent' => 0,
                                'health' => 'critical',
                                'healthReason' => 'Cola actual mayor o igual a 30 trabajos',
                                'unassignedQueueCurrent' => 3,
                                'printers' => [
                                    ['printerId' => 99, 'queueCurrent' => 597, 'failedWindow' => 0, 'totalWindow' => 600],
                                ],
                            ],
                        ],
                    ],
                    default => [],
                };
            }
        };

        $this->app->instance(ApiClient::class, $api);

        $response = $this
            ->withSession([
                'impresoras_token' => 'token',
                'impresoras_user' => ['role' => 'Admin', 'login' => 'admin'],
            ])
            ->get('/');

        $response->assertOk();
        $response->assertViewHas('stores', function (array $stores): bool {
            $store = $stores[0] ?? [];
            $printer = $store['printers'][0] ?? [];

            return ($store['queuedCurrent'] ?? null) === 600
                && ($store['unassignedQueueCurrent'] ?? null) === 3
                && ($printer['printerId'] ?? null) === 99
                && ($printer['queueCurrent'] ?? null) === 597
                && ($printer['totalWindow'] ?? null) === 600;
        });
    }

    /**
     * F2.2: cuando el overview no responde, PHP recalcula desde `api/printjobs` (fallback) y debe
     * marcar `partialData` si el número de jobs alcanza el límite honesto de 500 (VAL-P1-004), y
     * el predicado `failedWithoutRetryCurrent` debe alinearse con el contrato (ErrorFinal o
     * Pending/Routed/Printing/Cancelled/PrinterBlocked con AttemptCount>1 — VAL-P2-006), incluyendo
     * el mapeo de PrinterBlocked que antes faltaba en normalizePrintJobStatus.
     */
    public function test_dashboard_falls_back_to_legacy_aggregation_with_partial_data_warning(): void
    {
        $now = gmdate('c');

        $fillerJobs = [];
        for ($i = 0; $i < 498; $i++) {
            $fillerJobs[] = [
                'storeId' => 10, 'printerId' => 0, 'status' => 'Pending',
                'attemptCount' => 0, 'createdAtUtc' => $now, 'updatedAtUtc' => $now,
            ];
        }
        $jobs = array_merge($fillerJobs, [
            ['storeId' => 10, 'printerId' => 0, 'status' => 'Cancelled', 'attemptCount' => 2, 'createdAtUtc' => $now, 'updatedAtUtc' => $now],
            ['storeId' => 10, 'printerId' => 0, 'status' => 'PrinterBlocked', 'attemptCount' => 2, 'createdAtUtc' => $now, 'updatedAtUtc' => $now],
        ]);

        $api = new class($jobs) extends ApiClient
        {
            public function __construct(private readonly array $jobs)
            {
                parent::__construct(new Client(['base_uri' => 'http://api.test/']), 'http://api.test');
            }

            public function getAsync(string $path): PromiseInterface
            {
                return Create::promiseFor($this->responseFor($path));
            }

            public function get(string $path): array
            {
                return $this->responseFor($path);
            }

            public function getQuiet(string $path): array
            {
                // Simula la caída del overview: respuesta vacía -> DashboardOverviewService::fetch = null.
                return [];
            }

            /**
             * @return array<int|string, mixed>
             */
            private function responseFor(string $path): array
            {
                return match ($path) {
                    'api/stores?isActive=true' => [
                        ['storeId' => 10, 'name' => 'Tienda 10'],
                    ],
                    'api/dashboard/thresholds' => [],
                    'api/printjobs?limit=500' => $this->jobs,
                    'api/printers?isActive=true' => [],
                    'api/users' => [],
                    default => [],
                };
            }
        };

        $this->app->instance(ApiClient::class, $api);

        $response = $this
            ->withSession([
                'impresoras_token' => 'token',
                'impresoras_user' => ['role' => 'Admin', 'login' => 'admin'],
            ])
            ->get('/');

        $response->assertOk();
        $response->assertViewHas('partialData', true);
        $response->assertViewHas('kpis', function (array $kpis): bool {
            // 498 Pending (sin señal de fallo) + Cancelled(attempt=2) + PrinterBlocked(attempt=2):
            // ambos cuentan ahora en failedWithoutRetryCurrent (antes del fix, solo ErrorFinal contaba -> 0).
            return $kpis['failedWithoutRetryCurrent'] === 2;
        });
    }
}
