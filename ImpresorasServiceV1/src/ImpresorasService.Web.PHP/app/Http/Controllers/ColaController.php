<?php

namespace App\Http\Controllers;

use App\Helpers\AuthHelper;
use App\Services\ApiClient;
use GuzzleHttp\Exception\RequestException;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\RedirectResponse;
use Illuminate\Http\Request;
use Illuminate\View\View;

class ColaController extends Controller
{
    public function __construct(private readonly ApiClient $api)
    {
    }

    public function index(Request $request): View
    {
        $params = [];
        $externalJobId = trim((string) $request->input('externalJobId', ''));
        $page = max(1, $request->integer('page', 1));
        $limit = $this->resolvePageSize($request->integer('limit', 100));
        $effectiveStore = AuthHelper::getEffectiveStoreId();
        if ($request->filled('storeId')) {
            $params['storeId'] = $request->integer('storeId');
        } elseif ($effectiveStore !== null) {
            $params['storeId'] = $effectiveStore;
        }
        if ($request->filled('status')) {
            $params['status'] = $request->integer('status');
        }
        if ($externalJobId !== '') {
            $params['externalJobId'] = $externalJobId;
        }
        $params['page'] = $page;
        $params['limit'] = $limit;
        $params['includeTotal'] = 'true';

        [$jobs, $total, $page, $limit] = $this->fetchQueuePage($params, $externalJobId, $page, $limit);
        $lastPage = max(1, (int) ceil($total / max(1, $limit)));

        // Una página parcial (menos ítems que el límite) es definitivamente la última página.
        // Esto evita redirecciones incorrectas cuando el campo count de la API devuelve
        // el recuento de la página actual en lugar del total global.
        if (! empty($jobs) && count($jobs) < $limit) {
            $lastPage = max($lastPage, $page);
            if ($total === count($jobs) && $page > 1) {
                $total = ($page - 1) * $limit + count($jobs);
            }
        }

        if ($total > 0 && $page > $lastPage) {
            $params['page'] = $lastPage;
            [$jobs, $total, $page, $limit] = $this->fetchQueuePage($params, $externalJobId, $lastPage, $limit);
            $lastPage = max(1, (int) ceil($total / max(1, $limit)));
            if (! empty($jobs) && count($jobs) < $limit) {
                $lastPage = max($lastPage, $page);
            }
        }

        foreach ($jobs as &$job) {
            $s = $job['status'] ?? $job['Status'] ?? null;
            if ($s !== null && $s !== '') {
                $job['_status'] = \App\Helpers\StatusLabels::normalizeToInt($s) ?? $s;
            }
        }

        return view('cola', [
            'jobs' => $jobs,
            'storeId' => $request->input('storeId'),
            'status' => $request->input('status'),
            'externalJobId' => $externalJobId,
            'page' => $page,
            'limit' => $limit,
            'total' => $total,
            'lastPage' => $lastPage,
            'from' => $total > 0 ? (($page - 1) * $limit) + 1 : 0,
            'to' => min($total, $page * $limit),
        ]);
    }

    /**
     * @return array{0: array<int, mixed>, 1: int, 2: int, 3: int}
     */
    private function fetchQueuePage(array $params, string $externalJobId, int $page, int $limit): array
    {
        $query = http_build_query($params);
        $path = 'api/printjobs' . ($query ? '?' . $query : '');
        $response = $this->api->get($path) ?? [];
        $response = is_array($response) ? $response : [];

        if (array_key_exists('value', $response) || array_key_exists('Value', $response)) {
            $jobs = $response['value'] ?? $response['Value'] ?? [];
            $jobs = is_array($jobs) ? $jobs : [];

            return [
                $jobs,
                (int) ($response['count'] ?? $response['Count'] ?? count($jobs)),
                max(1, (int) ($response['page'] ?? $response['Page'] ?? $page)),
                $this->resolvePageSize((int) ($response['pageSize'] ?? $response['PageSize'] ?? $limit)),
            ];
        }

        $jobs = $this->fetchLegacyQueueSnapshot($params, $response);
        if ($externalJobId !== '') {
            $needle = mb_strtolower($externalJobId);
            $jobs = array_values(array_filter($jobs, static function ($job) use ($needle) {
                $value = (string) ($job['externalJobId'] ?? $job['ExternalJobId'] ?? '');

                return mb_stripos($value, $needle) !== false;
            }));
        }

        $total = count($jobs);
        $offset = ($page - 1) * $limit;

        return [array_slice($jobs, $offset, $limit), $total, $page, $limit];
    }

    /**
     * @return array<int, mixed>
     */
    private function fetchLegacyQueueSnapshot(array $params, array $initialResponse): array
    {
        $legacyParams = $params;
        unset($legacyParams['page'], $legacyParams['includeTotal']);
        $legacyParams['limit'] = 500;

        $query = http_build_query($legacyParams);
        $path = 'api/printjobs' . ($query ? '?' . $query : '');
        $response = $this->api->get($path) ?? $initialResponse;

        return is_array($response) ? $response : [];
    }

    private function resolvePageSize(int $value): int
    {
        $allowed = [50, 100, 250, 500];

        return in_array($value, $allowed, true) ? $value : 100;
    }

    public function reintentar(Request $request, string $id): RedirectResponse|JsonResponse
    {
        $result = $this->routeJob($id);

        return $this->respondAction(
            $request,
            $result,
            $result['ok'] ? 'Reintento enviado.' : ($result['error'] ?? 'No se pudo reintentar el trabajo seleccionado.')
        );
    }

    public function cancelar(Request $request, string $id): RedirectResponse|JsonResponse
    {
        $result = $this->cancelJob($id);

        return $this->respondAction(
            $request,
            $result,
            $result['ok'] ? 'Trabajo cancelado.' : ($result['error'] ?? 'No se pudo cancelar el trabajo seleccionado.')
        );
    }

    public function reintentarMasivo(Request $request): RedirectResponse|JsonResponse
    {
        $jobIds = $this->normalizeJobIds($request->input('jobIds'));
        if ($jobIds === []) {
            return $this->respondBulk($request, [], 0, 0, 'Selecciona al menos un trabajo.', true);
        }

        $results = [];
        $ok = 0;
        $errors = [];

        foreach ($jobIds as $jobId) {
            $result = $this->routeJob($jobId);
            $results[] = $result;
            if ($result['ok']) {
                $ok++;
            } else {
                $errors[] = $result['error'] ?? 'No se pudo reintentar el trabajo.';
            }
        }

        $total = count($jobIds);
        $message = $errors === []
            ? "Reintento enviado para {$ok} trabajo(s)."
            : $this->bulkActionMessage('reintentar', $total, $ok, $errors, 'Solo se pueden reintentar trabajos pendientes o en error final.');

        return $this->respondBulk($request, $results, $ok, $total, $message);
    }

    public function cancelarMasivo(Request $request): RedirectResponse|JsonResponse
    {
        $jobIds = $this->normalizeJobIds($request->input('jobIds'));
        if ($jobIds === []) {
            return $this->respondBulk($request, [], 0, 0, 'Selecciona al menos un trabajo.', true);
        }

        $results = [];
        $ok = 0;
        $errors = [];

        foreach ($jobIds as $jobId) {
            $result = $this->cancelJob($jobId);
            $results[] = $result;
            if ($result['ok']) {
                $ok++;
            } else {
                $errors[] = $result['error'] ?? 'No se pudo cancelar el trabajo.';
            }
        }

        $total = count($jobIds);
        $message = $errors === []
            ? "Cancelación enviada para {$ok} trabajo(s)."
            : $this->bulkActionMessage('cancelar', $total, $ok, $errors, 'Los trabajos seleccionados no se pueden cancelar en su estado actual.');

        return $this->respondBulk($request, $results, $ok, $total, $message);
    }

    /**
     * @return array{ok: bool, jobId: string, newStatus?: int, message?: string, error?: string}
     */
    private function routeJob(string $id): array
    {
        try {
            $apiResult = $this->api->post("api/printjobs/{$id}/route");
            $statusKey = strtolower((string) ($apiResult['status'] ?? $apiResult['Status'] ?? 'routed'));
            $newStatus = $statusKey === 'errorfinal' ? 8 : 1;

            return [
                'ok' => true,
                'jobId' => $id,
                'newStatus' => $newStatus,
                'message' => $newStatus === 8
                    ? 'Reintento enviado; el trabajo permanece en error final.'
                    : 'Reintento enviado.',
            ];
        } catch (RequestException $e) {
            return [
                'ok' => false,
                'jobId' => $id,
                'error' => $this->apiErrorMessage($e, 'No se pudo reintentar el trabajo seleccionado.'),
            ];
        }
    }

    /**
     * @return array{ok: bool, jobId: string, newStatus?: int, error?: string}
     */
    private function cancelJob(string $id): array
    {
        try {
            $this->api->post("api/printjobs/{$id}/cancel");

            return [
                'ok' => true,
                'jobId' => $id,
                'newStatus' => 7,
            ];
        } catch (RequestException $e) {
            $statusCode = $e->getResponse()?->getStatusCode();
            $error = $statusCode === 404
                ? 'La API activa no tiene disponible el endpoint de cancelacion. Reinicia la API para cargar la version actual.'
                : $this->apiErrorMessage($e, 'No se pudo cancelar el trabajo seleccionado.');

            return [
                'ok' => false,
                'jobId' => $id,
                'error' => $error,
            ];
        }
    }

    /**
     * @param  array<int, string|mixed>  $raw
     * @return array<int, string>
     */
    private function normalizeJobIds(mixed $raw): array
    {
        if (! is_array($raw)) {
            return [];
        }

        $ids = [];
        foreach ($raw as $jobIdRaw) {
            $jobId = is_string($jobIdRaw) ? trim($jobIdRaw) : '';
            if ($jobId !== '') {
                $ids[] = $jobId;
            }
        }

        return $ids;
    }

    /**
     * @param  array{ok: bool, jobId: string, newStatus?: int, message?: string, error?: string}  $result
     */
    private function respondAction(Request $request, array $result, string $flashMessage): RedirectResponse|JsonResponse
    {
        if ($this->wantsJson($request)) {
            return response()->json([
                'ok' => $result['ok'],
                'message' => $flashMessage,
                'results' => [$result],
            ], $result['ok'] ? 200 : 422);
        }

        return $result['ok']
            ? back()->with('success', $flashMessage)
            : back()->with('error', $flashMessage);
    }

    /**
     * @param  array<int, array{ok: bool, jobId: string, newStatus?: int, message?: string, error?: string}>  $results
     */
    private function respondBulk(
        Request $request,
        array $results,
        int $ok,
        int $total,
        string $message,
        bool $validationError = false
    ): RedirectResponse|JsonResponse {
        $allOk = $total > 0 && $ok === $total;
        $noneOk = $ok === 0 && $total > 0;

        if ($this->wantsJson($request)) {
            return response()->json([
                'ok' => $allOk,
                'partial' => ! $allOk && ! $noneOk && $total > 0,
                'message' => $message,
                'processed' => $ok,
                'total' => $total,
                'results' => $results,
            ], ($noneOk || $validationError) ? 422 : 200);
        }

        if ($allOk) {
            return back()->with('success', $message);
        }

        return back()->with('error', $message);
    }

    private function wantsJson(Request $request): bool
    {
        return $request->expectsJson() || $request->ajax();
    }

    /**
     * @param  array<int, string>  $errors
     */
    private function bulkActionMessage(string $action, int $total, int $ok, array $errors, string $defaultReason): string
    {
        $failed = max(0, $total - $ok);
        $uniqueReasons = array_values(array_unique(array_filter($errors)));
        $reason = $uniqueReasons[0] ?? $defaultReason;

        if (str_contains($reason, 'no se puede reintentar')) {
            $reason = 'Solo se pueden reintentar trabajos pendientes o en error final.';
        } elseif (str_contains($reason, 'no se puede cancelar')) {
            $reason = 'Los trabajos seleccionados no se pueden cancelar en su estado actual.';
        }

        if ($ok === 0) {
            return "No se pudo {$action} ningun trabajo seleccionado. {$reason}";
        }

        return "Se procesaron {$ok} de {$total} trabajo(s). {$failed} no se pudieron {$action}. {$reason}";
    }
}
