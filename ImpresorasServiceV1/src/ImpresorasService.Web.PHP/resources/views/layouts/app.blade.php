<!DOCTYPE html>
<html lang="{{ str_replace('_', '-', app()->getLocale()) }}" data-theme="">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <meta name="csrf-token" content="{{ csrf_token() }}">
    <title>@yield('title', 'Impresoras Service')</title>
    @vite(['resources/css/app.css', 'resources/js/app.js'])
    @yield('page_styles')
    @vite('resources/css/dbx.css')
    @vite('resources/css/system.css')
</head>
<body class="min-h-screen">
    <div class="app-shell">
        <div id="sidebar-overlay" class="sidebar-overlay"></div>
        <aside class="app-sidebar">
            <div class="app-brand">
                @if(file_exists(public_path('img/serca-logo.png')))
                    <img src="{{ asset('img/serca-logo.png') }}" alt="AD Serca" class="app-logo">
                @elseif(file_exists(public_path('img/serca-logo.svg')))
                    <img src="{{ asset('img/serca-logo.svg') }}" alt="AD Serca" class="app-logo">
                @elseif(file_exists(public_path('img/ad-logo.svg')))
                    <img src="{{ asset('img/ad-logo.svg') }}" alt="AD Serca" class="app-logo">
                @elseif(file_exists(public_path('img/logo.png')))
                    <img src="{{ asset('img/logo.png') }}" alt="Logo" class="app-logo">
                @else
                    <div class="app-logo-fallback">AD</div>
                @endif
                <span class="text-white font-semibold">Impresoras Service</span>
            </div>

            <nav class="app-nav">
                <a href="{{ route('dashboard') }}" class="app-nav-link {{ request()->routeIs('dashboard') ? 'app-nav-link-active' : '' }}" @if(request()->routeIs('dashboard')) aria-current="page" @endif>
                    <svg class="nav-icon" viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="3" y="3" width="7" height="7" rx="1"/><rect x="14" y="3" width="7" height="7" rx="1"/><rect x="3" y="14" width="7" height="7" rx="1"/><rect x="14" y="14" width="7" height="7" rx="1"/></svg>
                    Dashboard
                </a>
                <a href="{{ url('/cola') }}" class="app-nav-link {{ request()->is('cola*') ? 'app-nav-link-active' : '' }}" @if(request()->is('cola*')) aria-current="page" @endif>
                    <svg class="nav-icon" viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M8 6h13M8 12h13M8 18h13M3 6h.01M3 12h.01M3 18h.01"/></svg>
                    Cola
                </a>
                @if(($isStoreManager ?? false) || ($isAdmin ?? false))
                    <a href="{{ route('impresoras.index') }}" class="app-nav-link {{ request()->routeIs('impresoras.*') ? 'app-nav-link-active' : '' }}" @if(request()->routeIs('impresoras.*')) aria-current="page" @endif>
                        <svg class="nav-icon" viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M6 9V2h12v7"/><path d="M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2"/><rect x="6" y="14" width="12" height="8" rx="1"/></svg>
                        Impresoras
                    </a>
                @endif
                @if($isAdmin ?? false)
                    <a href="{{ route('reglas.index') }}" class="app-nav-link {{ request()->routeIs('reglas.*') ? 'app-nav-link-active' : '' }}" @if(request()->routeIs('reglas.*')) aria-current="page" @endif>
                        <svg class="nav-icon" viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><line x1="4" y1="6" x2="20" y2="6"/><line x1="4" y1="12" x2="20" y2="12"/><line x1="4" y1="18" x2="12" y2="18"/><polyline points="15 15 18 18 21 15"/><line x1="18" y1="18" x2="18" y2="11"/></svg>
                        Reglas
                    </a>
                    <a href="{{ route('tiendas.index') }}" class="app-nav-link {{ request()->routeIs('tiendas.*') ? 'app-nav-link-active' : '' }}" @if(request()->routeIs('tiendas.*')) aria-current="page" @endif>
                        <svg class="nav-icon" viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M3 9l9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/><polyline points="9 22 9 12 15 12 15 22"/></svg>
                        Tiendas
                    </a>
                    <a href="{{ route('usuarios.index') }}" class="app-nav-link {{ request()->routeIs('usuarios.*') ? 'app-nav-link-active' : '' }}" @if(request()->routeIs('usuarios.*')) aria-current="page" @endif>
                        <svg class="nav-icon" viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75"/></svg>
                        Usuarios
                    </a>
                    <a href="{{ route('ajustes.index') }}" class="app-nav-link {{ request()->routeIs('ajustes.*') ? 'app-nav-link-active' : '' }}" @if(request()->routeIs('ajustes.*')) aria-current="page" @endif>
                        <svg class="nav-icon" viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z"/></svg>
                        Ajustes
                    </a>
                @endif
                <a href="{{ url('/alertas') }}" class="app-nav-link {{ request()->is('alertas*') ? 'app-nav-link-active' : '' }}" @if(request()->is('alertas*')) aria-current="page" @endif>
                    <svg class="nav-icon" viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9"/><path d="M13.73 21a2 2 0 0 1-3.46 0"/></svg>
                    Alertas
                </a>
            </nav>
            <div class="app-sidebar-footer">
                <button type="button" id="theme-toggle" class="btn btn-ghost theme-toggle" title="Cambiar tema" aria-label="Cambiar tema">
                    <svg class="theme-icon theme-icon-sun" viewBox="0 0 24 24" aria-hidden="true" focusable="false">
                        <circle cx="12" cy="12" r="4" />
                        <path d="M12 2v2" />
                        <path d="M12 20v2" />
                        <path d="M4.93 4.93l1.41 1.41" />
                        <path d="M17.66 17.66l1.41 1.41" />
                        <path d="M2 12h2" />
                        <path d="M20 12h2" />
                        <path d="M4.93 19.07l1.41-1.41" />
                        <path d="M17.66 6.34l1.41-1.41" />
                    </svg>
                    <svg class="theme-icon theme-icon-moon" viewBox="0 0 24 24" aria-hidden="true" focusable="false">
                        <path d="M21 12.79A8.5 8.5 0 1 1 11.21 3a6.8 6.8 0 0 0 9.79 9.79Z" />
                    </svg>
                </button>
            </div>
        </aside>
        <main class="app-content">
            <header class="app-topbar">
                <div class="app-topbar-title">
                    <button type="button" id="sidebar-toggle" class="btn btn-ghost sidebar-toggle" title="Abrir menu" aria-label="Abrir menu">&#9776;</button>
                    <h1 class="text-2xl font-semibold">@yield('title', 'Dashboard')</h1>
                </div>
                <div class="app-topbar-actions flex items-center gap-2 flex-wrap">
                    @php
                        $contextStoreId = $effectiveStoreId ?? $authStoreId ?? null;
                        $contextStore = $contextStoreId !== null
                            ? collect($storeOptions ?? [])->firstWhere('storeId', (int) $contextStoreId)
                            : null;
                        $contextStoreName = is_array($contextStore ?? null) ? ($contextStore['name'] ?? null) : null;
                    @endphp
                    <span class="badge badge-neutral app-context-badge">
                        @if($isAdmin ?? false)
                            {{ $authRoleLabel ?? 'Administrador' }}
                        @elseif($contextStoreId !== null)
                            {{ \App\Helpers\StoreFormat::label($contextStoreId, $contextStoreName) }}
                        @else
                            {{ $authRoleLabel ?? 'Empleado' }}
                        @endif
                    </span>
                    @if($isAdmin ?? false)
                        <form method="POST" action="{{ url('/store-filter') }}" class="flex items-center gap-2">
                            @csrf
                            <label for="store-filter" class="text-sm">Tienda:</label>
                            <select name="storeId" id="store-filter" onchange="this.form.submit()" class="select !w-auto">
                                <option value="">Todas</option>
                                @foreach($storeOptions ?? [] as $store)
                                    <option value="{{ $store['storeId'] }}" {{ (string)($selectedStoreId ?? '') === (string)$store['storeId'] ? 'selected' : '' }}>
                                        {{ \App\Helpers\StoreFormat::label($store['storeId'], $store['name']) }}
                                    </option>
                                @endforeach
                            </select>
                        </form>
                    @endif
                    <form action="{{ route('logout') }}" method="POST" class="inline">
                        @csrf
                        <button type="submit" class="btn btn-danger">Cerrar sesi&oacute;n</button>
                    </form>
                </div>
            </header>
            @if(session('error'))
                <div class="mb-4 alert alert-error" role="alert">{{ session('error') }}</div>
            @endif
            @if($errors->any())
                <div class="mb-4 alert alert-error" role="alert">
                    <ul class="list-disc pl-5 space-y-1">
                        @foreach($errors->all() as $error)
                            <li>{{ $error }}</li>
                        @endforeach
                    </ul>
                </div>
            @endif
            @yield('content')
        </main>
    </div>
    <script>
        (function() {
            function closeSidebar() {
                document.body.classList.remove('sidebar-open');
            }

            function toggleSidebar() {
                document.body.classList.toggle('sidebar-open');
            }

            function applyTheme(isDark) {
                document.documentElement.setAttribute('data-theme', isDark ? 'dark' : '');
                document.documentElement.classList.toggle('dark', isDark);
                const toggle = document.getElementById('theme-toggle');
                if (toggle) {
                    toggle.setAttribute('aria-label', isDark ? 'Cambiar a modo claro' : 'Cambiar a modo oscuro');
                    toggle.setAttribute('title', isDark ? 'Cambiar a modo claro' : 'Cambiar a modo oscuro');
                }
            }

            function syncServerRenderedControls() {
                document.querySelectorAll('[data-current]').forEach(function(el) {
                    const value = el.getAttribute('data-current');
                    if (value !== null) el.value = value;
                });
            }

            function initAutoFilters() {
                const forms = Array.from(document.querySelectorAll('form[method="GET"], form[method="get"]'))
                    .filter(form =>
                        form.classList.contains('dbx-filters')
                        || form.classList.contains('filters-row')
                        || form.querySelector('.dbx-filters, .filters-row')
                    );
                const debounceMs = 250;

                forms.forEach(form => {
                    let inputTimer = null;

                    const submitNow = () => {
                        if (typeof form.requestSubmit === 'function') {
                            form.requestSubmit();
                        } else {
                            form.submit();
                        }
                    };

                    form.querySelectorAll('select, input[type="checkbox"], input[type="radio"]').forEach(el => {
                        el.addEventListener('change', submitNow);
                    });

                    form.querySelectorAll('input[type="text"], input[type="search"], input[type="number"], input[type="date"], input[type="datetime-local"]').forEach(el => {
                        el.addEventListener('input', () => {
                            if (inputTimer) {
                                window.clearTimeout(inputTimer);
                            }
                            inputTimer = window.setTimeout(submitNow, debounceMs);
                        });
                        el.addEventListener('change', submitNow);
                    });
                });
            }

            const stored = localStorage.getItem('theme') || 'light';
            applyTheme(stored === 'dark');
            syncServerRenderedControls();
            initAutoFilters();
            document.getElementById('sidebar-toggle')?.addEventListener('click', toggleSidebar);
            document.getElementById('sidebar-overlay')?.addEventListener('click', closeSidebar);
            window.addEventListener('resize', function() {
                if (window.innerWidth > 1024) {
                    closeSidebar();
                }
            });
            document.getElementById('theme-toggle')?.addEventListener('click', function() {
                const isDark = document.documentElement.classList.contains('dark');
                const next = isDark ? 'light' : 'dark';
                applyTheme(next === 'dark');
                localStorage.setItem('theme', next);
            });
        })();
    </script>
    @yield('page_scripts')
</body>
</html>
