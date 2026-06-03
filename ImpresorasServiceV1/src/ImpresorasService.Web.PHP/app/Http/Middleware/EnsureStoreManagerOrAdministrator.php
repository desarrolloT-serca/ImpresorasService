<?php

namespace App\Http\Middleware;

use App\Helpers\AuthHelper;
use Closure;
use Illuminate\Http\Request;
use Symfony\Component\HttpFoundation\Response;

class EnsureStoreManagerOrAdministrator
{
    public function handle(Request $request, Closure $next): Response
    {
        if (! AuthHelper::isStoreManagerOrAdmin()) {
            abort(403, 'Acceso denegado. Esta sección requiere rol jefe de tienda o administrador.');
        }

        return $next($request);
    }
}
