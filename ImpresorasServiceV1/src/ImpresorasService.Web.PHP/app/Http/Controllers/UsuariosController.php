<?php

namespace App\Http\Controllers;

use App\Helpers\AuthHelper;
use App\Services\ApiClient;
use Illuminate\Http\RedirectResponse;
use Illuminate\Http\Request;
use Illuminate\View\View;

class UsuariosController extends Controller
{
    public function __construct(private readonly ApiClient $api)
    {
    }

    public function index(Request $request): View
    {
        $search = trim((string) $request->query('q', ''));

        try {
            $users = $this->api->get('api/users') ?? [];
        } catch (\Throwable) {
            return view('usuarios.index', [
                'users' => [],
            ])->with('error', 'No se pudieron cargar los usuarios desde la API.');
        }

        $users = is_array($users) ? $users : [];

        if ($search !== '') {
            $needle = mb_strtolower($search);
            $users = array_values(array_filter($users, static function ($user) use ($needle) {
                $id = (string) ($user['userId'] ?? $user['UserId'] ?? '');
                $login = (string) ($user['login'] ?? $user['Login'] ?? '');
                $displayName = (string) ($user['displayName'] ?? $user['DisplayName'] ?? '');
                $storeId = (string) ($user['storeId'] ?? $user['StoreId'] ?? '');

                return (mb_stripos($id, $needle) !== false)
                    || (mb_stripos($login, $needle) !== false)
                    || (mb_stripos($displayName, $needle) !== false)
                    || (mb_stripos($storeId, $needle) !== false);
            }));
        }

        return view('usuarios.index', [
            'users'        => $users,
            'storeOptions' => $this->loadStores(),
        ]);
    }

    public function create(): View
    {
        return view('usuarios.form', [
            'user' => null,
            'stores' => $this->loadStores(),
            'roles' => $this->roles(),
        ]);
    }

    public function store(Request $request): RedirectResponse
    {
        $request->validate([
            'login' => 'required|string|min:3|max:80',
            'displayName' => 'nullable|string|max:120',
            'password' => 'required|string|min:6|max:80',
            'role' => 'required|string',
            'storeId' => 'nullable|integer|min:0',
        ]);

        try {
            $this->api->post('api/users', [
                'login' => $request->input('login'),
                'displayName' => $request->input('displayName'),
                'password' => $request->input('password'),
                'role' => $request->input('role'),
                'storeId' => $request->filled('storeId') ? $request->integer('storeId') : null,
            ]);
            return redirect()->route('usuarios.index')->with('success', 'Usuario creado.');
        } catch (\GuzzleHttp\Exception\RequestException $e) {
            return back()->withInput()->withErrors($this->extractApiErrors($e, 'login'));
        }
    }

    public function edit(int $usuario): View|RedirectResponse
    {
        try {
            $user = $this->api->get("api/users/{$usuario}");
            return view('usuarios.form', [
                'user' => $user,
                'stores' => $this->loadStores(),
                'roles' => $this->roles(),
            ]);
        } catch (\Throwable) {
            return redirect()->route('usuarios.index')->with('error', 'Usuario no encontrado.');
        }
    }

    public function update(Request $request, int $usuario): RedirectResponse
    {
        $request->validate([
            'login' => 'required|string|min:3|max:80',
            'displayName' => 'nullable|string|max:120',
            'password' => 'nullable|string|min:6|max:80',
            'role' => 'required|string',
            'storeId' => 'nullable|integer|min:0',
        ]);

        try {
            $this->api->put("api/users/{$usuario}", [
                'login' => $request->input('login'),
                'displayName' => $request->input('displayName'),
                'password' => $request->input('password'),
                'role' => $request->input('role'),
                'storeId' => $request->filled('storeId') ? $request->integer('storeId') : null,
            ]);
            return redirect()->route('usuarios.index')->with('success', 'Usuario actualizado.');
        } catch (\GuzzleHttp\Exception\RequestException $e) {
            return back()->withInput()->withErrors($this->extractApiErrors($e, 'login'));
        }
    }

    /**
     * Activa o desactiva un usuario sin pasar por el formulario de edicion. Desactivar corta el
     * acceso en la siguiente peticion -la Api comprueba is_active en cada una-, asi que es la via
     * rapida cuando hay que echar a alguien y no se quiere borrar su historial.
     */
    public function activacion(Request $request, int $usuario): RedirectResponse
    {
        $activar = $request->boolean('activar');

        // La Api tambien lo impide; comprobarlo aqui ahorra el viaje y da mejor mensaje.
        $sessionUser = session('impresoras_user', []);
        $loggedUserId = $sessionUser['userId'] ?? $sessionUser['UserId'] ?? null;
        if (! $activar && (int) $loggedUserId === $usuario) {
            return back()->with('error', 'No puedes desactivar tu propio usuario.');
        }

        try {
            // El PUT de la Api exige login, rol y tienda, asi que se releen y se reenvian tal cual:
            // lo unico que cambia es isActive. Sin password, la contrasena no se toca.
            $user = $this->api->get("api/users/{$usuario}");

            $this->api->put("api/users/{$usuario}", [
                'login'       => $user['login'] ?? $user['Login'] ?? null,
                'displayName' => $user['displayName'] ?? $user['DisplayName'] ?? null,
                'role'        => $user['role'] ?? $user['Role'] ?? null,
                'storeId'     => $user['storeId'] ?? $user['StoreId'] ?? null,
                'isActive'    => $activar,
            ]);

            return redirect()->route('usuarios.index')->with(
                'success',
                $activar ? 'Usuario activado.' : 'Usuario desactivado. Su acceso queda cortado de inmediato.'
            );
        } catch (\GuzzleHttp\Exception\RequestException $e) {
            return back()->withErrors($this->extractApiErrors($e, 'form'));
        }
    }

    public function destroy(int $usuario): RedirectResponse
    {
        $sessionUser = session('impresoras_user', []);
        $loggedUserId = $sessionUser['userId'] ?? $sessionUser['UserId'] ?? null;
        if ((int) $loggedUserId === $usuario) {
            return back()->with('error', 'No puedes eliminar tu propio usuario.');
        }

        try {
            $this->api->delete("api/users/{$usuario}");
            return redirect()->route('usuarios.index')->with('success', 'Usuario eliminado.');
        } catch (\GuzzleHttp\Exception\RequestException $e) {
            return back()->withErrors($this->extractApiErrors($e, 'form'));
        }
    }

    private function loadStores(): array
    {
        $stores = $this->api->get('api/stores?isActive=true') ?? [];
        $stores = is_array($stores) ? $stores : [];
        usort($stores, function ($a, $b) {
            $aId = (int) ($a['storeId'] ?? $a['StoreId'] ?? 0);
            $bId = (int) ($b['storeId'] ?? $b['StoreId'] ?? 0);
            return $aId <=> $bId;
        });
        return $stores;
    }

    private function roles(): array
    {
        return [
            AuthHelper::ROLE_ADMIN => 'Administrador',
            AuthHelper::ROLE_STORE_MANAGER => 'Jefe de tienda',
            AuthHelper::ROLE_EMPLOYEE => 'Empleado',
        ];
    }
}
