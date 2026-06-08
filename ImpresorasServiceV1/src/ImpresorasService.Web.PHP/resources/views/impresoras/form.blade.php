@extends('layouts.app')

@section('title', $printer ? 'Editar impresora' : 'Nueva impresora')

@section('content')
<x-ui.card class="max-w-2xl">
<form method="POST" action="{{ $printer ? route('impresoras.update', $printer['printerId'] ?? $printer['PrinterId'] ?? 0) : route('impresoras.store') }}" class="dbx-form-grid">
    @csrf
    @if($printer) @method('PUT') @endif

    <div>
        <label for="printerName" class="form-label">Nombre *</label>
        <input type="text" name="printerName" id="printerName" value="{{ old('printerName', $printer ? ($printer['printerName'] ?? $printer['PrinterName'] ?? '') : '') }}" required
            class="input @error('printerName') border-red-500 @enderror">
        @error('printerName')<p class="field-error">{{ $message }}</p>@enderror
    </div>
    <div>
        <label for="spoolQueue" class="form-label">SpoolQueue *</label>
        <input type="text" name="spoolQueue" id="spoolQueue" value="{{ old('spoolQueue', $printer ? ($printer['spoolQueue'] ?? $printer['SpoolQueue'] ?? '') : '') }}" required
            class="input @error('spoolQueue') border-red-500 @enderror">
        @error('spoolQueue')<p class="field-error">{{ $message }}</p>@enderror
    </div>
    <div>
        <label for="host" class="form-label">Host (opcional)</label>
        <input type="text" name="host" id="host" value="{{ old('host', $printer ? ($printer['host'] ?? $printer['Host'] ?? '') : '') }}"
            class="input @error('host') border-red-500 @enderror">
        @error('host')<p class="field-error">{{ $message }}</p>@enderror
        <p class="form-hint">Usado para comprobar conectividad SMB cuando el `SpoolQueue` no es UNC (ej. `\\\\server\\share`).</p>
    </div>
    <div>
        <label for="storeId" class="form-label">Tienda (StoreId) *</label>
        @if($storeIdLocked ?? false)
        <input type="hidden" name="storeId" value="{{ $effectiveStoreId ?? 101 }}">
        <input type="number" id="storeId" value="{{ $effectiveStoreId ?? 101 }}" disabled min="1" class="input opacity-70 cursor-not-allowed">
        <p class="form-hint">Fijado a tu tienda (Jefe de tienda)</p>
        @else
        @php $selectedStoreId = old('storeId', $printer ? ($printer['storeId'] ?? $printer['StoreId'] ?? '') : ''); @endphp
        <select name="storeId" id="storeId" required class="select @error('storeId') border-red-500 @enderror">
            <option value="">Seleccionar tienda...</option>
            @foreach($storeOptions ?? [] as $store)
                <option value="{{ $store['storeId'] }}" {{ (string)$selectedStoreId === (string)$store['storeId'] ? 'selected' : '' }}>
                    {{ \App\Helpers\StoreFormat::label($store['storeId'], $store['name']) }}
                </option>
            @endforeach
        </select>
        @error('storeId')<p class="field-error">{{ $message }}</p>@enderror
        @endif
    </div>
    <div>
        <label class="flex items-center gap-2">
            <input type="hidden" name="isActive" value="0">
            <input type="checkbox" name="isActive" value="1" {{ old('isActive', $printer ? ($printer['isActive'] ?? $printer['IsActive'] ?? true) : true) ? 'checked' : '' }}>
            <span class="text-sm">Activa</span>
        </label>
    </div>
    <div class="form-actions">
        <button type="submit" class="btn btn-primary">{{ $printer ? 'Guardar' : 'Crear' }}</button>
        <a href="{{ route('impresoras.index') }}" class="btn btn-ghost">Cancelar</a>
    </div>
</form>
</x-ui.card>
@endsection
