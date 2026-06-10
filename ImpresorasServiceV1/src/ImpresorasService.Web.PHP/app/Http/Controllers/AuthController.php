<?php

namespace App\Http\Controllers;

use App\Helpers\AuthHelper;
use App\Services\ApiClient;
use Illuminate\Http\RedirectResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\Session;
use Illuminate\View\View;

class AuthController extends Controller
{
    public function showLogin(): View|RedirectResponse
    {
        if (Session::has('impresoras_token')) {
            return redirect()->route('dashboard');
        }
        return view('auth.login');
    }

    public function login(Request $request): RedirectResponse
    {
        $request->validate([
            'login' => 'required|string',
            'password' => 'required|string',
        ]);

        $api = ApiClient::fromConfig();
        try {
            $data = $api->post('api/auth/token', [
                'login' => $request->input('login'),
                'password' => $request->input('password'),
            ]);
        } catch (\GuzzleHttp\Exception\ConnectException $e) {
            return back()->withInput($request->only('login'))->withErrors([
                'login' => 'No se puede conectar con la API. ¿Está la API .NET ejecutándose en ' . config('impresoras.api_url') . '?'
            ]);
        } catch (\GuzzleHttp\Exception\RequestException $e) {
            $status = $e->getResponse()?->getStatusCode();
            $errorBag = $this->extractApiErrors($e, 'login');
            if ($status === 401 && empty($errorBag['login'])) {
                $errorBag = ['login' => ['Credenciales inválidas']];
            }
            return back()->withInput($request->only('login'))->withErrors($errorBag);
        }

        $token = $data['token'] ?? $data['Token'] ?? null;
        $user = $data['user'] ?? $data['User'] ?? null;

        if (!$token || !$user) {
            return back()->withInput($request->only('login'))->withErrors(['login' => 'Respuesta inválida de la API']);
        }

        $user = is_array($user) ? $user : (array) $user;
        $user = array_merge([
            'displayName' => $user['displayName'] ?? $user['DisplayName'] ?? $user['login'] ?? $user['Login'] ?? 'Usuario',
            'login' => $user['login'] ?? $user['Login'] ?? '',
        ], $user);

        Session::put('impresoras_token', $token);
        Session::put('impresoras_user', $user);

        $role = AuthHelper::normalizeRole($user['role'] ?? $user['Role'] ?? '');
        $storeId = $user['storeId'] ?? $user['StoreId'] ?? null;
        if ($role !== AuthHelper::ROLE_ADMIN && $storeId !== null) {
            Session::put('selected_store_id', $storeId);
        } else {
            Session::forget('selected_store_id');
        }

        return redirect()->intended('/');
    }

    public function logout(): RedirectResponse
    {
        Session::forget(['impresoras_token', 'impresoras_user']);
        return redirect()->route('login');
    }
}
