<?php

namespace Tests\Feature;

use App\Services\ApiClient;
use GuzzleHttp\Client;
use Tests\TestCase;

class TiendasControllerTest extends TestCase
{
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
