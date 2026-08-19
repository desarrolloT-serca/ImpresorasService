<?php

namespace Tests\Feature;

use App\Services\ApiClient;
use GuzzleHttp\Client;
use Tests\TestCase;

/**
 * Toggle de activacion (Fase 5). Desactivar corta el acceso en la siguiente peticion, asi que la
 * pantalla tiene que dejar hacerlo sin pasar por el formulario de edicion.
 */
class UsuariosControllerTest extends TestCase
{
    public function test_index_shows_state_and_the_right_toggle_per_user(): void
    {
        $this->app->instance(ApiClient::class, $this->fakeApi());

        $response = $this->asAdmin()->get('/usuarios');

        $response->assertOk();
        $response->assertSee('>Activo<', false);
        $response->assertSee('>Desactivado<', false);
        // El activo ofrece desactivar, con confirmacion.
        $response->assertSee('data-confirm-title="Desactivar usuario"', false);
        $response->assertSee('aria-label="Desactivar activo"', false);
        // El desactivado ofrece activar, sin confirmacion: no rompe nada.
        $response->assertSee('aria-label="Activar inactivo"', false);
    }

    public function test_deactivate_sends_is_active_false_preserving_the_rest(): void
    {
        $api = $this->fakeApi();
        $this->app->instance(ApiClient::class, $api);

        $response = $this->asAdmin()->post('/usuarios/1/activacion', ['activar' => '0']);

        $response->assertRedirect('/usuarios');
        $this->assertSame('api/users/1', $api->putPath);
        $this->assertFalse($api->putBody['isActive']);
        // Se reenvia el resto tal cual; sin password, la contrasena no se toca.
        $this->assertSame('activo', $api->putBody['login']);
        $this->assertSame('Admin', $api->putBody['role']);
        $this->assertArrayNotHasKey('password', $api->putBody);
    }

    public function test_cannot_deactivate_yourself(): void
    {
        $api = $this->fakeApi();
        $this->app->instance(ApiClient::class, $api);

        $response = $this
            ->withSession([
                'impresoras_token' => 'token',
                'impresoras_user' => ['userId' => 1, 'role' => 'Admin', 'login' => 'activo'],
            ])
            ->post('/usuarios/1/activacion', ['activar' => '0']);

        $response->assertSessionHas('error');
        $this->assertNull($api->putPath, 'No debe llegar a llamar a la Api.');
    }

    private function asAdmin(): self
    {
        return $this->withSession([
            'impresoras_token' => 'token',
            'impresoras_user' => ['userId' => 99, 'role' => 'Admin', 'login' => 'admin'],
        ]);
    }

    private function fakeApi(): ApiClient
    {
        return new class extends ApiClient
        {
            public ?string $putPath = null;

            /** @var array<string, mixed> */
            public array $putBody = [];

            public function __construct()
            {
                parent::__construct(new Client(['base_uri' => 'http://api.test/']), 'http://api.test');
            }

            public function get(string $path): array
            {
                return match ($path) {
                    'api/users' => [
                        ['userId' => 1, 'login' => 'activo', 'displayName' => 'Uno', 'role' => 'Admin', 'storeId' => null, 'isActive' => true],
                        ['userId' => 2, 'login' => 'inactivo', 'displayName' => 'Dos', 'role' => 'Employee', 'storeId' => 3, 'isActive' => false],
                    ],
                    'api/users/1' => ['userId' => 1, 'login' => 'activo', 'displayName' => 'Uno', 'role' => 'Admin', 'storeId' => null, 'isActive' => true],
                    default => [],
                };
            }

            public function put(string $path, array $body = []): array
            {
                $this->putPath = $path;
                $this->putBody = $body;

                return [];
            }
        };
    }
}
