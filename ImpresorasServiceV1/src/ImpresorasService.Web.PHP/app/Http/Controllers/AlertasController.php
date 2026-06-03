<?php

namespace App\Http\Controllers;

use App\Helpers\AuthHelper;
use App\Services\ApiClient;
use GuzzleHttp\Exception\RequestException;
use Illuminate\Http\Request;
use Illuminate\View\View;

class AlertasController extends Controller
{
    public function __construct(private readonly ApiClient $api)
    {
    }

    public function index(Request $request): View
    {
        $externalJobId = trim((string) $request->input('externalJobId', ''));
        $effectiveStore = AuthHelper::getEffectiveStoreId();
        $requestedStore = $request->filled('storeId') ? $request->integer('storeId') : null;
        if ($requestedStore !== null && $requestedStore < 1) {
            $requestedStore = null;
        }
        // Si el usuario viene con un storeId explícito (por ejemplo al clicar desde el dashboard),
        // debe tener prioridad sobre "selected_store_id" de sesión.
        $storeIdToUse = $requestedStore ?? $effectiveStore;

        // Construimos el query para filtrar por storeId.
        // Nota: aunque la API (según roles) pueda ignorar el storeId del query,
        // aplicamos un filtro extra en PHP para que la UI respete el storeId solicitado.
        $path = 'api/printjobs?status=8' . ($storeIdToUse !== null ? '&storeId=' . $storeIdToUse : '');
        try {
            $jobs = $this->api->get($path) ?? [];
            $jobs = is_array($jobs) ? $jobs : [];

            // Filtro defensivo por storeId para evitar diferencias por roles en la API.
            if ($storeIdToUse !== null) {
                $jobs = array_values(array_filter($jobs, function ($job) use ($storeIdToUse) {
                    $jobStore = $job['storeId'] ?? $job['StoreId'] ?? null;
                    if ($jobStore === null || $jobStore === '') return false;
                    return (int) $jobStore === (int) $storeIdToUse;
                }));
            }

            if ($externalJobId !== '') {
                $needle = mb_strtolower($externalJobId);
                $jobs = array_values(array_filter($jobs, static function ($job) use ($needle) {
                    $value = (string) ($job['externalJobId'] ?? $job['ExternalJobId'] ?? '');
                    return mb_stripos($value, $needle) !== false;
                }));
            }

            return view('alertas', [
                'jobs' => $jobs,
                'externalJobId' => $externalJobId,
                'storeId' => $requestedStore,
            ]);
        } catch (RequestException $e) {
            $statusCode = $e->getResponse()?->getStatusCode();
            $apiError = $statusCode === 404
                ? 'No se encontraron alertas para la tienda seleccionada.'
                : 'Error consultando alertas.';

            return view('alertas', [
                'jobs' => [],
                'apiError' => $apiError,
                'externalJobId' => $externalJobId,
                'storeId' => $requestedStore,
            ]);
        }
    }
}
