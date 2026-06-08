@extends('layouts.app')

@section('title', $rule ? 'Editar regla' : 'Nueva regla')

@section('content')
@php
    $storeNameById = collect($storeOptions ?? [])->mapWithKeys(fn ($store) => [(string) ($store['storeId'] ?? '') => $store['name'] ?? null]);
@endphp
<x-ui.card class="max-w-2xl">
<form method="POST" action="{{ $rule ? route('reglas.update', $rule['ruleId'] ?? $rule['RuleId'] ?? 0) : route('reglas.store') }}" class="dbx-form-grid">
    @csrf
    @if($rule) @method('PUT') @endif

    <div>
        <label for="priority" class="form-label">Prioridad *</label>
        <input type="number" name="priority" id="priority" value="{{ old('priority', $rule ? ($rule['priority'] ?? $rule['Priority'] ?? 0) : 0) }}" required min="0"
            class="input @error('priority') border-red-500 @enderror">
        @error('priority')<p class="field-error">{{ $message }}</p>@enderror
    </div>
    <div>
        <label for="storeId" class="form-label">Tienda (opcional)</label>
        @if($storeIdLocked ?? false)
        <input type="hidden" name="storeId" value="{{ $effectiveStoreId ?? '' }}">
        <input type="text" id="storeId" value="{{ \App\Helpers\StoreFormat::label($effectiveStoreId ?? null, $storeNameById[(string) ($effectiveStoreId ?? '')] ?? null) }}" disabled class="input opacity-70 cursor-not-allowed">
        <p class="form-hint">Fijado a tu tienda (Jefe de tienda)</p>
        @else
        @php $selectedStoreId = old('storeId', $rule ? ($rule['storeId'] ?? $rule['StoreId'] ?? '') : ''); @endphp
        <select name="storeId" id="storeId" class="select">
            <option value="">Todas las tiendas</option>
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
        <label for="documentType" class="form-label">Tipo documento (opcional)</label>
        <input type="text" name="documentType" id="documentType" value="{{ old('documentType', $rule ? ($rule['documentType'] ?? $rule['DocumentType'] ?? '') : '') }}"
            class="input @error('documentType') border-red-500 @enderror" placeholder="TICKET, etc.">
        @error('documentType')<p class="field-error">{{ $message }}</p>@enderror
    </div>
    <div>
        <label for="channel" class="form-label">Canal (opcional)</label>
        <input type="text" name="channel" id="channel" value="{{ old('channel', $rule ? ($rule['channel'] ?? $rule['Channel'] ?? '') : '') }}"
            class="input @error('channel') border-red-500 @enderror" placeholder="DEFAULT, etc.">
        @error('channel')<p class="field-error">{{ $message }}</p>@enderror
    </div>
    <div>
        <label for="printerId" class="form-label">Impresora *</label>
        <select name="printerId" id="printerId" required class="select">
            <option value="">Seleccionar...</option>
            @foreach($printers as $p)
            @php $pid = $p['printerId'] ?? $p['PrinterId']; $pname = $p['printerName'] ?? $p['PrinterName']; @endphp
            <option value="{{ $pid }}" {{ old('printerId', $rule ? ($rule['printerId'] ?? $rule['PrinterId'] ?? '') : '') == $pid ? 'selected' : '' }}>{{ $pname }} ({{ $pid }})</option>
            @endforeach
        </select>
        @error('printerId')<p class="field-error">{{ $message }}</p>@enderror
    </div>
    <div>
        <label class="flex items-center gap-2">
            <input type="hidden" name="isActive" value="0">
            <input type="checkbox" name="isActive" value="1" {{ old('isActive', $rule ? ($rule['isActive'] ?? $rule['IsActive'] ?? true) : true) ? 'checked' : '' }}>
            <span class="text-sm">Activa</span>
        </label>
        @error('isActive')<p class="field-error">{{ $message }}</p>@enderror
    </div>
    <div class="form-actions">
        <button type="submit" class="btn btn-primary">{{ $rule ? 'Guardar' : 'Crear' }}</button>
        <a href="{{ route('reglas.index') }}" class="btn btn-ghost">Cancelar</a>
    </div>
</form>
</x-ui.card>

<script>
(function() {
    const csrf = document.querySelector('meta[name="csrf-token"]')?.content || '';

    const storeIdEl = document.getElementById('storeId');
    const printerIdEl = document.getElementById('printerId');
    if (!storeIdEl || !printerIdEl) return;

    const printersByStoreUrl = @json(route('reglas.printersByStore'));

    function coerceStoreId(raw) {
        if (raw === null || raw === undefined) return null;
        const s = String(raw).trim();
        if (s === '') return null;
        const n = Number(s);
        if (!Number.isFinite(n) || n < 1) return null;
        return n;
    }

    async function loadPrintersForCurrentStore() {
        const storeId = coerceStoreId(storeIdEl.value);
        const currentPrinterId = printerIdEl.value;

        let url = printersByStoreUrl;
        if (storeId !== null) {
            const u = new URL(printersByStoreUrl, window.location.origin);
            u.searchParams.set('storeId', String(storeId));
            url = u.toString();
        }

        printerIdEl.disabled = true;

        try {
            const res = await fetch(url, {
                method: 'GET',
                headers: {
                    'Accept': 'application/json',
                    'X-CSRF-TOKEN': csrf,
                    'X-Requested-With': 'XMLHttpRequest',
                },
                credentials: 'same-origin',
            });
            if (!res.ok) throw new Error('HTTP ' + res.status);

            const data = await res.json();
            const printers = Array.isArray(data?.printers) ? data.printers : [];

            printerIdEl.innerHTML = '<option value="">Seleccionar...</option>';
            printers.forEach(p => {
                const pid = p.printerId ?? p.PrinterId;
                const pname = p.printerName ?? p.PrinterName ?? ('Impresora ' + pid);
                if (!pid) return;
                const opt = document.createElement('option');
                opt.value = pid;
                opt.textContent = pname + ' (' + pid + ')';
                printerIdEl.appendChild(opt);
            });

            const stillValid = currentPrinterId
                && Array.from(printerIdEl.options).some(o => String(o.value) === String(currentPrinterId));

            printerIdEl.value = stillValid ? currentPrinterId : '';
        } catch (e) {
            // No rompemos el formulario: si falla, se mantiene el dropdown actual.
            // (El guardado server-side validará la pertenencia impresora-tienda.)
        } finally {
            printerIdEl.disabled = false;
        }
    }

    // Carga inicial por si el servidor devuelve all/printers, o por posibles re-renders.
    loadPrintersForCurrentStore();

    storeIdEl.addEventListener('change', () => {
        loadPrintersForCurrentStore();
    });
})();
</script>

@endsection
