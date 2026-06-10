<?php

namespace App\Http\Controllers;

use App\Helpers\AuthHelper;
use App\Services\ApiClient;
use Illuminate\Http\RedirectResponse;
use Illuminate\Http\Request;
use Illuminate\View\View;
use GuzzleHttp\Exception\RequestException;

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

        if ($total > 0 && $page > $lastPage) {
            $params['page'] = $lastPage;
            [$jobs, $total, $page, $limit] = $this->fetchQueuePage($params, $externalJobId, $lastPage, $limit);
            $lastPage = max(1, (int) ceil($total / max(1, $limit)));
        }

        // Normalizar status a int para compatibilidad con la vista
        foreach ($jobs as &$job) {
            $s = $job['status'] ?? $job['Status'] ?? null;
            if ($s !== null && $s !== '') {
                $job['_status'] = is_numeric($s) ? (int) $s : $s;
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
     * Compatibilidad con APIs aun no reiniciadas: obtienen una lista plana limitada.
     *
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

    public function reintentar(string $id): RedirectResponse
    {
        try {
            $this->api->post("api/printjobs/{$id}/route");
            return back()->with('success', 'Reintento enviado.');
        } catch (\GuzzleHttp\Exception\RequestException $e) {
            return back()->with('error', $this->apiErrorMessage(
                $e,
                'No se pudo reintentar el trabajo seleccionado.'
            ));
        }
    }

    public function cancelar(string $id): RedirectResponse
    {
        try {
            $this->api->post("api/printjobs/{$id}/cancel");
            return back()->with('success', 'Trabajo cancelado.');
        } catch (\GuzzleHttp\Exception\RequestException $e) {
            $statusCode = $e->getResponse()?->getStatusCode();
            if ($statusCode === 404) {
                return back()->with(
                    'error',
                    'La API activa no tiene disponible el endpoint de cancelacion. Reinicia la API para cargar la version actual.'
                );
            }

            return back()->with('error', $this->apiErrorMessage(
                $e,
                'No se pudo cancelar el trabajo seleccionado.'
            ));
        }
    }

    public function reintentarMasivo(Request $request): RedirectResponse
    {
        $jobIds = $request->input('jobIds');
        if (!is_array($jobIds) || count($jobIds) === 0) {
            return back()->with('error', 'Selecciona al menos un trabajo.');
        }

        $total = count($jobIds);
        $ok = 0;
        $errors = [];

        foreach ($jobIds as $jobIdRaw) {
            $jobId = is_string($jobIdRaw) ? trim($jobIdRaw) : '';
            if ($jobId === '') continue;

            try {
                $this->api->post("api/printjobs/{$jobId}/route");
                $ok++;
            } catch (RequestException $e) {
                $errors[] = $this->apiErrorMessage($e, 'No se pudo reintentar el trabajo.');
            }
        }

        if (!empty($errors)) {
            return back()->with([
                'error' => $this->bulkActionMessage(
                    'reintentar',
                    $total,
                    $ok,
                    $errors,
                    'Solo se pueden reintentar trabajos pendientes o en error final.'
                ),
            ]);
        }

        return back()->with('success', "Reintento enviado para {$ok} trabajo(s).");
    }

    public function cancelarMasivo(Request $request): RedirectResponse
    {
        $jobIds = $request->input('jobIds');
        if (!is_array($jobIds) || count($jobIds) === 0) {
            return back()->with('error', 'Selecciona al menos un trabajo.');
        }

        $total = count($jobIds);
        $ok = 0;
        $errors = [];

        foreach ($jobIds as $jobIdRaw) {
            $jobId = is_string($jobIdRaw) ? trim($jobIdRaw) : '';
            if ($jobId === '') continue;

            try {
                $this->api->post("api/printjobs/{$jobId}/cancel");
                $ok++;
            } catch (RequestException $e) {
                $errors[] = $this->apiErrorMessage($e, 'No se pudo cancelar el trabajo.');
            }
        }

        if (!empty($errors)) {
            return back()->with([
                'error' => $this->bulkActionMessage(
                    'cancelar',
                    $total,
                    $ok,
                    $errors,
                    'Los trabajos seleccionados no se pueden cancelar en su estado actual.'
                ),
            ]);
        }

        return back()->with('success', "Cancelación enviada para {$ok} trabajo(s).");
    }

    /**
     * @param array<int, string> $errors
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
