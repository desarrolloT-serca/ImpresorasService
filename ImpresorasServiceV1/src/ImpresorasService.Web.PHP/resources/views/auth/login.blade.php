<!DOCTYPE html>
<html lang="{{ str_replace('_', '-', app()->getLocale()) }}">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title>Login - Impresoras Service</title>
    @if (file_exists(public_path('build/manifest.json')) || file_exists(public_path('hot')))
        @vite(['resources/css/app.css', 'resources/js/app.js'])
        @vite('resources/css/dbx.css')
        @vite('resources/css/system.css')
    @else
        <script src="https://cdn.tailwindcss.com"></script>
    @endif
    <script>
        (function() {
            var t = localStorage.getItem('theme');
            if (t === 'dark') {
                document.documentElement.setAttribute('data-theme', 'dark');
                document.documentElement.classList.add('dark');
            }
        })();
    </script>
</head>
<body class="login-page">
    <main class="login-shell">
        <section class="login-panel" aria-labelledby="login-title">
            <div class="login-identity">
                <div class="login-logo-frame">
                    @if(file_exists(public_path('img/serca-logo.png')))
                        <img src="{{ asset('img/serca-logo.png') }}" alt="AD Serca" class="login-logo">
                    @elseif(file_exists(public_path('img/serca-logo.svg')))
                        <img src="{{ asset('img/serca-logo.svg') }}" alt="AD Serca" class="login-logo">
                    @elseif(file_exists(public_path('img/ad-logo.svg')))
                        <img src="{{ asset('img/ad-logo.svg') }}" alt="AD Serca" class="login-logo">
                    @endif
                </div>
                <div class="login-brand-copy">
                    <span>Panel operativo</span>
                    <h1 id="login-title">Impresoras Service</h1>
                    <p>Supervisi&oacute;n de cola, alertas y estado de impresoras por tienda.</p>
                </div>
            </div>

            <div class="login-access">
                <div class="login-access-head">
                    <span class="login-access-kicker">Acceso seguro</span>
                    <h2>Iniciar sesi&oacute;n</h2>
                    <p>Introduce tus credenciales para continuar.</p>
                </div>

                @include('components.form-errors')

                <form method="POST" action="{{ route('login') }}" class="login-form">
                    @csrf
                    <div>
                        <label for="login" class="form-label">Usuario</label>
                        <input type="text" name="login" id="login" value="{{ old('login') }}" required autofocus autocomplete="username"
                            class="input @error('login') border-red-500 @enderror">
                        @error('login')
                            <p class="mt-1 text-sm text-red-600">{{ $message }}</p>
                        @enderror
                    </div>
                    <div>
                        <label for="password" class="form-label">Contrase&ntilde;a</label>
                        <input type="password" name="password" id="password" required autocomplete="current-password"
                            class="input @error('password') border-red-500 @enderror">
                        @error('password')
                            <p class="mt-1 text-sm text-red-600">{{ $message }}</p>
                        @enderror
                    </div>
                    <button type="submit" class="btn btn-primary w-full">Entrar al panel</button>
                </form>
            </div>
        </section>
    </main>
</body>
</html>
