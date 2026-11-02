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

            /** El dashboard pide el overview dentro del lote paralelo. */
            public function getQuietAsync(string $path): PromiseInterface
            {
                return Create::promiseFor($this->getQuiet($path));
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

            /** El dashboard pide el overview dentro del lote paralelo. */
            public function getQuietAsync(string $path): PromiseInterface
            {
                return Create::promiseFor($this->getQuiet($path));
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
                                    ['printerId' => 99, 'queueCurrent' => 597, 'failedWindow' => 0, 'receivedWindow' => 600],
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
                && ($printer['receivedWindow'] ?? null) === 600;
        });
    }

    /**
     * F2.2: cuando el overview no responde, PHP recalcula desde `api/printjobs` (fallback) y debe
     * marcar `partialData` si el número de jobs alcanza el límite honesto de 500 (VAL-P1-004), y
     * el predicado `failedWithoutRetryCurrent` debe alinearse con el contrato (ErrorFinal,
     * PrinterBlocked desde el primer intento, o Pending/Routed/Printing con AttemptCount>1 — VAL-P2-006), incluyendo
     * el mapeo de PrinterBlocked que antes faltaba en normalizePrintJobStatus. `Cancelled` quedó
     * fuera del predicado (2026-08-17), así que el fallback tampoco debe contarlo.
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

            /** El dashboard pide el overview dentro del lote paralelo. */
            public function getQuietAsync(string $path): PromiseInterface
            {
                return Create::promiseFor($this->getQuiet($path));
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
            // solo cuenta PrinterBlocked. El cancelado lo cerró el operador, no está pendiente de
            // reenvío — mismo criterio que DashboardPrintJobPredicates.FailedWithoutRetryCurrent.
            return $kpis['failedWithoutRetryCurrent'] === 1;
        });
    }

    /**
     * Fase 2 (KPI-P2-003): activePrinters/activeStores
     * también deben salir del overview, no solo received/printed/failed/queueCurrent/
     * failedWithoutRetryCurrent. Antes del fix, applyOverviewKpis() no los tocaba y el valor
     * mostrado quedaba en el calculado localmente (heurística distinta: "tiendas con impresora
     * conectada" en vez de "tiendas con IsActive=true").
     */
    public function test_dashboard_uses_all_overview_kpis(): void
    {
        $api = $this->makeOverviewApiClient(
            jobs: [], // fetch legacy vacío: si activePrinters/activeStores no vinieran del overview, saldrían en 0 aquí.
            printers: [],
            overviewKpis: [
                'received' => 5, 'printed' => 3, 'failed' => 1, 'queueCurrent' => 1,
                'failedWithoutRetryCurrent' => 0, 'activePrinters' => 7, 'activeStores' => 3,
            ],
        );

        $this->app->instance(ApiClient::class, $api);

        $response = $this
            ->withSession([
                'impresoras_token' => 'token',
                'impresoras_user' => ['role' => 'Admin', 'login' => 'admin'],
            ])
            ->get('/');

        $response->assertOk();
        $response->assertViewHas('kpis', function (array $kpis): bool {
            return $kpis['activePrinters'] === 7 && $kpis['activeStores'] === 3;
        });
    }

    public function test_dashboard_hides_the_success_rate_when_the_period_has_no_received_jobs(): void
    {
        $api = $this->makeOverviewApiClient(
            jobs: [],
            printers: [],
            overviewKpis: [
                'received' => 0, 'printed' => 0, 'failed' => 0, 'queueCurrent' => 0,
                'failedWithoutRetryCurrent' => 32, 'activePrinters' => 1, 'activeStores' => 1,
            ],
        );

        $this->app->instance(ApiClient::class, $api);

        $response = $this
            ->withSession([
                'impresoras_token' => 'token',
                'impresoras_user' => ['role' => 'Admin', 'login' => 'admin'],
            ])
            ->get('/?window=today');

        $content = $response->assertOk()->getContent();
        // Sin recibidos no hay tasa de exito: se pinta "—", no un 100% ficticio.
        self::assertStringContainsString('<strong class="dbx-op-tile-value">—</strong>', $content);
        self::assertStringContainsString('sin trabajos recibidos en el periodo', $content);
    }

    /**
     * queueCurrent y failedWithoutRetryCurrent son foto de estado actual (sin ventana, A-KPI-01):
     * dividirlos entre los recibidos del periodo producia porcentajes imposibles (32/1 = 3200%).
     */
    public function test_dashboard_never_expresses_current_state_kpis_as_a_percentage_of_the_window(): void
    {
        $api = $this->makeOverviewApiClient(
            jobs: [],
            printers: [],
            overviewKpis: [
                'received' => 1, 'printed' => 1, 'failed' => 0, 'queueCurrent' => 4,
                'failedWithoutRetryCurrent' => 32, 'activePrinters' => 1, 'activeStores' => 1,
            ],
        );

        $this->app->instance(ApiClient::class, $api);

        $content = $this
            ->withSession([
                'impresoras_token' => 'token',
                'impresoras_user' => ['role' => 'Admin', 'login' => 'admin'],
            ])
            ->get('/?window=today')
            ->assertOk()
            ->getContent();

        self::assertStringNotContainsString('3200', $content);
        // Cola y sin reenviar se muestran en absoluto, dentro de "Estado actual de la operacion".
        self::assertStringContainsString('<strong class="dbx-op-tile-value">4</strong>', $content);
        self::assertStringContainsString('<strong class="dbx-op-tile-value">32</strong>', $content);
        // Los porcentajes solo salen del bloque de periodo, y dicen sobre que base se calculan.
        self::assertStringContainsString('porcentajes sobre 1 recibidos', $content);
        self::assertStringContainsString('<strong class="dbx-op-tile-value">100 %</strong>', $content);
    }

    /**
     * Los botones de una incidencia tienen que llevar al subconjunto que la produce, no al listado
     * entero de la tienda: la impresora concreta que falla, y los trabajos sin impresora asignada.
     * Sin esto el operador aterrizaba en una lista de 200 trabajos sin saber cuales eran los rotos.
     */
    public function test_incident_actions_link_to_the_filtered_subset_that_causes_them(): void
    {
        $api = $this->makeOverviewApiClient(
            jobs: [],
            printers: [[
                'printerId' => 77, 'storeId' => 10, 'printerName' => 'TICKET-02', 'isActive' => true,
                'lastConnectionOk' => false, 'lastConnectionCheckAtUtc' => gmdate('c', time() - 2400),
                'lastConnectionTransport' => 'IPP', 'lastConnectionError' => 'timeout',
                'connectionFailuresStreak' => 6,
            ]],
            overviewKpis: [
                'received' => 10, 'printed' => 8, 'failed' => 2, 'queueCurrent' => 4,
                'failedWithoutRetryCurrent' => 3, 'activePrinters' => 1, 'activeStores' => 1,
            ],
            overviewAlerts: [
                ['storeId' => 10, 'storeName' => 'Tienda 10', 'health' => 'critical', 'healthReason' => 'Impresora(s) sin conexion (conectividad)', 'queuedCurrent' => 4, 'failedWithoutRetryCurrent' => 3],
                ['storeId' => 10, 'storeName' => 'Tienda 10', 'health' => 'critical', 'healthReason' => 'Acumula 1 o mas fallos sin reenviar', 'queuedCurrent' => 4, 'failedWithoutRetryCurrent' => 3],
            ],
            overviewStoreExtra: ['unassignedQueueCurrent' => 2],
        );

        $this->app->instance(ApiClient::class, $api);

        $content = $this
            ->withSession([
                'impresoras_token' => 'token',
                'impresoras_user' => ['role' => 'Admin', 'login' => 'admin'],
            ])
            ->get('/?window=today')
            ->assertOk()
            ->getContent();

        // Conectividad: a la impresora caida, no a las de la tienda.
        self::assertStringContainsString('/impresoras?storeId=10&amp;printerId=77', $content);
        // Sin reenviar: al mismo conjunto que cuenta el KPI.
        self::assertStringContainsString('/cola?storeId=10&amp;failedWithoutRetry=1', $content);
        // Sin impresora asignada: a los trabajos sin impresora, no a la cola entera.
        self::assertStringContainsString('/cola?storeId=10&amp;unassigned=1', $content);
    }

    public function test_employee_can_follow_a_printer_alert_to_the_read_only_printer_panel(): void
    {
        $this->app->instance(ApiClient::class, $this->makeOverviewApiClient(
            jobs: [],
            printers: [],
            overviewKpis: [
                'received' => 0, 'printed' => 0, 'failed' => 0, 'queueCurrent' => 0,
                'failedWithoutRetryCurrent' => 0, 'activePrinters' => 0, 'activeStores' => 1,
            ],
        ));

        $this->withSession([
            'impresoras_token' => 'token',
            'impresoras_user' => ['role' => 'Employee', 'storeId' => 10, 'login' => 'employee'],
        ])->get('/impresoras?storeId=10')
            ->assertOk()
            ->assertSee('Impresoras');
    }

    public function test_employee_printer_panel_does_not_start_connection_checks(): void
    {
        $view = str_replace("\r\n", "\n", file_get_contents(resource_path('views/impresoras/index.blade.php')));

        self::assertStringContainsString('const canCheckConnection =', $view);
        self::assertStringContainsString('if (canCheckConnection && pingInterval > 0)', $view);
    }

    /**
     * Fase 2: con overview disponible, el bucle de agregación local sobre `api/printjobs` no debe
     * ejecutarse en absoluto (no solo quedar sobrescrito al final). Se simulan 600 jobs crudos —
     * suficientes para disparar `partialData` si el fallback corriera — y se comprueba que
     * `partialData` sigue en false, señal de que el bucle nunca se ejecutó.
     */
    public function test_dashboard_does_not_recalculate_kpis_when_overview_exists(): void
    {
        $now = gmdate('c');
        $jobs = [];
        for ($i = 0; $i < 600; $i++) {
            $jobs[] = ['storeId' => 10, 'printerId' => 0, 'status' => 'Pending', 'attemptCount' => 0, 'createdAtUtc' => $now, 'updatedAtUtc' => $now];
        }

        $api = $this->makeOverviewApiClient(
            jobs: $jobs,
            printers: [],
            overviewKpis: [
                'received' => 5, 'printed' => 3, 'failed' => 1, 'queueCurrent' => 1,
                'failedWithoutRetryCurrent' => 0, 'activePrinters' => 1, 'activeStores' => 1,
            ],
        );

        $this->app->instance(ApiClient::class, $api);

        $response = $this
            ->withSession([
                'impresoras_token' => 'token',
                'impresoras_user' => ['role' => 'Admin', 'login' => 'admin'],
            ])
            ->get('/');

        $response->assertOk();
        $response->assertViewHas('partialData', false);
        $response->assertViewHas('kpis', fn (array $kpis): bool => $kpis['received'] === 5);
    }

    /**
     * La grafica del periodo cubre los cuatro KPI del bloque de rendimiento, no solo la cola: las
     * cuatro series se pre-renderizan y el selector solo conmuta visibilidad. Si una dejara de
     * pintarse, su pestana se quedaria sin nada que ensenar y el fallo seria mudo.
     */
    public function test_the_period_chart_renders_one_series_per_kpi(): void
    {
        $api = $this->makeOverviewApiClient(
            jobs: [],
            printers: [],
            overviewKpis: [
                'received' => 12, 'printed' => 9, 'failed' => 3, 'queueCurrent' => 4,
                'failedWithoutRetryCurrent' => 0, 'activePrinters' => 1, 'activeStores' => 1,
            ],
            timeseriesPoints: [
                ['startUtc' => '2026-01-01T06:00:00Z', 'received' => 5, 'printed' => 4, 'failed' => 1, 'queueDepth' => 2],
                ['startUtc' => '2026-01-01T07:00:00Z', 'received' => 7, 'printed' => 5, 'failed' => 2, 'queueDepth' => 4],
            ],
        );

        $this->app->instance(ApiClient::class, $api);

        $response = $this
            ->withSession([
                'impresoras_token' => 'token',
                'impresoras_user' => ['role' => 'Admin', 'login' => 'admin'],
            ])
            ->get('/');

        $response->assertOk();
        $html = $response->getContent();

        foreach (['queue', 'received', 'printed', 'failed'] as $serie) {
            $this->assertStringContainsString('data-serie-tab="' . $serie . '"', $html);
            $this->assertStringContainsString('data-serie="' . $serie . '"', $html);
        }

        // Arranca en cola: es la unica serie con umbral, la que dice si hay que actuar ahora.
        $this->assertMatchesRegularExpression('/<g data-serie="queue"\s*>/', $html);
        $this->assertMatchesRegularExpression('/<g data-serie="failed"\s+hidden\s*>/', $html);
        $this->assertStringContainsString('Umbral cr&iacute;tico', $html);
    }

    /**
     * @param list<array<string, mixed>> $jobs
     * @param list<array<string, mixed>> $printers
     * @param array<string, int> $overviewKpis
     * @param list<array<string, mixed>> $timeseriesPoints
     */
    private function makeOverviewApiClient(
        array $jobs,
        array $printers,
        array $overviewKpis,
        array $overviewAlerts = [],
        array $overviewStoreExtra = [],
        array $timeseriesPoints = [],
    ): ApiClient {
        return new class($jobs, $printers, $overviewKpis, $overviewAlerts, $overviewStoreExtra, $timeseriesPoints) extends ApiClient
        {
            public function __construct(
                private readonly array $jobs,
                private readonly array $printers,
                private readonly array $overviewKpis,
                private readonly array $overviewAlerts,
                private readonly array $overviewStoreExtra,
                private readonly array $timeseriesPoints,
            ) {
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

            /** El dashboard pide el overview dentro del lote paralelo. */
            public function getQuietAsync(string $path): PromiseInterface
            {
                return Create::promiseFor($this->getQuiet($path));
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
                    'api/printjobs?limit=500' => $this->jobs,
                    'api/printers?isActive=true' => $this->printers,
                    'api/users' => [],
                    'api/dashboard/overview?window=today' => [
                        'kpis' => $this->overviewKpis,
                        'stores' => [
                            array_merge([
                                'storeId' => 10,
                                'storeName' => 'Tienda 10',
                                'connectedPrinters' => 1,
                                'received' => $this->overviewKpis['received'],
                                'printed' => $this->overviewKpis['printed'],
                                'failed' => $this->overviewKpis['failed'],
                                'queuedCurrent' => $this->overviewKpis['queueCurrent'],
                                'failedWithoutRetryCurrent' => $this->overviewKpis['failedWithoutRetryCurrent'],
                                'health' => 'healthy',
                                'healthReason' => 'Operacion dentro de umbrales',
                            ], $this->overviewStoreExtra),
                        ],
                        'alerts' => $this->overviewAlerts,
                    ],
                    'api/dashboard/timeseries?window=today' => ['points' => $this->timeseriesPoints],
                    default => [],
                };
            }
        };
    }

    /**
     * Todo lo que alimenta el dashboard se pide en un unico lote paralelo. Antes eran tres tandas
     * secuenciales y el tiempo de carga era la suma de las tres, no la mas lenta.
     */
    public function test_dashboard_requests_everything_in_a_single_batch(): void
    {
        $api = $this->makeRecordingApiClient(activeStoreIds: [10]);
        $this->app->instance(ApiClient::class, $api);

        $this->withSession([
            'impresoras_token' => 'token',
            'impresoras_user' => ['role' => 'Admin', 'login' => 'admin'],
        ])->get('/')->assertOk();

        // Overview y threshold-rules entran en el lote, no en peticiones sueltas despues de el.
        // El doble no simula el cache por peticion del ApiClient real, asi que aqui solo se
        // comprueba que la ruta se pidio; en produccion la segunda lectura sale de ese cache.
        foreach (['api/dashboard/overview?window=today', 'api/dashboard/threshold-rules'] as $esperada) {
            self::assertContains($esperada, $api->paths, "falta $esperada en el lote");
        }
    }

    /**
     * Unica dependencia real entre peticiones: si la tienda seleccionada ya no esta activa, el
     * filtro se limpia y el lote se rehace sin ella. Sin esto, el dashboard mostraria los datos
     * de una tienda dada de baja.
     */
    public function test_dashboard_refetches_without_the_store_when_it_is_no_longer_active(): void
    {
        // La sesion apunta a la tienda 99, que no esta entre las activas.
        $api = $this->makeRecordingApiClient(activeStoreIds: [10]);
        $this->app->instance(ApiClient::class, $api);

        $response = $this->withSession([
            'impresoras_token' => 'token',
            'impresoras_user' => ['role' => 'Admin', 'login' => 'admin'],
            'selected_store_id' => 99,
        ])->get('/');

        $response->assertOk();

        $conTienda = array_filter($api->paths, fn ($p) => str_contains($p, 'storeId=99'));
        $sinTienda = array_filter($api->paths, fn ($p) => str_starts_with($p, 'api/printers?isActive=true') && !str_contains($p, 'storeId'));

        self::assertNotEmpty($conTienda, 'el primer lote debe salir acotado a la tienda de sesion');
        self::assertNotEmpty($sinTienda, 'el lote debe rehacerse sin la tienda inactiva');
        $response->assertSessionMissing('selected_store_id');
    }

    /** Doble que solo registra las rutas pedidas; el contenido no importa para estas pruebas. */
    private function makeRecordingApiClient(array $activeStoreIds): ApiClient
    {
        return new class($activeStoreIds) extends ApiClient
        {
            /** @var list<string> */
            public array $paths = [];

            public function __construct(private readonly array $activeStoreIds)
            {
                parent::__construct(new Client(['base_uri' => 'http://api.test/']), 'http://api.test');
            }

            public function getAsync(string $path): PromiseInterface
            {
                return Create::promiseFor($this->responseFor($path));
            }

            public function getQuietAsync(string $path): PromiseInterface
            {
                return Create::promiseFor($this->getQuiet($path));
            }

            public function get(string $path): array
            {
                return $this->responseFor($path);
            }

            public function getQuiet(string $path): array
            {
                return $this->responseFor($path);
            }

            /** @return array<int|string, mixed> */
            private function responseFor(string $path): array
            {
                $this->paths[] = $path;

                if (str_starts_with($path, 'api/stores')) {
                    return array_map(
                        static fn (int $id): array => ['storeId' => $id, 'name' => 'Tienda ' . $id],
                        $this->activeStoreIds,
                    );
                }

                return [];
            }
        };
    }
}
