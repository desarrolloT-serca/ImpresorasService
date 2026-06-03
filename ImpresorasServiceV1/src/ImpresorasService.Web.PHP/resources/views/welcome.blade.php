<!DOCTYPE html>
<html lang="{{ str_replace('_', '-', app()->getLocale()) }}">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title>{{ config('app.name', 'Impresoras Service') }}</title>
    @if (file_exists(public_path('build/manifest.json')) || file_exists(public_path('hot')))
        @vite(['resources/css/app.css', 'resources/js/app.js'])
    @endif
</head>
<body class="min-h-screen flex items-center justify-center p-4">
    <main class="card max-w-xl w-full p-6">
        <h1 class="text-2xl font-semibold mb-2">Impresoras Service</h1>
        <p class="muted-text mb-6">Panel web para gestion operativa de cola, impresoras, reglas y alertas.</p>
        <div class="flex flex-wrap gap-2">
            @auth
                <a href="{{ url('/dashboard') }}" class="btn btn-primary">Ir al dashboard</a>
            @else
                <a href="{{ route('login') }}" class="btn btn-primary">Iniciar sesi&oacute;n</a>
            @endauth
        </div>
    </main>
</body>
</html>
