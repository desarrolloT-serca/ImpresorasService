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
<div class="dbx-wrap">
@fragment('routing-layout')
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
                    <a href="{{ route('reglas.index', $storeUrlParams) }}"
                       data-dynamic-store-link
                       data-dynamic-target="reglas"
                       class="dbx-routing-store-link {{ $isSelected ? 'is-active' : '' }} {{ $hasActiveRules ? 'has-active-rules' : 'is-empty-store' }}">
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

    <x-ui.card class="dbx-routing-rules-card" data-dynamic-panel="reglas" aria-live="polite">
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
            @php
                $activePriorityCounts = [];
                foreach ($rules as $r) {
                    if ((bool) ($r['isActive'] ?? $r['IsActive'] ?? false)) {
                        $p = $r['priority'] ?? $r['Priority'] ?? null;
                        if ($p !== null) {
                            $activePriorityCounts[$p] = ($activePriorityCounts[$p] ?? 0) + 1;
                        }
                    }
                }
            @endphp
            <div class="dbx-rule-list">
                @foreach($rules as $r)
                    @php
                        $id = $r['ruleId'] ?? $r['RuleId'] ?? null;
                        $isActive = (bool) ($r['isActive'] ?? $r['IsActive'] ?? false);
                        $priority = $r['priority'] ?? $r['Priority'] ?? null;
                        $docType = $r['documentType'] ?? $r['DocumentType'] ?? null;
                        $channel = $r['channel'] ?? $r['Channel'] ?? null;
                        $printerName = ($r['printer'] ?? null)
                            ? ($r['printer']['printerName'] ?? $r['printer']['PrinterName'] ?? $r['printerId'] ?? $r['PrinterId'])
                            : ($r['printerId'] ?? $r['PrinterId'] ?? '-');
                        $docTypeIsWildcard = $docType === null || $docType === '' || $docType === '-';
                        $channelIsWildcard = $channel === null || $channel === '' || $channel === '-' || strtoupper((string) $channel) === 'DEFAULT';
                        $hasPriorityConflict = $isActive && $priority !== null && ($activePriorityCounts[$priority] ?? 0) > 1;
                        $docTypePhrase = $docTypeIsWildcard
                            ? '<span class="dbx-rule-wildcard">cualquier tipo de documento</span>'
                            : 'documentos tipo <strong>' . e($docType) . '</strong>';
                        $channelPhrase = $channelIsWildcard
                            ? '<span class="dbx-rule-wildcard">cualquier canal</span>'
                            : 'canal <strong>' . e($channel) . '</strong>';
                    @endphp
                    <div class="dbx-rule-sentence-row">
                        <p class="dbx-rule-sentence">
                            Si {!! $docTypePhrase !!} y {!! $channelPhrase !!}, enviar a <strong>{{ $printerName }}</strong>, prioridad <strong>{{ $priority ?? '-' }}</strong>
                            @if($hasPriorityConflict)
                                <span class="dbx-rule-conflict" title="Otra regla activa de esta tienda comparte la misma prioridad">Prioridad duplicada</span>
                            @endif
                        </p>
                        <div class="dbx-rule-sentence-meta">
                            <x-ui.status :level="$isActive ? 'healthy' : 'critical'" aria-label="{{ $isActive ? 'Regla activa' : 'Regla inactiva' }}">
                                {{ $isActive ? 'Activa' : 'Inactiva' }}
                            </x-ui.status>
                            @if($id)
                            <x-ui.action-buttons>
                                <a href="{{ route('reglas.edit', $id) }}" class="btn btn-ghost">Editar</a>
                                <x-ui.confirm-form
                                    :action="route('reglas.destroy', $id)"
                                    method="DELETE"
                                    title="Eliminar regla"
                                    message="Se eliminara la regla de prioridad {{ $priority ?? '-' }} ({{ $docType ?? '-' }} / {{ $channel ?? '-' }}). Esta accion no se puede deshacer."
                                    confirm-label="Eliminar"
                                    danger
                                >
                                    <x-slot:trigger>
                                        <button type="submit" class="btn btn-danger">Eliminar</button>
                                    </x-slot:trigger>
                                </x-ui.confirm-form>
                            </x-ui.action-buttons>
                            @endif
                        </div>
                    </div>
                @endforeach
            </div>
        @endif
    </x-ui.card>
</section>
@endfragment
</div>

<script>
(function() {
    let panelAbortController = null;
    let panelRequestId = 0;

    function getDynamicPanel() {
        return document.querySelector('[data-dynamic-panel="reglas"]');
    }

    function showPanelError(panel, url) {
        const previous = panel.querySelector('.dynamic-panel-error');
        if (previous) previous.remove();

        const error = document.createElement('div');
        error.className = 'dynamic-panel-error';
        error.innerHTML = 'No se pudo actualizar el panel. <a class="btn btn-ghost btn-sm" href="' + url + '">Abrir vista completa</a>';
        panel.prepend(error);
    }

    async function loadRulesPanel(url, pushState) {
        const currentPanel = getDynamicPanel();
        if (!currentPanel || !window.fetch || !window.DOMParser) {
            window.location.href = url;
            return;
        }

        if (panelAbortController) {
            panelAbortController.abort();
        }

        panelAbortController = new AbortController();
        const requestId = ++panelRequestId;
        currentPanel.classList.add('is-dynamic-loading');
        currentPanel.setAttribute('aria-busy', 'true');

        try {
            const response = await fetch(url, {
                headers: {
                    'Accept': 'text/html',
                    'X-Requested-With': 'XMLHttpRequest'
                },
                signal: panelAbortController.signal
            });

            if (!response.ok) {
                throw new Error('HTTP ' + response.status);
            }

            const html = await response.text();
            if (requestId !== panelRequestId) return;

            const doc = new DOMParser().parseFromString(html, 'text/html');
            const nextPanel = doc.querySelector('[data-dynamic-panel="reglas"]');
            const nextStores = doc.querySelector('.dbx-routing-store-list');
            const currentStores = document.querySelector('.dbx-routing-store-list');

            if (!nextPanel) {
                throw new Error('Panel no encontrado');
            }

            currentPanel.replaceWith(nextPanel);
            if (nextStores && currentStores) {
                currentStores.replaceWith(nextStores);
            }

            if (pushState) {
                history.pushState({ dynamicPanel: 'reglas' }, '', url);
            }
        } catch (error) {
            if (error.name === 'AbortError') return;
            const panel = getDynamicPanel();
            if (panel) {
                panel.classList.remove('is-dynamic-loading');
                panel.removeAttribute('aria-busy');
                showPanelError(panel, url);
            }
        }
    }

    document.addEventListener('click', function(event) {
        const link = event.target.closest('a[data-dynamic-store-link][data-dynamic-target="reglas"]');
        if (!link || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;

        event.preventDefault();
        loadRulesPanel(link.href, true);
    });

    window.addEventListener('popstate', function() {
        if (window.location.pathname.includes('/reglas')) {
            loadRulesPanel(window.location.href, false);
        }
    });
})();
</script>
@endsection
