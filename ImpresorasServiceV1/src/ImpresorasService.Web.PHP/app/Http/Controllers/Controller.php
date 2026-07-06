<?php

namespace App\Http\Controllers;

use GuzzleHttp\Exception\RequestException;
use Illuminate\Support\Facades\Log;

abstract class Controller
{
    protected function apiErrorMessage(RequestException $e, string $fallback = 'No se pudo completar la operacion.'): string
    {
        $response = $e->getResponse();
        if (! $response) {
            return $fallback;
        }

        $payload = json_decode((string) $response->getBody(), true);
        if (! is_array($payload)) {
            return $fallback;
        }

        $message = $payload['error']
            ?? $payload['message']
            ?? $payload['title']
            ?? $fallback;

        return $this->humanizeApiMessage((string) $message, $fallback);
    }

    protected function humanizeApiMessage(string $message, string $fallback = 'No se pudo completar la operacion.'): string
    {
        $message = trim($message);
        if ($message === '') {
            return $fallback;
        }

        $normalized = str_replace(["\r", "\n"], ' ', $message);
        $normalized = preg_replace('/\s+/', ' ', $normalized) ?? $normalized;
        $normalized = preg_replace('/\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b/i', 'seleccionado', $normalized) ?? $normalized;
        $normalized = str_ireplace('PrintedUnknown', 'impreso pendiente de confirmacion', $normalized);
        $normalized = str_ireplace('ErrorFinal', 'error final', $normalized);
        $normalized = str_ireplace('Pending', 'pendiente', $normalized);
        $normalized = str_ireplace('job seleccionado', 'trabajo seleccionado', $normalized);
        $normalized = str_ireplace('El job', 'El trabajo', $normalized);
        $lookup = strtolower(strtr($normalized, [
            'á' => 'a',
            'Á' => 'A',
            'é' => 'e',
            'É' => 'E',
            'í' => 'i',
            'Í' => 'I',
            'ó' => 'o',
            'Ó' => 'O',
            'ú' => 'u',
            'Ú' => 'U',
        ]));

        if (str_contains($lookup, 'no esta en pendiente ni en error final')) {
            return 'El trabajo seleccionado no se puede reintentar porque no esta pendiente ni en error final.';
        }

        if (str_contains($lookup, 'no puede cancelarse')) {
            return 'El trabajo seleccionado no se puede cancelar en su estado actual.';
        }

        return $normalized !== '' ? $normalized : $fallback;
    }

    protected function extractApiErrors(RequestException $e, string $fallbackField = 'form'): array
    {
        $response = $e->getResponse();
        if (! $response) {
            return [$fallbackField => ['No se pudo conectar con el servicio. Comprueba que la API esté en marcha.']];
        }

        $status = $response->getStatusCode();
        $body = (string) $response->getBody();
        $payload = json_decode($body, true);

        Log::warning('API error response', [
            'status' => $status,
            'body'   => mb_substr($body, 0, 1000),
            'url'    => (string) $e->getRequest()->getUri(),
            'method' => $e->getRequest()->getMethod(),
        ]);

        if (is_array($payload)) {
            $result = [];

            if (isset($payload['errors']) && is_array($payload['errors'])) {
                foreach ($payload['errors'] as $field => $messages) {
                    $fieldKey = is_string($field) && $field !== ''
                        ? lcfirst($field)
                        : $fallbackField;
                    if (is_array($messages)) {
                        $result[$fieldKey] = array_values(array_map(
                            fn ($message): string => $this->humanizeApiMessage((string) $message),
                            $messages
                        ));
                    } elseif (is_string($messages)) {
                        $result[$fieldKey] = [$this->humanizeApiMessage($messages)];
                    }
                }
            }

            if (! empty($result)) {
                return $result;
            }

            $message = $payload['error']
                ?? $payload['message']
                ?? $payload['title']
                ?? null;

            if ($message !== null) {
                return [$fallbackField => [$this->humanizeApiMessage((string) $message)]];
            }
        }

        $fallback = match (true) {
            $status === 400 => 'Solicitud incorrecta. Revisa los datos introducidos.',
            $status === 404 => 'El recurso solicitado no existe.',
            $status === 409 => 'Conflicto: ya existe un registro con esos datos.',
            $status === 422 => 'Los datos enviados no son válidos.',
            $status >= 500 => "Error del servidor (HTTP {$status}). Inténtalo de nuevo más tarde.",
            default        => "Error al procesar la solicitud (HTTP {$status}).",
        };

        return [$fallbackField => [$fallback]];
    }
}
