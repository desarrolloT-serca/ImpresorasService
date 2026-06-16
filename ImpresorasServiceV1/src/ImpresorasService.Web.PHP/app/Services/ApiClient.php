<?php

namespace App\Services;

use GuzzleHttp\Client;
use GuzzleHttp\Exception\RequestException;
use Illuminate\Support\Facades\Cache;
use Illuminate\Support\Facades\Log;
use Illuminate\Support\Facades\Session;

class ApiClient
{
    public const SESSION_API_ERROR_KEY = 'impresoras_api_error';

    /** @var array<string, array> */
    private array $requestGetCache = [];

    public function __construct(
        private readonly Client $client,
        private readonly string $baseUrl
    ) {}

    public static function fromConfig(): self
    {
        $baseUrl = rtrim(config('impresoras.api_url'), '/');
        $client = new Client([
            'base_uri' => $baseUrl . '/',
            'timeout' => 30,
            'headers' => [
                'Accept' => 'application/json',
                'Content-Type' => 'application/json',
            ],
        ]);
        return new self($client, $baseUrl);
    }

    public function getAsync(string $path): \GuzzleHttp\Promise\PromiseInterface
    {
        $normalizedPath = ltrim($path, '/');
        $token = Session::get('impresoras_token');
        $cacheKey = sha1(($token ? 'auth:' . $token : 'guest') . '|' . $normalizedPath);

        if (array_key_exists($cacheKey, $this->requestGetCache)) {
            return \GuzzleHttp\Promise\Create::promiseFor($this->requestGetCache[$cacheKey]);
        }

        $options = $token ? ['headers' => ['Authorization' => 'Bearer ' . $token]] : [];

        return $this->client->getAsync($normalizedPath, $options)->then(
            function ($response) use ($cacheKey) {
                $decoded = json_decode((string) $response->getBody(), true) ?? [];
                if (array_key_exists('value', $decoded) && is_array($decoded['value'])) {
                    $decoded = $decoded['value'];
                } elseif (array_key_exists('Value', $decoded) && is_array($decoded['Value'])) {
                    $decoded = $decoded['Value'];
                }
                $this->clearApiFailure();

                return $this->requestGetCache[$cacheKey] = $decoded;
            },
            function (\Throwable $e) use ($normalizedPath) {
                $this->markApiFailure($normalizedPath, $e->getMessage());

                return [];
            }
        );
    }

    public function get(string $path): array
    {
        $normalizedPath = ltrim($path, '/');
        $token = Session::get('impresoras_token');
        $cacheKey = sha1(($token ? 'auth:' . $token : 'guest') . '|' . $normalizedPath);

        if (array_key_exists($cacheKey, $this->requestGetCache)) {
            return $this->requestGetCache[$cacheKey];
        }

        try {
            $decoded = $this->request('GET', $normalizedPath);
            $this->clearApiFailure();

            return $this->requestGetCache[$cacheKey] = $decoded;
        } catch (\Throwable $e) {
            if (! $e instanceof \Illuminate\Http\Exceptions\HttpResponseException) {
                $this->markApiFailure($normalizedPath, $e->getMessage());
            }

            return [];
        }
    }

    public function post(string $path, array $body = []): array
    {
        return $this->mutate('POST', $path, $body);
    }

    public function put(string $path, array $body = []): array
    {
        return $this->mutate('PUT', $path, $body);
    }

    public function delete(string $path): array
    {
        return $this->mutate('DELETE', $path);
    }

    private function mutate(string $method, string $path, array $body = []): array
    {
        $result = $this->request($method, $path, $body);
        $this->invalidateCaches();

        return $result;
    }

    private function invalidateCaches(): void
    {
        $this->requestGetCache = [];
        Cache::store('file')->forget('layout_stores_active');
    }

    private function markApiFailure(string $path, string $message): void
    {
        Session::put(self::SESSION_API_ERROR_KEY, 'No se pudo conectar con la API. Algunos datos pueden estar incompletos.');
        Log::warning('ApiClient GET falló', [
            'path' => $path,
            'error' => $message,
        ]);
    }

    private function clearApiFailure(): void
    {
        Session::forget(self::SESSION_API_ERROR_KEY);
    }

    private function request(string $method, string $path, array $body = []): array
    {
        $normalizedPath = ltrim($path, '/');
        $options = [];
        if (!empty($body)) {
            $options['json'] = $body;
        }

        $token = Session::get('impresoras_token');
        if ($token) {
            $options['headers'] = [
                'Authorization' => 'Bearer ' . $token,
            ];
        }

        try {
            $response = $this->client->request($method, $normalizedPath, $options);
            $content = (string) $response->getBody();
            return json_decode($content, true) ?? [];
        } catch (RequestException $e) {
            $isUnauthorized = $e->getResponse()?->getStatusCode() === 401;
            $isAuthEndpoint = str_starts_with(strtolower($normalizedPath), 'api/auth/');
            $isAuthenticatedRequest = !empty($token);

            if ($isUnauthorized && $isAuthenticatedRequest && ! $isAuthEndpoint) {
                Session::forget(['impresoras_token', 'impresoras_user', 'selected_store_id']);
                throw new \Illuminate\Http\Exceptions\HttpResponseException(
                    redirect()->route('login')->with('error', 'Sesión expirada. Inicia sesión de nuevo.')
                );
            }
            throw $e;
        }
    }
}
