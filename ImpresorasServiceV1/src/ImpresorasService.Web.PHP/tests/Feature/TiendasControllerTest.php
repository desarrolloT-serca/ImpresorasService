<?php

namespace Tests\Feature;

use App\Services\ApiClient;
use GuzzleHttp\Client;
use Tests\TestCase;

class TiendasControllerTest extends TestCase
{
    public function test_index_shows_actions_for_store_id_zero(): void
    {
        $api = new class extends ApiClient
        {
            public function __construct()
            {
                parent::__construct(new Client(['base_uri' => 'http://api.test/']), 'http://api.test');
            }

            public function get(string $path): array
            {
                return match ($path) {
                    'api/stores' => [
                        [
                            'storeId' => 0,
                            'name' => 'Almacen Central',
                            'isActive' => true,
                            'usersCount' => 0,
                            'printersCount' => 0,
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
            ->get('/tiendas');

        $response->assertOk();
        $response->assertSee('/tiendas/0/edit', false);
        $response->assertSee('data-confirm-title="Desactivar tienda"', false);
        $response->assertSee('data-confirm-title="Eliminar tienda definitivamente"', false);
        $response->assertSee('>Desactivar<', false);
        $response->assertSee('>Eliminar definitivo<', false);
    }

    public function test_index_filters_stores_by_search_query(): void
    {
        $api = new class extends ApiClient
        {
            public function __construct()
            {
                parent::__construct(new Client(['base_uri' => 'http://api.test/']), 'http://api.test');
            }

            public function get(string $path): array
            {
                return match ($path) {
                    'api/stores' => [
                        ['storeId' => 1, 'name' => 'Tienda Norte', 'isActive' => true, 'usersCount' => 0, 'printersCount' => 0],
                        ['storeId' => 2, 'name' => 'Tienda Sur', 'isActive' => true, 'usersCount' => 0, 'printersCount' => 0],
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
            ->get('/tiendas?q=norte');

        $response->assertOk();
        $response->assertSee('Tienda Norte');
        $response->assertDontSee('Tienda Sur');
    }

    public function test_store_allows_store_id_zero(): void
    {
        $api = new class extends ApiClient
        {
            /** @var array<string, mixed>|null */
            public ?array $lastBody = null;

            public function __construct()
            {
                parent::__construct(new Client(['base_uri' => 'http://api.test/']), 'http://api.test');
            }

            public function post(string $path, array $body = []): array
            {
                $this->lastBody = ['path' => $path, 'body' => $body];

                return [];
            }
        };

        $this->app->instance(ApiClient::class, $api);

        $response = $this
            ->withSession([
                'impresoras_token' => 'token',
                'impresoras_user' => ['role' => 'Admin', 'login' => 'admin'],
            ])
            ->post('/tiendas', [
                'storeId' => '0',
                'name' => 'Almacen Central',
                'isActive' => '1',
            ]);

        $response->assertRedirect(route('tiendas.index'));
        $response->assertSessionHasNoErrors();
        $this->assertSame('api/stores', $api->lastBody['path'] ?? null);
        $this->assertSame(0, $api->lastBody['body']['storeId'] ?? null);
        $this->assertSame('Almacen Central', $api->lastBody['body']['name'] ?? null);
    }
}
