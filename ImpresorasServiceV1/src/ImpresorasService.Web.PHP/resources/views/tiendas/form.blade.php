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

        <x-ui.field label="Numero de tienda" name="storeId" type="number" :value="$storeId" required :readonly="$editing" />

        <x-ui.field label="Nombre" name="name" :value="$name" required maxlength="120" autocomplete="off" />

        <x-ui.field label="Activa" name="isActive" type="select">
            <option value="1" {{ $isActiveValue === '1' ? 'selected' : '' }}>Si</option>
            <option value="0" {{ $isActiveValue === '0' ? 'selected' : '' }}>No</option>
        </x-ui.field>

        <div class="form-actions">
            <a href="{{ route('tiendas.index') }}" class="btn btn-ghost">Cancelar</a>
            <button type="submit" class="btn btn-primary">{{ $editing ? 'Guardar cambios' : 'Crear tienda' }}</button>
        </div>
    </form>
</x-ui.card>
@endsection
