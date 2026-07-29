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
                    'api/printjobs?limit=5000' => [],
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
}
