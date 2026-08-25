<?php

namespace Tests\Feature;

use App\Services\ApiClient;
use GuzzleHttp\Client;
use GuzzleHttp\Promise\Create;
use GuzzleHttp\Promise\PromiseInterface;
use Tests\TestCase;

/**
 * Los enlaces que salen de una incidencia del dashboard tienen que aterrizar ya filtrados. Aqui se
 * comprueba el otro extremo: que /cola y /impresoras entienden esos filtros.
 */
class IncidentFilterTest extends TestCase
{
    public function test_cola_forwards_the_unassigned_filter_to_the_api(): void
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

                return Create::promiseFor([]);
            }

            public function get(string $path): array
            {
                $this->paths[] = $path;

                return [];
            }

            public function getQuiet(string $path): array
            {
                $this->paths[] = $path;

                return [];
            }
        };

        $this->app->instance(ApiClient::class, $api);

        $this->withSession([
            'impresoras_token' => 'token',
            'impresoras_user' => ['role' => 'Admin', 'login' => 'admin'],
        ])->get('/cola?storeId=10&unassigned=1')->assertOk();

        $queuePaths = array_values(array_filter($api->paths, static fn (string $p): bool => str_starts_with($p, 'api/printjobs')));
        self::assertNotEmpty($queuePaths);
        self::assertStringContainsString('unassigned=true', $queuePaths[0]);
        // Excluyente con status, igual que failedWithoutRetry: define su propio conjunto de estados.
        self::assertStringNotContainsString('status=', $queuePaths[0]);
    }

    public function test_impresoras_narrows_the_list_to_the_printer_that_causes_the_incident(): void
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

            /** @return array<int|string, mixed> */
            private function responseFor(string $path): array
            {
                return match (true) {
                    str_starts_with($path, 'api/stores') => [['storeId' => 10, 'name' => 'Tienda 10']],
                    str_starts_with($path, 'api/printers') => [
                        ['printerId' => 77, 'storeId' => 10, 'printerName' => 'ROTA-77', 'spoolQueue' => '\\\\h\\q77', 'host' => 'h', 'isActive' => true, 'lastConnectionOk' => false, 'lastConnectionCheckAtUtc' => gmdate('c'), 'connectionFailuresStreak' => 6],
                        ['printerId' => 88, 'storeId' => 10, 'printerName' => 'SANA-88', 'spoolQueue' => '\\\\h\\q88', 'host' => 'h', 'isActive' => true, 'lastConnectionOk' => true, 'lastConnectionCheckAtUtc' => gmdate('c'), 'connectionFailuresStreak' => 0],
                    ],
                    default => [],
                };
            }
        };

        $this->app->instance(ApiClient::class, $api);

        $session = [
            'impresoras_token' => 'token',
            'impresoras_user' => ['role' => 'Admin', 'login' => 'admin'],
        ];

        $focused = $this->withSession($session)->get('/impresoras?storeId=10&printerId=77')->assertOk()->getContent();
        self::assertStringContainsString('ROTA-77', $focused);
        self::assertStringNotContainsString('SANA-88', $focused);
        self::assertStringContainsString('la impresora que origina la incidencia', $focused);

        // Sin printerId se sigue viendo todo.
        $all = $this->withSession($session)->get('/impresoras?storeId=10')->assertOk()->getContent();
        self::assertStringContainsString('ROTA-77', $all);
        self::assertStringContainsString('SANA-88', $all);
    }

    public function test_impresoras_falls_back_to_the_full_list_when_the_printer_no_longer_exists(): void
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

            /** @return array<int|string, mixed> */
            private function responseFor(string $path): array
            {
                return match (true) {
                    str_starts_with($path, 'api/stores') => [['storeId' => 10, 'name' => 'Tienda 10']],
                    str_starts_with($path, 'api/printers') => [
                        ['printerId' => 88, 'storeId' => 10, 'printerName' => 'SANA-88', 'spoolQueue' => '\\\\h\\q88', 'host' => 'h', 'isActive' => true, 'lastConnectionOk' => true, 'lastConnectionCheckAtUtc' => gmdate('c'), 'connectionFailuresStreak' => 0],
                    ],
                    default => [],
                };
            }
        };

        $this->app->instance(ApiClient::class, $api);

        $content = $this->withSession([
            'impresoras_token' => 'token',
            'impresoras_user' => ['role' => 'Admin', 'login' => 'admin'],
        ])->get('/impresoras?storeId=10&printerId=999')->assertOk()->getContent();

        self::assertStringContainsString('SANA-88', $content);
        self::assertStringNotContainsString('la impresora que origina la incidencia', $content);
    }
}
