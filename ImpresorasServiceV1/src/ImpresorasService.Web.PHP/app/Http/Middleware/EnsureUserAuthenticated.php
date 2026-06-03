<?php

namespace App\Http\Middleware;

use Closure;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\Session;
use Symfony\Component\HttpFoundation\Response;

class EnsureUserAuthenticated
{
    public function handle(Request $request, Closure $next): Response
    {
        if (!Session::has('impresoras_token')) {
            return redirect()->route('login');
        }

        return $next($request);
    }
}
