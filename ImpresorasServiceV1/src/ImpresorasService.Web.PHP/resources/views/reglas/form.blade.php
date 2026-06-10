@extends('layouts.app')

@section('title', $rule ? 'Editar regla' : 'Nueva regla')

@section('content')
@php
    $storeNameById = collect($storeOptions ?? [])->mapWithKeys(fn ($store) => [(string) ($store['storeId'] ?? '') => $store['name'] ?? null]);
    $ruleStoreId = $rule ? ($rule['storeId'] ?? $rule['StoreId'] ?? null) : null;
    $currentStoreId = old('storeId', $selectedStoreId ?? $ruleStoreId ?? '');
    $currentStoreName = (string) $currentStoreId !== ''
        ? ($storeNameById[(string) $currentStoreId] ?? null)
        : null;
    $currentStoreLabel = (string) $currentStoreId !== ''
        ? \App\Helpers\StoreFormat::label($currentStoreId, $currentStoreName)
        : 'Todas las tiendas';
    $cancelUrl = (string) $currentStoreId !== ''
        ? route('reglas.index', ['storeId' => $currentStoreId])
        : route('reglas.index');
    $selectedPrinterId = old('printerId', $rule ? ($rule['printerId'] ?? $rule['PrinterId'] ?? '') : '');
@endphp

<div class="dbx-wrap">
<x-ui.card class="dbx-rule-form-card">
    <form method="POST" action="{{ $rule ? route('reglas.update', $rule['ruleId'] ?? $rule['RuleId'] ?? 0) : route('reglas.store') }}" class="dbx-rule-form">
        @csrf
        @if($rule) @method('PUT') @endif

        <div class="dbx-rule-form-header">
            <div>
                <span class="dbx-rule-form-kicker">Reglas de enrutado</span>
                <h2 class="dbx-title">{{ $rule ? 'Editar regla' : 'Nueva regla' }}</h2>
                <p class="dbx-subtle">Define la prioridad, condiciones y destino de impresi&oacute;n.</p>
            </div>
            @if((string) $currentStoreId !== '')
                <span class="badge badge-neutral">Tienda: {{ $currentStoreLabel }}</span>
            @endif
        </div>

        <section class="dbx-rule-form-section">
            <div class="dbx-rule-form-section-title">
                <h3>Destino</h3>
                <span>Donde aplica la regla y a qu&eacute; impresora env&iacute;a.</span>
            </div>
            <div class="dbx-rule-form-grid">
                <div>
                    <label for="storeId" class="form-label">Tienda</label>
                    @if($storeIdLocked ?? false)
                        <input type="hidden" id="storeId" name="storeId" value="{{ $effectiveStoreId ?? '' }}">
                        <input type="text" id="storeIdDisplay" value="{{ \App\Helpers\StoreFormat::label($effectiveStoreId ?? null, $storeNameById[(string) ($effectiveStoreId ?? '')] ?? null) }}" disabled class="input opacity-70 cursor-not-allowed">
                        <p class="form-hint">Fijado a tu tienda.</p>
                    @else
                        <select name="storeId" id="storeId" class="select">
                            <option value="">Todas las tiendas</option>
                            @foreach($storeOptions ?? [] as $store)
                                <option value="{{ $store['storeId'] }}" {{ (string) $currentStoreId === (string) $store['storeId'] ? 'selected' : '' }}>
                                    {{ \App\Helpers\StoreFormat::label($store['storeId'], $store['name']) }}
                                </option>
                            @endforeach
                        </select>
                        @error('storeId')<p class="field-error">{{ $message }}</p>@enderror
                    @endif
                </div>

                <div>
                    <label for="printerId" class="form-label">Impresora *</label>
                    <select name="printerId" id="printerId" required class="select">
                        <option value="">Seleccionar impresora...</option>
                        @foreach($printers as $p)
                            @php
                                $pid = $p['printerId'] ?? $p['PrinterId'] ?? null;
                                $pname = $p['printerName'] ?? $p['PrinterName'] ?? ('Impresora ' . $pid);
                                $spool = trim((string) ($p['spoolQueue'] ?? $p['SpoolQueue'] ?? ''));
                                $printerLabel = $spool !== '' && $spool !== '-' ? ($pname . ' - ' . $spool) : $pname;
                            @endphp
                            @if($pid)
                                <option value="{{ $pid }}" {{ (string) $selectedPrinterId === (string) $pid ? 'selected' : '' }}>{{ $printerLabel }}</option>
                            @endif
                        @endforeach
                    </select>
                    @error('printerId')<p class="field-error">{{ $message }}</p>@enderror
                </div>
            </div>
        </section>

        <section class="dbx-rule-form-section">
            <div class="dbx-rule-form-section-title">
                <h3>Condiciones</h3>
                <span>Filtros que determinan cuando se aplica.</span>
            </div>
            <div class="dbx-rule-form-grid">
                <div>
                    <label for="documentType" class="form-label">Tipo documento</label>
                    <input type="text" name="documentType" id="documentType" value="{{ old('documentType', $rule ? ($rule['documentType'] ?? $rule['DocumentType'] ?? '') : '') }}"
                        class="input @error('documentType') border-red-500 @enderror" placeholder="Ej. ALBARAN, TICKET, FACTURA">
                    <p class="form-hint">Vac&iacute;o = aplica a cualquier tipo.</p>
                    @error('documentType')<p class="field-error">{{ $message }}</p>@enderror
                </div>
                <div>
                    <label for="channel" class="form-label">Canal</label>
                    <input type="text" name="channel" id="channel" value="{{ old('channel', $rule ? ($rule['channel'] ?? $rule['Channel'] ?? '') : '') }}"
                        class="input @error('channel') border-red-500 @enderror" placeholder="Ej. DEFAULT, MOSTRADOR, WEB">
                    <p class="form-hint">Vac&iacute;o = aplica a cualquier canal.</p>
                    @error('channel')<p class="field-error">{{ $message }}</p>@enderror
                </div>
            </div>
        </section>

        <section class="dbx-rule-form-section">
            <div class="dbx-rule-form-section-title">
                <h3>Ejecuci&oacute;n</h3>
                <span>Orden de evaluaci&oacute;n y estado operativo.</span>
            </div>
            <div class="dbx-rule-form-grid">
                <div>
                    <label for="priority" class="form-label">Prioridad *</label>
                    <input type="number" name="priority" id="priority" value="{{ old('priority', $rule ? ($rule['priority'] ?? $rule['Priority'] ?? 0) : 0) }}" required min="0"
                        class="input @error('priority') border-red-500 @enderror">
                    <p class="form-hint">Menor n&uacute;mero = mayor prioridad.</p>
                    @error('priority')<p class="field-error">{{ $message }}</p>@enderror
                </div>
                <div class="dbx-rule-active-field">
                    <label class="dbx-toggle">
                        <input type="hidden" name="isActive" value="0">
                        <input type="checkbox" name="isActive" value="1" {{ old('isActive', $rule ? ($rule['isActive'] ?? $rule['IsActive'] ?? true) : true) ? 'checked' : '' }}>
                        Activa
                    </label>
                    @error('isActive')<p class="field-error">{{ $message }}</p>@enderror
                </div>
            </div>
        </section>

        <div class="dbx-rule-form-actions">
            <a href="{{ $cancelUrl }}" class="btn btn-ghost">Cancelar</a>
            <button type="submit" class="btn btn-primary">{{ $rule ? 'Guardar' : 'Crear' }}</button>
        </div>
    </form>
</x-ui.card>
</div>

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

    function printerLabel(printer) {
        const pid = printer.printerId ?? printer.PrinterId;
        const name = printer.printerName ?? printer.PrinterName ?? ('Impresora ' + pid);
        const spool = String(printer.spoolQueue ?? printer.SpoolQueue ?? '').trim();
        return spool && spool !== '-' ? (name + ' - ' + spool) : name;
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

            printerIdEl.innerHTML = '<option value="">Seleccionar impresora...</option>';
            printers.forEach(p => {
                const pid = p.printerId ?? p.PrinterId;
                if (!pid) return;
                const opt = document.createElement('option');
                opt.value = pid;
                opt.textContent = printerLabel(p);
                printerIdEl.appendChild(opt);
            });

            const stillValid = currentPrinterId
                && Array.from(printerIdEl.options).some(o => String(o.value) === String(currentPrinterId));

            printerIdEl.value = stillValid ? currentPrinterId : '';
        } catch (e) {
            // No rompemos el formulario: el guardado server-side valida impresora-tienda.
        } finally {
            printerIdEl.disabled = false;
        }
    }

    loadPrintersForCurrentStore();

    storeIdEl.addEventListener('change', () => {
        loadPrintersForCurrentStore();
    });
})();
</script>

@endsection
