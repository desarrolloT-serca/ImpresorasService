<!DOCTYPE html>
<html lang="{{ str_replace('_', '-', app()->getLocale()) }}" data-theme="">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <meta name="csrf-token" content="{{ csrf_token() }}">
    <title>@yield('title', 'Impresoras Service')</title>
    <link rel="icon" type="image/svg+xml" href="{{ asset('img/serca-logo.svg') }}">
    <link rel="icon" type="image/png" href="{{ asset('img/serca-logo.png') }}">
    <meta name="theme-color" id="theme-color-meta" content="#1b2f82">
    @vite(['resources/css/app.css', 'resources/js/app.js'])
    @yield('page_styles')
    @vite('resources/css/dbx.css')
    @vite('resources/css/system.css')
    <script>if(localStorage.getItem('sidebar-pinned')==='true'){document.documentElement.classList.add('sidebar-pinned');}</script>
</head>
<body class="min-h-screen">
    <div class="app-shell">
        <div id="sidebar-overlay" class="sidebar-overlay"></div>
        <aside class="app-sidebar" id="app-sidebar">
            <div class="app-brand">
                @if(file_exists(public_path('img/serca-logo.png')))
                    <img src="{{ asset('img/serca-logo.png') }}" alt="AD Serca" class="app-logo app-logo-full">
                @elseif(file_exists(public_path('img/serca-logo.svg')))
                    <img src="{{ asset('img/serca-logo.svg') }}" alt="AD Serca" class="app-logo app-logo-full">
                @elseif(file_exists(public_path('img/ad-logo.svg')))
                    <img src="{{ asset('img/ad-logo.svg') }}" alt="AD Serca" class="app-logo app-logo-full">
                @elseif(file_exists(public_path('img/logo.png')))
                    <img src="{{ asset('img/logo.png') }}" alt="Logo" class="app-logo app-logo-full">
                @else
                    <div class="app-logo-fallback app-logo-full">AD</div>
                @endif
                <span class="app-brand-name">Impresoras Service</span>
            </div>

            <nav class="app-nav">
                <span class="app-nav-group-title">Operación</span>
                <a href="{{ route('dashboard') }}" class="app-nav-link {{ request()->routeIs('dashboard') ? 'app-nav-link-active' : '' }}" @if(request()->routeIs('dashboard')) aria-current="page" @endif data-label="Dashboard">
                    <svg class="nav-icon" viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="3" y="3" width="7" height="7" rx="1"/><rect x="14" y="3" width="7" height="7" rx="1"/><rect x="3" y="14" width="7" height="7" rx="1"/><rect x="14" y="14" width="7" height="7" rx="1"/></svg>
                    <span class="nav-label">Dashboard</span>
                    <span class="nav-tooltip" aria-hidden="true">Dashboard</span>
                </a>
                <a href="{{ url('/cola') }}" class="app-nav-link {{ request()->is('cola*') ? 'app-nav-link-active' : '' }}" @if(request()->is('cola*')) aria-current="page" @endif data-label="Cola">
                    <svg class="nav-icon" viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M8 6h13M8 12h13M8 18h13M3 6h.01M3 12h.01M3 18h.01"/></svg>
                    <span class="nav-label">Cola</span>
                    <span class="nav-tooltip" aria-hidden="true">Cola</span>
                </a>
                <a href="{{ route('alertas.index') }}" class="app-nav-link {{ request()->routeIs('alertas.index') ? 'app-nav-link-active' : '' }}" @if(request()->routeIs('alertas.index')) aria-current="page" @endif data-label="Alertas">
                    <svg class="nav-icon" viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9"/><path d="M13.73 21a2 2 0 0 1-3.46 0"/></svg>
                    <span class="nav-label">Alertas</span>
                    <span class="nav-tooltip" aria-hidden="true">Alertas</span>
                </a>
                @if(($isStoreManager ?? false) || ($isAdmin ?? false))
                    <a href="{{ route('impresoras.index') }}" class="app-nav-link {{ request()->routeIs('impresoras.*') ? 'app-nav-link-active' : '' }}" @if(request()->routeIs('impresoras.*')) aria-current="page" @endif data-label="Impresoras">
                        <svg class="nav-icon" viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M6 9V2h12v7"/><path d="M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2"/><rect x="6" y="14" width="12" height="8" rx="1"/></svg>
                        <span class="nav-label">Impresoras</span>
                        <span class="nav-tooltip" aria-hidden="true">Impresoras</span>
                    </a>
                @endif
                @if($isAdmin ?? false)
                    <span class="app-nav-group-title">Configuración</span>
                    <a href="{{ route('reglas.index') }}" class="app-nav-link {{ request()->routeIs('reglas.*') ? 'app-nav-link-active' : '' }}" @if(request()->routeIs('reglas.*')) aria-current="page" @endif data-label="Reglas">
                        <svg class="nav-icon" viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="6" cy="12" r="2"/><circle cx="18" cy="6" r="2"/><circle cx="18" cy="18" r="2"/><path d="M8 12h4"/><path d="M12 12l4-6"/><path d="M12 12l4 6"/></svg>
                        <span class="nav-label">Reglas</span>
                        <span class="nav-tooltip" aria-hidden="true">Reglas</span>
                    </a>
                    <a href="{{ route('tiendas.index') }}" class="app-nav-link {{ request()->routeIs('tiendas.*') ? 'app-nav-link-active' : '' }}" @if(request()->routeIs('tiendas.*')) aria-current="page" @endif data-label="Tiendas">
                        <svg class="nav-icon" viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M3 9l9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/><polyline points="9 22 9 12 15 12 15 22"/></svg>
                        <span class="nav-label">Tiendas</span>
                        <span class="nav-tooltip" aria-hidden="true">Tiendas</span>
                    </a>
                    <a href="{{ route('usuarios.index') }}" class="app-nav-link {{ request()->routeIs('usuarios.*') ? 'app-nav-link-active' : '' }}" @if(request()->routeIs('usuarios.*')) aria-current="page" @endif data-label="Usuarios">
                        <svg class="nav-icon" viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75"/></svg>
                        <span class="nav-label">Usuarios</span>
                        <span class="nav-tooltip" aria-hidden="true">Usuarios</span>
                    </a>
                    <span class="app-nav-group-title">Sistema</span>
                    <a href="{{ route('ajustes.index') }}" class="app-nav-link {{ request()->routeIs('ajustes.*') ? 'app-nav-link-active' : '' }}" @if(request()->routeIs('ajustes.*')) aria-current="page" @endif data-label="Ajustes">
                        <svg class="nav-icon" viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z"/></svg>
                        <span class="nav-label">Ajustes</span>
                        <span class="nav-tooltip" aria-hidden="true">Ajustes</span>
                    </a>
                    <a href="{{ route('alertas.configuracion') }}" class="app-nav-link {{ request()->routeIs('alertas.configuracion') ? 'app-nav-link-active' : '' }}" @if(request()->routeIs('alertas.configuracion')) aria-current="page" @endif data-label="Telegram">
                        <svg class="nav-icon" viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><line x1="22" y1="2" x2="11" y2="13"/><polygon points="22 2 15 22 11 13 2 9 22 2"/></svg>
                        <span class="nav-label">Telegram</span>
                        <span class="nav-tooltip" aria-hidden="true">Telegram</span>
                    </a>
                    <span class="app-nav-group-title">Desarrollo</span>
                    <a href="{{ route('pruebas.index') }}" class="app-nav-link {{ request()->routeIs('pruebas.*') ? 'app-nav-link-active' : '' }}" @if(request()->routeIs('pruebas.*')) aria-current="page" @endif data-label="Pruebas">
                        <svg class="nav-icon" viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"/></svg>
                        <span class="nav-label">Pruebas</span>
                        <span class="nav-tooltip" aria-hidden="true">Pruebas</span>
                    </a>
                @endif
            </nav>
            <div class="app-sidebar-footer">
                <div class="app-sidebar-identity">
                    <span class="app-sidebar-identity-name">{{ $authUser['displayName'] ?? $authUser['login'] ?? 'Usuario' }}</span>
                    <span class="badge badge-neutral app-sidebar-identity-role">{{ $authRoleLabel ?? 'Empleado' }}</span>
                </div>
                <div class="app-sidebar-footer-actions">
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
                    <button type="button" id="sidebar-pin-toggle" class="btn btn-ghost sidebar-pin-btn" title="Fijar menú abierto" aria-label="Fijar menú abierto">
                        <svg class="icon-unpinned" viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                            <line x1="12" y1="17" x2="12" y2="22"/><path d="M5 17h14v-1.76a2 2 0 0 0-1.11-1.79l-1.78-.9A2 2 0 0 1 15 10.76V6h1a2 2 0 0 0 0-4H8a2 2 0 0 0 0 4h1v4.76a2 2 0 0 1-1.11 1.79l-1.78.9A2 2 0 0 0 5 15.24Z"/>
                        </svg>
                        <svg class="icon-pinned" viewBox="0 0 24 24" width="16" height="16" fill="currentColor" stroke="none" aria-hidden="true" style="display:none">
                            <line x1="12" y1="17" x2="12" y2="22" stroke="currentColor" stroke-width="2" fill="none"/><path d="M5 17h14v-1.76a2 2 0 0 0-1.11-1.79l-1.78-.9A2 2 0 0 1 15 10.76V6h1a2 2 0 0 0 0-4H8a2 2 0 0 0 0 4h1v4.76a2 2 0 0 1-1.11 1.79l-1.78.9A2 2 0 0 0 5 15.24Z"/>
                        </svg>
                    </button>
                </div>
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
                    <span class="badge badge-neutral app-context-badge">
                        @if($isAdmin ?? false)
                            {{ $authRoleLabel ?? 'Administrador' }}
                        @elseif($contextStoreId !== null)
                            {{ \App\Helpers\StoreFormat::label($contextStoreId, $contextStoreName) }}
                        @else
                            {{ $authRoleLabel ?? 'Empleado' }}
                        @endif
                    </span>
                    <form action="{{ route('logout') }}" method="POST" class="inline">
                        @csrf
                        <button type="submit" class="btn btn-danger btn-logout" title="Cerrar sesión" aria-label="Cerrar sesión">
                            <svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4"/><polyline points="16 17 21 12 16 7"/><line x1="21" y1="12" x2="9" y2="12"/></svg>
                            <span class="btn-logout-text">Cerrar sesión</span>
                        </button>
                    </form>
                </div>
            </header>
            @if($errors->any())
                <div class="mb-4 alert alert-error" role="alert">
                    <ul class="list-disc pl-5 space-y-1">
                        @foreach($errors->all() as $error)
                            <li>{{ $error }}</li>
                        @endforeach
                    </ul>
                </div>
            @endif
            @if(!empty($apiError))
                <div class="mb-4 alert alert-error" role="alert" aria-live="polite">
                    {{ $apiError }}
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
                const meta = document.getElementById('theme-color-meta');
                if (meta) meta.setAttribute('content', isDark ? '#1b1b1f' : '#1b2f82');
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
                const debounceMs = 400;
                const FOCUS_KEY = 'dbx-filter-focus';

                // El debounce dispara una navegacion GET completa (no hay fetch/AJAX),
                // asi que cada recarga pierde el foco y la posicion del cursor del input.
                // Guardamos donde estaba el usuario antes de recargar y lo restauramos
                // al terminar de cargar la pagina.
                try {
                    const saved = JSON.parse(sessionStorage.getItem(FOCUS_KEY) || 'null');
                    sessionStorage.removeItem(FOCUS_KEY);
                    if (saved && saved.path === location.pathname) {
                        const el = document.getElementById(saved.id);
                        if (el) {
                            el.focus();
                            if (typeof el.setSelectionRange === 'function' && saved.start != null) {
                                el.setSelectionRange(saved.start, saved.end);
                            }
                        }
                    }
                } catch (e) {}

                forms.forEach(form => {
                    let inputTimer = null;

                    const submitNow = () => {
                        if (typeof form.requestSubmit === 'function') {
                            form.requestSubmit();
                        } else {
                            form.submit();
                        }
                    };

                    const rememberFocus = (el) => {
                        if (!el.id) return;
                        try {
                            sessionStorage.setItem(FOCUS_KEY, JSON.stringify({
                                path: location.pathname,
                                id: el.id,
                                start: el.selectionStart,
                                end: el.selectionEnd,
                            }));
                        } catch (e) {}
                    };

                    form.querySelectorAll('select, input[type="checkbox"], input[type="radio"]').forEach(el => {
                        el.addEventListener('change', submitNow);
                    });

                    form.querySelectorAll('input[type="text"], input[type="search"], input[type="number"], input[type="date"], input[type="datetime-local"]').forEach(el => {
                        el.addEventListener('input', () => {
                            if (inputTimer) {
                                window.clearTimeout(inputTimer);
                            }
                            rememberFocus(el);
                            inputTimer = window.setTimeout(submitNow, debounceMs);
                        });
                        el.addEventListener('change', submitNow);
                    });
                });
            }

            function initSidebarPin() {
                const sidebar = document.getElementById('app-sidebar');
                if (!sidebar) return;
                const body = document.body;
                const isPinned = localStorage.getItem('sidebar-pinned') === 'true';

                document.documentElement.classList.remove('sidebar-pinned');
                if (isPinned) {
                    body.classList.add('sidebar-pinned');
                } else {
                    body.classList.add('sidebar-compact');
                }

                var collapseTimer = null;
                const sidebarFooter = sidebar.querySelector('.app-sidebar-footer');

                function expand() {
                    clearTimeout(collapseTimer);
                    if (!body.classList.contains('sidebar-pinned')) body.classList.remove('sidebar-compact');
                }

                function scheduleCollapse() {
                    if (!body.classList.contains('sidebar-pinned')) {
                        collapseTimer = setTimeout(function() { body.classList.add('sidebar-compact'); }, 500);
                    }
                }

                // Expand on nav/brand hover only — NOT on footer hover
                [sidebar.querySelector('.app-brand'), sidebar.querySelector('.app-nav')].forEach(function(el) {
                    if (el) el.addEventListener('mouseenter', expand);
                });

                // Footer: cancel pending collapse but do not expand
                if (sidebarFooter) {
                    sidebarFooter.addEventListener('mouseenter', function() { clearTimeout(collapseTimer); });
                }

                // Collapse when leaving the whole sidebar
                sidebar.addEventListener('mouseleave', scheduleCollapse);

                // Keyboard focus: expand only when focus enters nav/brand, not footer
                sidebar.addEventListener('focusin', function(e) {
                    if (sidebarFooter && sidebarFooter.contains(e.target)) {
                        clearTimeout(collapseTimer);
                        return;
                    }
                    expand();
                });
                sidebar.addEventListener('focusout', function(e) {
                    if (!body.classList.contains('sidebar-pinned') && !sidebar.contains(e.relatedTarget)) {
                        collapseTimer = setTimeout(function() { body.classList.add('sidebar-compact'); }, 400);
                    }
                });

                const pinBtn = document.getElementById('sidebar-pin-toggle');
                if (!pinBtn) return;

                const iconPinned = pinBtn.querySelector('.icon-pinned');
                const iconUnpinned = pinBtn.querySelector('.icon-unpinned');

                function syncPinBtn() {
                    const pinned = body.classList.contains('sidebar-pinned');
                    pinBtn.setAttribute('title', pinned ? 'Desfijar menú' : 'Fijar menú abierto');
                    pinBtn.setAttribute('aria-label', pinned ? 'Desfijar menú' : 'Fijar menú abierto');
                    if (iconPinned) iconPinned.style.display = pinned ? '' : 'none';
                    if (iconUnpinned) iconUnpinned.style.display = pinned ? 'none' : '';
                }

                syncPinBtn();

                pinBtn.addEventListener('click', function() {
                    const wasPinned = body.classList.contains('sidebar-pinned');
                    body.classList.toggle('sidebar-pinned', !wasPinned);
                    body.classList.toggle('sidebar-compact', wasPinned);
                    localStorage.setItem('sidebar-pinned', wasPinned ? 'false' : 'true');
                    syncPinBtn();
                });
            }

            const stored = localStorage.getItem('theme') || 'light';
            applyTheme(stored === 'dark');
            syncServerRenderedControls();
            initAutoFilters();
            initSidebarPin();
            document.getElementById('sidebar-toggle')?.addEventListener('click', toggleSidebar);
            document.getElementById('sidebar-overlay')?.addEventListener('click', closeSidebar);
            window.addEventListener('resize', function() {
                if (window.innerWidth > 480) {
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
    <style id="app-toast-critical">
        /* Fallback: los estilos completos viven en system.css (requiere npm run build). */
        #app-toast-region {
            position: fixed;
            bottom: 24px;
            right: 24px;
            z-index: 9999;
            display: flex;
            flex-direction: column;
            gap: 10px;
            pointer-events: none;
            max-width: min(380px, calc(100vw - 48px));
        }
        .app-toast {
            display: flex;
            align-items: flex-start;
            gap: 10px;
            padding: 12px 14px;
            background: var(--ui-surface-raised, #fff);
            border: 1px solid var(--ui-border, #d8dee8);
            border-left-width: 4px;
            border-radius: 6px;
            box-shadow: 0 10px 24px rgba(27, 47, 130, .08);
            pointer-events: all;
            opacity: 0;
            transform: translateX(20px);
            transition: opacity .22s ease, transform .22s ease;
            font-size: 13.5px;
            color: var(--ui-text, #1d2433);
            word-break: break-word;
        }
        .app-toast.is-visible { opacity: 1; transform: translateX(0); }
        .app-toast.is-hiding { opacity: 0; transform: translateX(20px); }
        .app-toast.toast-success { border-left-color: var(--ui-success, #167a5b); }
        .app-toast.toast-error { border-left-color: var(--ui-danger, #c81e34); }
        .app-toast.toast-warning { border-left-color: var(--ui-warning, #b66b00); }
        .app-toast-body { flex: 1; min-width: 0; line-height: 1.4; }
        .app-toast-close {
            flex-shrink: 0;
            background: none;
            border: none;
            cursor: pointer;
            color: var(--ui-text-muted, #5f6b7a);
            padding: 0 0 0 4px;
            line-height: 1;
            font-size: 14px;
        }
    </style>
    <div id="app-toast-region" aria-live="polite" aria-atomic="false"></div>
    <script>
    (function () {
        var ICONS = {
            success: '<svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>',
            error:   '<svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg>',
            warning: '<svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg>'
        };

        function dismissToast(toast) {
            clearTimeout(toast._t);
            toast.classList.remove('is-visible');
            toast.classList.add('is-hiding');
            setTimeout(function () { if (toast.parentNode) toast.parentNode.removeChild(toast); }, 220);
        }

        function showToast(message, type) {
            type = type || 'success';
            var region = document.getElementById('app-toast-region');
            if (!region || !message) return;

            var toast = document.createElement('div');
            toast.className = 'app-toast toast-' + type;
            toast.setAttribute('role', 'alert');
            var safeMsg = String(message).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
            toast.innerHTML =
                '<span class="app-toast-icon">' + (ICONS[type] || ICONS.success) + '</span>' +
                '<span class="app-toast-body">' + safeMsg + '</span>' +
                '<button type="button" class="app-toast-close" aria-label="Cerrar">&times;</button>';

            toast.querySelector('.app-toast-close').addEventListener('click', function () { dismissToast(toast); });
            region.appendChild(toast);

            requestAnimationFrame(function () {
                requestAnimationFrame(function () { toast.classList.add('is-visible'); });
            });

            toast._t = setTimeout(function () { dismissToast(toast); }, 4500);
        }

        window.showToast = showToast;

    })();
    </script>
    <script>
    window.__toastQueue = [];
    @if(session('success'))
    window.__toastQueue.push({ type: 'success', message: @json(session('success')) });
    @endif
    @if(session('error'))
    window.__toastQueue.push({ type: 'error', message: @json(session('error')) });
    @endif
    window.__toastQueue.forEach(function (item, i) {
        setTimeout(function () { if (window.showToast) window.showToast(item.message, item.type); }, i * 130);
    });
    </script>
    <div id="app-confirm-overlay" class="app-confirm-overlay" role="presentation">
        <div class="app-confirm-dialog" role="alertdialog" aria-modal="true" aria-labelledby="app-confirm-title" aria-describedby="app-confirm-message">
            <h2 class="app-confirm-title" id="app-confirm-title"></h2>
            <p class="app-confirm-message" id="app-confirm-message"></p>
            <p class="app-confirm-type-hint" id="app-confirm-type-hint" hidden></p>
            <input type="text" class="input app-confirm-type" id="app-confirm-type-input" autocomplete="off" hidden>
            <div class="app-confirm-actions">
                <button type="button" class="btn btn-ghost" id="app-confirm-cancel">Cancelar</button>
                <button type="button" class="btn btn-danger" id="app-confirm-accept">Confirmar</button>
            </div>
        </div>
    </div>
    <script>
    (function () {
        var overlay = document.getElementById('app-confirm-overlay');
        var dialog = overlay.querySelector('.app-confirm-dialog');
        var titleEl = document.getElementById('app-confirm-title');
        var messageEl = document.getElementById('app-confirm-message');
        var typeInput = document.getElementById('app-confirm-type-input');
        var typeHint  = document.getElementById('app-confirm-type-hint');
        var cancelBtn = document.getElementById('app-confirm-cancel');
        var acceptBtn = document.getElementById('app-confirm-accept');
        var activeResolve = null;
        var lastFocused = null;

        function close(result) {
            overlay.classList.remove('is-open');
            document.removeEventListener('keydown', onKeydown);
            if (lastFocused) lastFocused.focus();
            var resolve = activeResolve;
            activeResolve = null;
            if (resolve) resolve(result);
        }

        function onKeydown(e) {
            if (e.key === 'Escape') {
                e.preventDefault();
                close(false);
                return;
            }
            if (e.key === 'Tab') {
                var focusable = [cancelBtn, acceptBtn];
                var idx = focusable.indexOf(document.activeElement);
                if (e.shiftKey && idx <= 0) {
                    e.preventDefault();
                    focusable[focusable.length - 1].focus();
                } else if (!e.shiftKey && idx === focusable.length - 1) {
                    e.preventDefault();
                    focusable[0].focus();
                }
            }
        }

        function syncTypeGate() {
            if (typeInput.hidden) return;
            acceptBtn.disabled = typeInput.value.trim() !== typeInput.dataset.expected;
        }

        typeInput.addEventListener('input', syncTypeGate);
        overlay.addEventListener('click', function (e) {
            if (e.target === overlay) close(false);
        });
        cancelBtn.addEventListener('click', function () { close(false); });
        acceptBtn.addEventListener('click', function () { close(true); });

        window.confirmDialog = function (message, opts) {
            opts = opts || {};
            return new Promise(function (resolve) {
                activeResolve = resolve;
                lastFocused = document.activeElement;
                titleEl.textContent = opts.title || 'Confirmar accion';
                messageEl.textContent = message || '';
                acceptBtn.textContent = opts.confirmLabel || 'Confirmar';
                dialog.classList.toggle('is-danger', !!opts.danger);
                acceptBtn.classList.toggle('btn-danger', !!opts.danger);
                acceptBtn.classList.toggle('btn-primary', !opts.danger);

                if (opts.typeToConfirm) {
                    typeHint.textContent = 'Escribe «' + opts.typeToConfirm + '» para confirmar:';
                    typeHint.hidden = false;
                    typeInput.placeholder = opts.typeToConfirm;
                    typeInput.hidden = false;
                    typeInput.value = '';
                    typeInput.dataset.expected = opts.typeToConfirm;
                    acceptBtn.disabled = true;
                } else {
                    typeHint.hidden = true;
                    typeInput.hidden = true;
                    acceptBtn.disabled = false;
                }

                overlay.classList.add('is-open');
                document.addEventListener('keydown', onKeydown);
                (opts.typeToConfirm ? typeInput : cancelBtn).focus();
            });
        };

        document.addEventListener('submit', function (e) {
            var form = e.target;
            if (!(form instanceof HTMLFormElement) || !form.dataset.confirmMessage) return;
            if (form.dataset.confirming === '1') {
                delete form.dataset.confirming;
                return;
            }
            e.preventDefault();
            window.confirmDialog(form.dataset.confirmMessage, {
                title: form.dataset.confirmTitle,
                confirmLabel: form.dataset.confirmLabel,
                danger: form.dataset.confirmDanger === '1',
                typeToConfirm: form.dataset.confirmType || null,
            }).then(function (ok) {
                if (!ok) return;
                form.dataset.confirming = '1';
                if (typeof form.requestSubmit === 'function') form.requestSubmit();
                else form.submit();
            });
        });
    })();
    </script>
</body>
</html>
