@extends('layouts.app')

@section('title', 'Telegram — Configuración')

@section('content')
@php
    $cfg = $config ?? [];
    $enabled = filter_var($cfg['enabled'] ?? $cfg['Enabled'] ?? true, FILTER_VALIDATE_BOOLEAN);
    $minSeverity = $cfg['minSeverity'] ?? $cfg['MinSeverity'] ?? 'critical';
    $notifyOnRecovery = filter_var($cfg['notifyOnRecovery'] ?? $cfg['NotifyOnRecovery'] ?? true, FILTER_VALIDATE_BOOLEAN);
    $interval = (int) ($cfg['checkIntervalMinutes'] ?? $cfg['CheckIntervalMinutes'] ?? 5);
    $chats = $chats ?? [];
    $storeOptions = collect($stores ?? [])->sortBy('storeId');
    $storeNameById = $storeOptions->mapWithKeys(fn ($s) => [(string)($s['storeId'] ?? '') => $s['name'] ?? '']);
@endphp

<div class="dbx-wrap">

    @if(session('success'))
        <div class="alert alert-success mt-3" role="alert">{{ session('success') }}</div>
    @endif
    @if(session('apiError'))
        <div class="alert alert-error mt-3" role="alert">{{ session('apiError') }}</div>
    @endif

    {{-- Bloque configuración --}}
    <x-ui.card>
        <div class="dbx-card-body">
            <div class="dbx-title-row">
                <h2 class="dbx-title">Notificaciones Telegram</h2>
                <span class="dbx-subtle">El bot token se gestiona como variable de entorno <code>Telegram__BotToken</code> en el servidor.</span>
            </div>

            <form method="POST" action="{{ route('alertas.config.save') }}" class="dbx-form mt-4">
                @csrf
                <div class="dbx-form-grid">
                    <div class="dbx-form-group">
                        <span class="dbx-filter-label">Estado del servicio</span>
                        <span class="badge {{ $enabled ? 'badge-success' : 'badge-neutral' }}">
                            {{ $enabled ? 'Alertas activas' : 'Alertas desactivadas' }}
                        </span>
                    </div>

                    <div class="dbx-form-group">
                        <label for="minSeverity" class="dbx-filter-label">Severidad mínima para alertar</label>
                        <select id="minSeverity" name="minSeverity" class="input">
                            <option value="warning" {{ $minSeverity === 'warning' ? 'selected' : '' }}>Warning (y crítica)</option>
                            <option value="critical" {{ $minSeverity === 'critical' ? 'selected' : '' }}>Solo Crítica</option>
                        </select>
                    </div>

                    <div class="dbx-form-group">
                        <label for="checkIntervalMinutes" class="dbx-filter-label">Intervalo de comprobación (min)</label>
                        <input id="checkIntervalMinutes" type="number" name="checkIntervalMinutes"
                            class="input" min="1" max="60" value="{{ $interval }}">
                    </div>

                    <div class="dbx-form-group flex items-center gap-2 pt-5">
                        <input id="notifyOnRecovery" type="checkbox" name="notifyOnRecovery" value="1"
                            {{ $notifyOnRecovery ? 'checked' : '' }}>
                        <label for="notifyOnRecovery" class="dbx-filter-label m-0">Notificar recuperación</label>
                    </div>
                </div>

                <div class="dbx-form-actions mt-4">
                    <button type="submit" class="btn btn-primary">Guardar configuración</button>
                </div>
            </form>
        </div>
    </x-ui.card>

    {{-- Bloque chats --}}
    <x-ui.card class="mt-4">
        <div class="dbx-card-body">
            <div class="dbx-title-row">
                <h2 class="dbx-title">Chats registrados</h2>
                <span class="dbx-subtle">Solo los chats activos reciben notificaciones.</span>
            </div>

            <x-ui.table class="mt-3">
                <caption class="sr-only">Chats de Telegram registrados</caption>
                <thead>
                    <tr>
                        <th>Chat ID</th>
                        <th>Descripción</th>
                        <th>Tienda</th>
                        <th class="status-col">Estado</th>
                        <th class="date-col">Alta</th>
                        <th class="actions-col">Acciones</th>
                    </tr>
                </thead>
                <tbody>
                    @forelse($chats as $chat)
                    @php
                        $chatId    = $chat['chatId'] ?? $chat['ChatId'] ?? '';
                        $desc      = $chat['description'] ?? $chat['Description'] ?? '-';
                        $active    = filter_var($chat['isActive'] ?? $chat['IsActive'] ?? true, FILTER_VALIDATE_BOOLEAN);
                        $chatStore = $chat['storeId'] ?? $chat['StoreId'] ?? null;
                        $createdAt = $chat['createdAtUtc'] ?? $chat['CreatedAtUtc'] ?? null;
                    @endphp
                    <tr>
                        <td><code>{{ $chatId }}</code></td>
                        <td>{{ $desc }}</td>
                        <td>
                            @if($chatStore)
                                <span class="badge badge-neutral">{{ $storeNameById[(string)$chatStore] ?? "Tienda $chatStore" }}</span>
                            @else
                                <span class="dbx-subtle">Todas</span>
                            @endif
                        </td>
                        <td class="status-col">
                            @if($active)
                                <span class="badge badge-success">Activo</span>
                            @else
                                <span class="badge badge-neutral">Inactivo</span>
                            @endif
                        </td>
                        <td class="date-col">{{ $createdAt ? \App\Helpers\DateTimeFormat::localDateTime($createdAt) : '-' }}</td>
                        <td class="actions-col">
                            <form method="POST" action="{{ route('alertas.chats.delete', ['chatId' => $chatId]) }}"
                                onsubmit="return confirm('¿Eliminar el chat {{ $chatId }}?')">
                                @csrf
                                <button type="submit" class="btn btn-danger btn-sm">Eliminar</button>
                            </form>
                        </td>
                    </tr>
                    @empty
                    <x-ui.empty-row colspan="6" message="No hay chats registrados." />
                    @endforelse
                </tbody>
            </x-ui.table>

            {{-- Añadir chat --}}
            <div class="dbx-title-row mt-6">
                <h3 class="dbx-title">Añadir chat</h3>
                <span class="dbx-subtle">Usa <code>/start</code> en el bot para obtener el Chat ID.</span>
            </div>
            <form method="POST" action="{{ route('alertas.chats.add') }}" class="dbx-form mt-2">
                @csrf
                <div class="dbx-form-grid">
                    <div class="dbx-form-group">
                        <label for="chatId" class="dbx-filter-label">Chat ID</label>
                        <input id="chatId" type="number" name="chatId" class="input" placeholder="-100123456789" required>
                    </div>
                    <div class="dbx-form-group">
                        <label for="description" class="dbx-filter-label">Descripción (opcional)</label>
                        <input id="description" type="text" name="description" class="input" placeholder="Grupo alertas tienda">
                    </div>
                    <div class="dbx-form-group">
                        <label for="storeId" class="dbx-filter-label">Tienda (vacío = todas)</label>
                        <select id="storeId" name="storeId" class="input">
                            <option value="">Todas las tiendas</option>
                            @foreach($storeOptions as $store)
                                <option value="{{ $store['storeId'] ?? '' }}">
                                    {{ $store['name'] ?? '' }} (#{{ $store['storeId'] ?? '' }})
                                </option>
                            @endforeach
                        </select>
                    </div>
                </div>
                <div class="dbx-form-actions mt-3">
                    <button type="submit" class="btn btn-primary">Añadir chat</button>
                </div>
            </form>
        </div>
    </x-ui.card>

    {{-- Enviar prueba --}}
    <x-ui.card class="mt-4">
        <div class="dbx-card-body">
            <div class="dbx-title-row">
                <h2 class="dbx-title">Prueba de envío</h2>
                <span class="dbx-subtle">Envía un mensaje de prueba a todos los chats activos.</span>
            </div>
            <form method="POST" action="{{ route('alertas.test') }}" class="mt-3">
                @csrf
                <button type="submit" class="btn btn-ghost"
                    onclick="return confirm('¿Enviar mensaje de prueba a todos los chats activos?')">
                    Enviar prueba
                </button>
            </form>
        </div>
    </x-ui.card>

</div>
@endsection
