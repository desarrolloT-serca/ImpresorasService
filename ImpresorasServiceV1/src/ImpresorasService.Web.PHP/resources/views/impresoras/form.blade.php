@extends('layouts.app')

@section('title', $printer ? 'Editar impresora' : 'Nueva impresora')

@section('content')
@php
    $storeNameById = collect($storeOptions ?? [])->mapWithKeys(fn ($store) => [(string) ($store['storeId'] ?? '') => $store['name'] ?? null]);
    $printerStoreId = $printer ? ($printer['storeId'] ?? $printer['StoreId'] ?? null) : null;
    $fallbackStoreId = ($storeOptions ?? []) ? (($storeOptions[0]['storeId'] ?? null) ?: null) : null;
    $currentStoreId = old('storeId', $selectedStoreId ?? $printerStoreId ?? $fallbackStoreId ?? '');
    $currentStoreName = (string) $currentStoreId !== ''
        ? ($storeNameById[(string) $currentStoreId] ?? null)
        : null;
    $currentStoreLabel = (string) $currentStoreId !== ''
        ? \App\Helpers\StoreFormat::label($currentStoreId, $currentStoreName)
        : null;
    $cancelUrl = (string) $currentStoreId !== ''
        ? route('impresoras.index', ['storeId' => $currentStoreId])
        : route('impresoras.index');
    $formAction = $printer
        ? route('impresoras.update', $printer['printerId'] ?? $printer['PrinterId'] ?? 0)
        : route('impresoras.store');
@endphp

<div class="dbx-wrap">
<x-ui.card class="dbx-rule-form-card">
    <form method="POST" action="{{ $formAction }}" class="dbx-rule-form">
        @csrf
        @if($printer) @method('PUT') @endif

        <x-ui.form-header
            kicker="Impresoras"
            :title="$printer ? 'Editar impresora' : 'Nueva impresora'"
            subtitle="Configura destino, cola y conectividad."
        >
            @if($currentStoreLabel)
                <span class="badge badge-neutral">Tienda: {{ $currentStoreLabel }}</span>
            @endif
        </x-ui.form-header>

        <x-ui.form-section title="Identificación" hint="Nombre operativo y tienda asociada.">
                <div>
                    <label for="printerName" class="form-label">Nombre *</label>
                    <input type="text" name="printerName" id="printerName" value="{{ old('printerName', $printer ? ($printer['printerName'] ?? $printer['PrinterName'] ?? '') : '') }}" required
                        class="input @error('printerName') border-red-500 @enderror">
                    @error('printerName')<p class="field-error">{{ $message }}</p>@enderror
                </div>

                <div>
                    <label for="storeId" class="form-label">Tienda *</label>
                    @if($storeIdLocked ?? false)
                        <input type="hidden" id="storeId" name="storeId" value="{{ $effectiveStoreId ?? $currentStoreId }}">
                        <input type="text" id="storeIdDisplay" value="{{ \App\Helpers\StoreFormat::label($effectiveStoreId ?? $currentStoreId, $storeNameById[(string) ($effectiveStoreId ?? $currentStoreId ?? '')] ?? null) }}" disabled class="input opacity-70 cursor-not-allowed">
                        <p class="form-hint">Fijado a tu tienda.</p>
                    @else
                        <select name="storeId" id="storeId" required class="select @error('storeId') border-red-500 @enderror">
                            <option value="">Seleccionar tienda...</option>
                            @foreach($storeOptions ?? [] as $store)
                                <option value="{{ $store['storeId'] }}" {{ (string) $currentStoreId === (string) $store['storeId'] ? 'selected' : '' }}>
                                    {{ \App\Helpers\StoreFormat::label($store['storeId'], $store['name']) }}
                                </option>
                            @endforeach
                        </select>
                        @error('storeId')<p class="field-error">{{ $message }}</p>@enderror
                    @endif
                </div>
        </x-ui.form-section>

        <x-ui.form-section title="Configuración de cola" hint="Ruta de spool y host para la comprobación.">
                <div>
                    <label for="spoolQueue" class="form-label">SpoolQueue *</label>
                    <input type="text" name="spoolQueue" id="spoolQueue" value="{{ old('spoolQueue', $printer ? ($printer['spoolQueue'] ?? $printer['SpoolQueue'] ?? '') : '') }}" required
                        class="input @error('spoolQueue') border-red-500 @enderror">
                    @error('spoolQueue')<p class="field-error">{{ $message }}</p>@enderror
                </div>

                <div>
                    <label for="host" class="form-label">Host</label>
                    <input type="text" name="host" id="host" value="{{ old('host', $printer ? ($printer['host'] ?? $printer['Host'] ?? '') : '') }}"
                        class="input @error('host') border-red-500 @enderror">
                    <p class="form-hint">Usado para comprobar conectividad SMB cuando SpoolQueue no es UNC.</p>
                    @error('host')<p class="field-error">{{ $message }}</p>@enderror
                </div>
        </x-ui.form-section>

        <x-ui.form-section title="Estado" hint="Disponibilidad de la impresora en la operativa.">
                <x-ui.toggle-field
                    name="isActive"
                    label="Activa"
                    :checked="old('isActive', $printer ? ($printer['isActive'] ?? $printer['IsActive'] ?? true) : true)"
                />
        </x-ui.form-section>

        <div class="dbx-rule-form-actions">
            <a href="{{ $cancelUrl }}" class="btn btn-ghost">Cancelar</a>
            <button type="submit" class="btn btn-primary">{{ $printer ? 'Guardar' : 'Crear' }}</button>
        </div>
    </form>
</x-ui.card>
</div>
@endsection
