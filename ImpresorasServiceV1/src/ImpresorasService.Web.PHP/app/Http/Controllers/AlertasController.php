<?php

namespace App\Http\Controllers;

use App\Services\ApiClient;
use Illuminate\Http\RedirectResponse;
use Illuminate\Http\Request;
use Illuminate\View\View;

class AlertasController extends Controller
{
    public function __construct(private readonly ApiClient $api)
    {
    }

    public function configuracion(Request $request): View
    {
        $config = $this->api->get('api/telegram/config');
        $chats  = $this->api->get('api/telegram/chats');
        $stores = $this->api->get('api/stores?isActive=true');

        return view('alertas.configuracion', [
            'config'  => is_array($config) ? $config : [],
            'chats'   => is_array($chats) ? $chats : [],
            'stores'  => is_array($stores) ? $stores : [],
            'success' => session('success'),
            'apiError' => session('apiError'),
        ]);
    }

    public function saveConfig(Request $request): RedirectResponse
    {
        $minSeverity = in_array($request->input('minSeverity'), ['warning', 'critical'], true)
            ? $request->input('minSeverity')
            : 'critical';

        $interval = max(1, (int) $request->input('checkIntervalMinutes', 5));

        try {
            $this->api->put('api/telegram/config', [
                'minSeverity'          => $minSeverity,
                'notifyOnRecovery'     => $request->boolean('notifyOnRecovery'),
                'checkIntervalMinutes' => $interval,
            ]);
            return redirect()->route('alertas.configuracion')->with('success', 'Configuración guardada.');
        } catch (\Throwable) {
            return redirect()->route('alertas.configuracion')->with('apiError', 'Error guardando la configuración.');
        }
    }

    public function addChat(Request $request): RedirectResponse
    {
        $chatId = (int) $request->input('chatId');
        $description = trim((string) $request->input('description', ''));
        $storeIdRaw = $request->input('storeId');
        $storeId = ($storeIdRaw !== null && $storeIdRaw !== '') ? (int) $storeIdRaw : null;

        if ($chatId === 0) {
            return redirect()->route('alertas.configuracion')->with('apiError', 'El Chat ID no puede ser 0.');
        }

        try {
            $this->api->post('api/telegram/chats', [
                'chatId'      => $chatId,
                'description' => $description ?: null,
                'storeId'     => $storeId,
            ]);
            return redirect()->route('alertas.configuracion')->with('success', "Chat {$chatId} añadido.");
        } catch (\Throwable) {
            return redirect()->route('alertas.configuracion')->with('apiError', 'Error añadiendo el chat. ¿Ya existe?');
        }
    }

    public function deleteChat(Request $request, string $chatId): RedirectResponse
    {
        try {
            $this->api->delete("api/telegram/chats/{$chatId}");
            return redirect()->route('alertas.configuracion')->with('success', "Chat {$chatId} eliminado.");
        } catch (\Throwable) {
            return redirect()->route('alertas.configuracion')->with('apiError', "Error eliminando el chat {$chatId}.");
        }
    }

    public function sendTest(Request $request): RedirectResponse
    {
        try {
            $this->api->post('api/telegram/test');
            return redirect()->route('alertas.configuracion')->with('success', 'Mensaje de prueba enviado.');
        } catch (\Throwable) {
            return redirect()->route('alertas.configuracion')->with('apiError', 'Error enviando el mensaje de prueba. ¿Hay chats activos y el bot token es correcto?');
        }
    }

}
