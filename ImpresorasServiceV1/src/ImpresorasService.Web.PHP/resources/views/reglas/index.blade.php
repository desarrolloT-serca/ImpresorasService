@extends('layouts.app')

@section('title', 'Reglas de enrutado')

@section('content')
@php
    $rulesByStore = is_array($rulesByStore ?? null) ? $rulesByStore : [];
    $selectedStoreGroup = is_array($selectedStoreGroup ?? null) ? $selectedStoreGroup : null;
    $selectedStoreId = $selectedStoreGroup['storeId'] ?? null;
    $selectedStoreKey = $selectedStoreId !== null ? (string) $selectedStoreId : 'global';
    $isActiveFilter = (string) ($isActiveFilter ?? request('isActive', ''));
    $hasAnyRules = (bool) ($hasAnyRules ?? false);
    $createUrl = $selectedStoreId !== null
        ? route('reglas.create', ['storeId' => $selectedStoreId])
        : route('reglas.create');
    $createLabel = $selectedStoreId !== null ? 'Crear regla para esta tienda' : 'Crear regla';
    $storeNameById = [];
    foreach ($rulesByStore as $storeGroup) {
        $sid = $storeGroup['storeId'] ?? null;
        if ($sid !== null) {
            $storeNameById[(string) $sid] = $storeGroup['storeName'] ?? null;
        }
    }
@endphp
@if(session('success'))
    <div class="mb-4 alert alert-success">{{ session('success') }}</div>
@endif

<div class="dbx-wrap">
<section class="dbx-routing-layout">
    <x-ui.card class="dbx-routing-stores-card">
        <div class="dbx-title-row">
            <h2 class="dbx-title">Tiendas</h2>
            <span class="dbx-subtle">Selecciona una tienda</span>
        </div>

        @if(count($rulesByStore) === 0)
            <p class="dbx-subtle">No hay tiendas disponibles.</p>
        @else
            <div class="dbx-routing-store-list">
                @foreach($rulesByStore as $storeGroup)
                    @php
                        $storeId = $storeGroup['storeId'] ?? null;
                        $storeKey = $storeId !== null ? (string) $storeId : 'global';
                        $isSelected = $storeKey === $selectedStoreKey;
                        $storeUrlParams = ['isActive' => $isActiveFilter];
                        if ($storeId !== null) {
                            $storeUrlParams['storeId'] = $storeId;
                        }
                        $storeUrlParams = array_filter($storeUrlParams, static fn ($value) => $value !== null && $value !== '');
                        $hasActiveRules = (int) ($storeGroup['activeCount'] ?? 0) > 0;
                    @endphp
                    <a href="{{ route('reglas.index', $storeUrlParams) }}" class="dbx-routing-store-link {{ $isSelected ? 'is-active' : '' }} {{ $hasActiveRules ? 'has-active-rules' : 'is-empty-store' }}">
                        <span class="dbx-routing-store-name">{{ $storeGroup['formattedStoreName'] ?? 'Sin tienda' }}</span>
                        <span class="dbx-routing-store-meta">
                            {{ (int) ($storeGroup['rulesCount'] ?? 0) }} reglas
                            &middot;
                            {{ (int) ($storeGroup['activeCount'] ?? 0) }} activas
                        </span>
                    </a>
                @endforeach
            </div>
        @endif
    </x-ui.card>

    <x-ui.card class="dbx-routing-rules-card">
        <div class="dbx-title-row dbx-routing-title-row">
            <div>
                <h2 class="dbx-title">
                    @if($selectedStoreGroup)
                        Reglas de {{ $selectedStoreGroup['formattedStoreName'] ?? 'la tienda seleccionada' }}
                    @else
                        Reglas de enrutado
                    @endif
                </h2>
                <span class="dbx-subtle">Prioridad, condiciones y destino de impresi&oacute;n</span>
            </div>
            <a href="{{ $createUrl }}" class="btn btn-primary dbx-routing-create-btn" title="{{ $createLabel }}" aria-label="{{ $createLabel }}">+ Nueva regla</a>
        </div>

        @if(!$hasAnyRules)
            <div class="dbx-empty-state">No hay reglas de enrutado configuradas.</div>
        @elseif(!$selectedStoreGroup || count($rules) === 0)
            <div class="dbx-empty-state">Esta tienda no tiene reglas configuradas.</div>
        @else
            <x-ui.table class="dbx-actions-table dbx-routing-rules-table">
                <thead>
                    <tr>
                        <th class="number-col">Prioridad</th>
                        <th class="status-col">Tipo doc</th>
                        <th class="status-col">Canal</th>
                        <th class="text-col">Impresora</th>
                        <th class="status-col">Activa</th>
                        <th class="actions-col">Acciones</th>
                    </tr>
                </thead>
                <tbody>
                    @foreach($rules as $r)
                        @php
                            $id = $r['ruleId'] ?? $r['RuleId'] ?? null;
                            $isActive = (bool) ($r['isActive'] ?? $r['IsActive'] ?? false);
                        @endphp
                        <tr>
                            <td class="number-col">{{ $r['priority'] ?? $r['Priority'] ?? '-' }}</td>
                            <td class="status-col">{{ $r['documentType'] ?? $r['DocumentType'] ?? '-' }}</td>
                            <td class="status-col">{{ $r['channel'] ?? $r['Channel'] ?? '-' }}</td>
                            <td class="text-col">{{ ($r['printer'] ?? null) ? ($r['printer']['printerName'] ?? $r['printer']['PrinterName'] ?? $r['printerId'] ?? $r['PrinterId']) : ($r['printerId'] ?? $r['PrinterId'] ?? '-') }}</td>
                            <td class="status-col">
                                <span class="badge status-chip {{ $isActive ? 'badge-success' : 'badge-danger' }}" aria-label="{{ $isActive ? 'Regla activa' : 'Regla inactiva' }}">
                                    {{ $isActive ? 'Sí' : 'No' }}
                                </span>
                            </td>
                            <td class="actions-col">
                                @if($id)
                                <x-ui.action-buttons>
                                    <a href="{{ route('reglas.edit', $id) }}" class="btn btn-ghost">Editar</a>
                                    <form action="{{ route('reglas.destroy', $id) }}" method="POST" onsubmit="return confirm('&iquest;Eliminar?')">
                                        @csrf
                                        @method('DELETE')
                                        <button type="submit" class="btn btn-danger">Eliminar</button>
                                    </form>
                                </x-ui.action-buttons>
                                @endif
                            </td>
                        </tr>
                    @endforeach
                </tbody>
            </x-ui.table>
        @endif
    </x-ui.card>
</section>
</div>
@endsection
