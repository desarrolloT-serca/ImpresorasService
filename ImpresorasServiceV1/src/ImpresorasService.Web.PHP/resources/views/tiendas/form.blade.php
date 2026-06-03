@extends('layouts.app')

@php
    $editing = !is_null($store ?? null);
    $storeId = old('storeId', $store['storeId'] ?? $store['StoreId'] ?? '');
    $name = old('name', $store['name'] ?? $store['Name'] ?? '');
    $isActiveValue = old('isActive', ($store['isActive'] ?? $store['IsActive'] ?? true) ? '1' : '0');
@endphp

@section('title', $editing ? 'Editar tienda' : 'Nueva tienda')

@section('content')
<x-ui.card class="max-w-2xl">
    <form method="POST" action="{{ $editing ? route('tiendas.update', $storeId) : route('tiendas.store') }}" class="dbx-form-grid">
        @csrf
        @if($editing)
            @method('PUT')
        @endif

        <div>
            <label class="form-label">Numero de tienda</label>
            <input type="number" name="storeId" value="{{ $storeId }}" class="input @error('storeId') border-red-500 @enderror" {{ $editing ? 'readonly' : '' }} required>
            @error('storeId')<p class="field-error">{{ $message }}</p>@enderror
        </div>

        <div>
            <label class="form-label">Nombre</label>
            <input type="text" name="name" value="{{ $name }}" class="input @error('name') border-red-500 @enderror" required maxlength="120">
            @error('name')<p class="field-error">{{ $message }}</p>@enderror
        </div>

        <div>
            <label class="form-label">Activa</label>
            <select name="isActive" class="select @error('isActive') border-red-500 @enderror">
                <option value="1" {{ $isActiveValue === '1' ? 'selected' : '' }}>Si</option>
                <option value="0" {{ $isActiveValue === '0' ? 'selected' : '' }}>No</option>
            </select>
            @error('isActive')<p class="field-error">{{ $message }}</p>@enderror
        </div>

        <div class="form-actions">
            <a href="{{ route('tiendas.index') }}" class="btn btn-ghost">Cancelar</a>
            <button type="submit" class="btn btn-primary">{{ $editing ? 'Guardar cambios' : 'Crear tienda' }}</button>
        </div>
    </form>
</x-ui.card>
@endsection
