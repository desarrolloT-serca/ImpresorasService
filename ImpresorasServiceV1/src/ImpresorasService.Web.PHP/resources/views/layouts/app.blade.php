<!DOCTYPE html>
<html lang="{{ str_replace('_', '-', app()->getLocale()) }}" data-theme="">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <meta name="csrf-token" content="{{ csrf_token() }}">
    <title>@yield('title', 'Impresoras Service')</title>
    <link rel="preconnect" href="https://fonts.bunny.net">
    <link href="https://fonts.bunny.net/css?family=poppins:400,500,600,700" rel="stylesheet" />
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
                <a href="{{ route('dashboard') }}" class="app-nav-link {{ request()->routeIs('dashboard') ? 'app-nav-link-active' : '' }}" @if(request()->routeIs('dashboard')) aria-current="page" @endif>Dashboard</a>
                <a href="{{ url('/cola') }}" class="app-nav-link {{ request()->is('cola*') ? 'app-nav-link-active' : '' }}" @if(request()->is('cola*')) aria-current="page" @endif>Cola</a>
                @if(($isStoreManager ?? false) || ($isAdmin ?? false))
                    <a href="{{ route('impresoras.index') }}" class="app-nav-link {{ request()->routeIs('impresoras.*') ? 'app-nav-link-active' : '' }}" @if(request()->routeIs('impresoras.*')) aria-current="page" @endif>Impresoras</a>
                @endif
                @if($isAdmin ?? false)
                    <a href="{{ route('reglas.index') }}" class="app-nav-link {{ request()->routeIs('reglas.*') ? 'app-nav-link-active' : '' }}" @if(request()->routeIs('reglas.*')) aria-current="page" @endif>Reglas</a>
                    <a href="{{ route('tiendas.index') }}" class="app-nav-link {{ request()->routeIs('tiendas.*') ? 'app-nav-link-active' : '' }}" @if(request()->routeIs('tiendas.*')) aria-current="page" @endif>Tiendas</a>
                    <a href="{{ route('usuarios.index') }}" class="app-nav-link {{ request()->routeIs('usuarios.*') ? 'app-nav-link-active' : '' }}" @if(request()->routeIs('usuarios.*')) aria-current="page" @endif>Usuarios</a>
                    <a href="{{ route('ajustes.index') }}" class="app-nav-link {{ request()->routeIs('ajustes.*') ? 'app-nav-link-active' : '' }}" @if(request()->routeIs('ajustes.*')) aria-current="page" @endif>Ajustes</a>
                @endif
                <a href="{{ url('/alertas') }}" class="app-nav-link {{ request()->is('alertas*') ? 'app-nav-link-active' : '' }}" @if(request()->is('alertas*')) aria-current="page" @endif>Alertas</a>
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
                    <span class="badge badge-neutral app-context-badge">
                        @if($isAdmin ?? false)
                            {{ $authRoleLabel ?? 'Administrador' }}
                        @elseif(($effectiveStoreId ?? $authStoreId ?? null) !== null)
                            Tienda {{ $effectiveStoreId ?? $authStoreId }}
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
                                        {{ $store['name'] }} ({{ $store['storeId'] }})
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
