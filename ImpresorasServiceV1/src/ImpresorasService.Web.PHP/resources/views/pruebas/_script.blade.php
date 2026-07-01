(function () {
    const csrf     = document.querySelector('meta[name="csrf-token"]')?.content || '';
    const TERMINAL = new Set(['SpoolAccepted', 'ErrorFinal', 'PrintedConfirmed', 'PrintedUnknown']);
    const CSS_MAP  = {
        SpoolAccepted: 'badge-success', PrintedConfirmed: 'badge-success',
        PrintedUnknown: 'badge-success', ErrorFinal: 'badge-danger',
        Pending: 'badge-warning', Routed: 'badge-warning',
        Printing: 'badge-warning', RetryScheduled: 'badge-warning',
    };

    const STORE_OPTIONS = @json(collect($stores)->map(function ($s) {
        $sid   = $s['storeId'] ?? $s['StoreId'] ?? 0;
        $sname = $s['name'] ?? $s['Name'] ?? '';
        return ['id' => $sid, 'name' => \App\Helpers\StoreFormat::label($sid, $sname)];
    })->values()->toArray());

    const DOC_TYPES = @json($docTypes);

    let activePdfTargetBtn = null;
    let pollTimer = null;

    // ── Helpers ──────────────────────────────────────────────────────────────

    async function apiFetch(url, opts = {}) {
        const headers = { 'X-CSRF-TOKEN': csrf, 'Accept': 'application/json' };
        if (opts.json !== undefined) {
            headers['Content-Type'] = 'application/json';
            opts.body = JSON.stringify(opts.json);
            delete opts.json;
        }
        const res = await fetch(url, { ...opts, headers: { ...headers, ...(opts.headers || {}) } });
        return { ok: res.ok, status: res.status, data: await res.json().catch(() => ({})) };
    }

    function buildStoreSelect(name, selectedId) {
        const opts = STORE_OPTIONS.map(s =>
            `<option value="${s.id}"${s.id == selectedId ? ' selected' : ''}>${s.name}</option>`
        ).join('');
        return `<select name="${name}" class="select select-sm" required><option value="">Tienda…</option>${opts}</select>`;
    }

    function buildDocDatalist() {
        if (document.getElementById('doc-types-list')) return;
        const dl = document.createElement('datalist');
        dl.id = 'doc-types-list';
        DOC_TYPES.forEach(t => { const o = document.createElement('option'); o.value = t; dl.appendChild(o); });
        document.body.appendChild(dl);
    }

    // ── Añadir lote ──────────────────────────────────────────────────────────

    document.getElementById('btn-add-lote')?.addEventListener('click', addLote);

    function addLote() {
        buildDocDatalist();
        const tbody = document.getElementById('lotes-tbody');
        if (!tbody) return;
        const idx = tbody.rows.length;
        const tr  = document.createElement('tr');
        tr.dataset.loteIdx = idx;
        tr.innerHTML = `
            <td>${buildStoreSelect(`batches[${idx}][storeId]`, '')}</td>
            <td><input type="text" name="batches[${idx}][documentType]" class="input input-sm"
                       list="doc-types-list" placeholder="ALBARAN" required maxlength="50"></td>
            <td><input type="text" name="batches[${idx}][channel]" class="input input-sm"
                       value="DEFAULT" maxlength="50" style="width:90px;"></td>
            <td><input type="number" name="batches[${idx}][count]" class="input input-sm"
                       value="1" min="1" max="50" required style="width:70px;"></td>
            <td>
                <button type="button" class="btn btn-ghost btn-sm btn-elegir-pdf" data-pdf-id="">(sin PDF)</button>
                <input type="hidden" name="batches[${idx}][pdfId]" value="">
            </td>
            <td><button type="button" class="btn btn-danger btn-sm btn-eliminar-lote">&#10005;</button></td>`;
        tbody.appendChild(tr);
    }

    // ── Eliminar lote ────────────────────────────────────────────────────────

    document.getElementById('lotes-tbody')?.addEventListener('click', e => {
        if (!e.target.closest('.btn-eliminar-lote')) return;
        e.target.closest('tr').remove();
        reindexLotes();
    });

    function reindexLotes() {
        document.querySelectorAll('#lotes-tbody tr').forEach((row, i) => {
            row.dataset.loteIdx = i;
            row.querySelectorAll('[name]').forEach(el => {
                el.name = el.name.replace(/batches\[\d+\]/, `batches[${i}]`);
            });
        });
    }

    // ── Guardar escenario ────────────────────────────────────────────────────

    document.getElementById('btn-guardar-escenario')?.addEventListener('click', async () => {
        const form = document.getElementById('form-escenario');
        if (!form) return;
        const id   = form.dataset.id || '';
        const name = document.getElementById('sc-nombre')?.value?.trim();
        if (!name) { window.showToast?.('Introduce un nombre para el escenario.', 'error'); return; }
        const batches = buildBatches();
        if (batches.length === 0) { window.showToast?.('A&ntilde;ade al menos un lote.', 'error'); return; }

        const payload = { name, batches };
        if (id) payload.id = id;

        const { ok, data } = await apiFetch('{{ route('pruebas.scenarios.save') }}', { json: payload, method: 'POST' });
        if (ok) {
            window.showToast?.('Escenario guardado.', 'success');
            setTimeout(() => { window.location.href = '{{ route('pruebas.index') }}?scenario=' + data.id; }, 600);
        } else {
            window.showToast?.(data?.message || 'Error al guardar.', 'error');
        }
    });

    function buildBatches() {
        return Array.from(document.querySelectorAll('#lotes-tbody tr')).map(row => {
            const g = name => row.querySelector(`[name$="[${name}]"]`)?.value?.trim() || '';
            return {
                storeId:      parseInt(g('storeId'), 10),
                documentType: g('documentType').toUpperCase(),
                channel:      g('channel').toUpperCase() || 'DEFAULT',
                count:        parseInt(g('count'), 10) || 1,
                pdfId:        g('pdfId') || null,
            };
        }).filter(b => b.storeId > 0 && b.documentType);
    }

    // ── Eliminar escenario ───────────────────────────────────────────────────

    document.getElementById('btn-eliminar-escenario')?.addEventListener('click', async e => {
        const id = e.currentTarget.dataset.id;
        if (!confirm('¿Eliminar este escenario?')) return;
        const url = '{{ url('/pruebas/scenarios') }}/' + encodeURIComponent(id);
        const { ok, data } = await apiFetch(url, { method: 'DELETE' });
        if (ok) {
            window.location.href = '{{ route('pruebas.index') }}';
        } else {
            window.showToast?.(data?.error || 'Error al eliminar.', 'error');
        }
    });

    // ── Lanzar escenario ─────────────────────────────────────────────────────

    document.getElementById('btn-lanzar-escenario')?.addEventListener('click', async () => {
        const scenarioId = document.getElementById('form-escenario')?.dataset.id;
        if (!scenarioId) { window.showToast?.('Guarda el escenario antes de lanzar.', 'error'); return; }

        const resultados = document.getElementById('pruebas-resultados');
        const tbody      = document.getElementById('pruebas-jobs-tbody');
        resultados.style.display = 'block';
        tbody.innerHTML = '<tr><td colspan="3" class="dbx-subtle">Inyectando trabajos…</td></tr>';
        document.getElementById('pruebas-resultado-resumen').textContent = '';

        const { ok, data } = await apiFetch('{{ route('pruebas.run') }}', {
            method: 'POST', json: { scenarioId },
        });
        if (!ok) { window.showToast?.(data?.error || 'Error al lanzar.', 'error'); return; }

        const jobIds = data.jobIds || [];
        const runId  = data.runId  || '';

        tbody.innerHTML = '';
        jobIds.forEach(id => {
            const tr = document.createElement('tr');
            tr.dataset.jobId = id;
            tr.innerHTML = `<td class="text-col" style="font-size:.8rem;">${id}</td>
                <td><span class="badge badge-warning">Pending</span></td><td>-</td>`;
            tbody.appendChild(tr);
        });

        if (data.errors?.length) window.showToast?.(`${data.errors.length} errores al inyectar.`, 'warning');

        if (jobIds.length && runId) {
            // Persistir estado para sobrevivir a un refresco de página
            sessionStorage.setItem('prueba_run', JSON.stringify({
                scenarioId, runId, jobIds, startedAt: Date.now(),
            }));
            startPolling(runId, jobIds);
        }
    });

    // ── Polling ──────────────────────────────────────────────────────────────

    const POLL_INTERVAL_MS = 1500;
    const POLL_TIMEOUT_MS  = 5 * 60 * 1000; // 5 minutos máximo

    function startPolling(runId, jobIds) {
        if (pollTimer) clearInterval(pollTimer);
        const startedAt = Date.now();

        async function tick() {
            // Timeout de seguridad: parar si lleva más de 5 minutos
            if (Date.now() - startedAt > POLL_TIMEOUT_MS) {
                clearInterval(pollTimer);
                pollTimer = null;
                sessionStorage.removeItem('prueba_run');
                document.getElementById('pruebas-resultado-resumen').textContent =
                    'Tiempo de espera agotado (5 min). Revisa la cola manualmente.';
                return;
            }

            const { ok, data } = await apiFetch(
                '{{ route('pruebas.jobs.status') }}?runId=' + encodeURIComponent(runId)
            );
            if (!ok) return;

            const byId = Object.fromEntries(data.map(j => [j.externalJobId, j]));
            let resolved = 0;

            jobIds.forEach(id => {
                const row = document.querySelector(`#pruebas-jobs-tbody tr[data-job-id="${id}"]`);
                if (!row) return;
                const job    = byId[id];
                const status = job?.status || 'Unknown';
                const css    = CSS_MAP[status] || 'badge-neutral';
                row.cells[1].innerHTML = `<span class="badge ${css}">${status}</span>`;
                row.cells[2].textContent = job?.printerId ? `#${job.printerId}` : '-';
                if (TERMINAL.has(status)) resolved++;
            });

            if (resolved === jobIds.length) {
                clearInterval(pollTimer);
                pollTimer = null;
                sessionStorage.removeItem('prueba_run');
                const okCount  = jobIds.filter(id => ['SpoolAccepted','PrintedConfirmed','PrintedUnknown'].includes(byId[id]?.status)).length;
                const errCount = jobIds.length - okCount;
                document.getElementById('pruebas-resultado-resumen').textContent =
                    `✓ ${okCount} OK  ✗ ${errCount} errores`;
            }
        }

        tick();
        pollTimer = setInterval(tick, POLL_INTERVAL_MS);
    }

    // ── Restaurar run activo tras refresco de página ──────────────────────────

    (function restoreActiveRun() {
        const saved = sessionStorage.getItem('prueba_run');
        if (!saved) return;
        let state;
        try { state = JSON.parse(saved); } catch { sessionStorage.removeItem('prueba_run'); return; }

        const { runId, jobIds, startedAt } = state;
        if (!runId || !jobIds?.length) { sessionStorage.removeItem('prueba_run'); return; }
        // Caducar si lleva más de 5 min desde que se lanzó
        if (Date.now() - startedAt > POLL_TIMEOUT_MS) { sessionStorage.removeItem('prueba_run'); return; }

        const resultados = document.getElementById('pruebas-resultados');
        const tbody      = document.getElementById('pruebas-jobs-tbody');
        if (!resultados || !tbody) return;

        resultados.style.display = 'block';
        tbody.innerHTML = '';
        jobIds.forEach(id => {
            const tr = document.createElement('tr');
            tr.dataset.jobId = id;
            tr.innerHTML = `<td class="text-col" style="font-size:.8rem;">${id}</td>
                <td><span class="badge badge-warning">…</span></td><td>-</td>`;
            tbody.appendChild(tr);
        });
        document.getElementById('pruebas-resultado-resumen').textContent = 'Retomando seguimiento…';

        startPolling(runId, jobIds);
    })();

    // ── Modal PDFs ───────────────────────────────────────────────────────────

    function openModal() { document.getElementById('modal-pdfs').style.display = 'flex'; }
    function closeModal() {
        document.getElementById('modal-pdfs').style.display = 'none';
        activePdfTargetBtn = null;
    }

    document.getElementById('btn-gestionar-pdfs')?.addEventListener('click', openModal);
    document.getElementById('btn-cerrar-modal-pdfs')?.addEventListener('click', closeModal);
    document.getElementById('modal-pdfs')?.addEventListener('click', e => {
        if (e.target === document.getElementById('modal-pdfs')) closeModal();
    });

    // Abrir modal desde botón de lote
    document.getElementById('lotes-tbody')?.addEventListener('click', e => {
        const btn = e.target.closest('.btn-elegir-pdf');
        if (!btn) return;
        activePdfTargetBtn = btn;
        openModal();
    });

    // Seleccionar PDF desde modal → asignar al lote
    document.getElementById('pdfs-tbody')?.addEventListener('click', e => {
        const btn = e.target.closest('.btn-seleccionar-pdf');
        if (!btn || !activePdfTargetBtn) return;
        const id   = btn.dataset.pdfId;
        const name = btn.dataset.pdfName;
        activePdfTargetBtn.textContent = name.length > 22 ? name.slice(0, 22) + '…' : name;
        activePdfTargetBtn.dataset.pdfId = id;
        const hidden = activePdfTargetBtn.closest('td')?.querySelector('input[type="hidden"]');
        if (hidden) hidden.value = id;
        closeModal();
    });

    // Subir PDF
    document.getElementById('input-subir-pdf')?.addEventListener('change', async e => {
        const file = e.target.files?.[0];
        if (!file) return;
        const status = document.getElementById('pdf-upload-status');
        status.textContent = 'Subiendo…';
        const fd = new FormData();
        fd.append('pdf', file);
        fd.append('_token', csrf);
        const res = await fetch('{{ route('pruebas.pdfs.upload') }}', { method: 'POST', body: fd });
        const data = await res.json().catch(() => ({}));
        if (res.ok) {
            status.textContent = '';
            appendPdfRow(data);
            const emptyMsg = document.getElementById('pdfs-empty-msg');
            if (emptyMsg) emptyMsg.style.display = 'none';
            const table = document.getElementById('tabla-pdfs');
            if (table) table.style.display = '';
            updatePdfCount(1);
            window.showToast?.('PDF subido.', 'success');
        } else {
            status.textContent = 'Error: ' + (data?.message || 'Fallo al subir');
            window.showToast?.('Error al subir el PDF.', 'error');
        }
        e.target.value = '';
    });

    function appendPdfRow(pdf) {
        const tbody = document.getElementById('pdfs-tbody');
        if (!tbody) return;
        const tr   = document.createElement('tr');
        tr.dataset.pdfId = pdf.id;
        const kb   = ((pdf.size || 0) / 1024).toFixed(1);
        tr.innerHTML = `
            <td>${pdf.name}</td>
            <td>${kb} KB</td>
            <td>
                <button type="button" class="btn btn-ghost btn-sm btn-seleccionar-pdf"
                        data-pdf-id="${pdf.id}" data-pdf-name="${pdf.name}">Seleccionar</button>
                <button type="button" class="btn btn-danger btn-sm btn-eliminar-pdf"
                        data-pdf-id="${pdf.id}">Eliminar</button>
            </td>`;
        tbody.appendChild(tr);
    }

    function updatePdfCount(delta) {
        const btn = document.getElementById('btn-gestionar-pdfs');
        if (!btn) return;
        const m = btn.textContent.match(/\((\d+)\)/);
        const n = m ? parseInt(m[1], 10) + delta : Math.max(0, delta);
        btn.textContent = `Gestionar PDFs (${n})`;
    }

    // Eliminar PDF desde modal
    document.getElementById('pdfs-tbody')?.addEventListener('click', async e => {
        const btn = e.target.closest('.btn-eliminar-pdf');
        if (!btn) return;
        const id  = btn.dataset.pdfId;
        const url = '{{ url('/pruebas/pdfs') }}/' + encodeURIComponent(id);
        const { ok, data } = await apiFetch(url, { method: 'DELETE' });
        if (ok) {
            btn.closest('tr').remove();
            updatePdfCount(-1);
            window.showToast?.('PDF eliminado.', 'success');
        } else {
            window.showToast?.(data?.error || 'No se pudo eliminar.', 'error');
        }
    });

})();
