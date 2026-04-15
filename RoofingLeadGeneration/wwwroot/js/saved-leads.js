// ── State ──────────────────────────────────────────────────────────
    let allLeads   = [];
    let sortCol    = 'riskLevel';
    let sortDir    = 'desc';
    let activeFilter = 'all';
    let editingId  = null;   // id of lead currently in edit mode

    // ── Boot ───────────────────────────────────────────────────────────
    document.addEventListener('DOMContentLoaded', loadLeads);

    // ── Tab switching ──────────────────────────────────────────────────
    function switchTab(tab) {
        const isLeads = tab === 'leads';
        document.getElementById('panelLeads').classList.toggle('hidden', !isLeads);
        document.getElementById('panelSources').classList.toggle('hidden', isLeads);
        document.getElementById('tabBtnLeads').classList.toggle('active', isLeads);
        document.getElementById('tabBtnSources').classList.toggle('active', !isLeads);
    }

    // ── Load leads from API ────────────────────────────────────────────
    async function loadLeads() {
        setLoading(true);
        try {
            const resp = await fetch('/Leads');
            if (!resp.ok) throw new Error(`HTTP ${resp.status}`);
            allLeads = await resp.json();
            updateFilterCounts();
            renderTable();
        } catch (e) {
            setLoading(false);
            document.getElementById('leadsBody').innerHTML =
                `<tr><td colspan="12" class="text-center text-red-400 py-8 text-sm px-4">
                    <i class="fa-solid fa-triangle-exclamation mr-2"></i>Failed to load: ${escapeHtml(e.message)}
                </td></tr>`;
        }
    }

    // ── Filter ─────────────────────────────────────────────────────────
    function setFilter(f) {
        activeFilter = f;
        document.querySelectorAll('.filter-btn').forEach(b => {
            b.classList.toggle('active', b.dataset.f === f);
        });
        renderTable();
    }

    function updateFilterCounts() {
        document.getElementById('fAll').textContent    = allLeads.length;
        document.getElementById('fHigh').textContent   = allLeads.filter(l => l.riskLevel === 'High').length;
        document.getElementById('fMedium').textContent = allLeads.filter(l => l.riskLevel === 'Medium').length;
        document.getElementById('fLow').textContent    = allLeads.filter(l => l.riskLevel === 'Low').length;
        const cnt = allLeads.length;
        document.getElementById('heroCount').textContent = cnt ? `(${cnt})` : '';
    }

    // ── Sort ───────────────────────────────────────────────────────────
    function sortBy(col) {
        if (sortCol === col) sortDir = sortDir === 'asc' ? 'desc' : 'asc';
        else { sortCol = col; sortDir = 'asc'; }
        renderTable();
    }

    function getSortValue(lead, col) {
        const dmgOrder = { Severe:0, Significant:1, Notable:2, Moderate:3, Minor:4, Minimal:5 };
        switch (col) {
            case 'address':         return (lead.address        || '').toLowerCase();
            case 'riskLevel':       return { High:0, Medium:1, Low:2 }[lead.riskLevel] ?? 3;
            case 'hailSize':        return parseFloat(lead.hailSize) || 0;
            case 'lastStormDate':   return lead.lastStormDate   || '';
            case 'estimatedDamage': return dmgOrder[lead.estimatedDamage] ?? 6;
            case 'roofAge':         return lead.roofAge         || 0;
            case 'propertyType':    return (lead.propertyType   || '').toLowerCase();
            case 'ownerName':       return (lead.ownerName      || '').toLowerCase();
            case 'ownerPhone':      return (lead.ownerPhone     || '').toLowerCase();
            case 'ownerEmail':      return (lead.ownerEmail     || '').toLowerCase();
            default:                return '';
        }
    }

    // ── Render table ───────────────────────────────────────────────────
    function renderTable() {
        setLoading(false);

        const query = (document.getElementById('searchInput').value || '').toLowerCase();

        let rows = allLeads
            .filter(l => activeFilter === 'all' || l.riskLevel === activeFilter)
            .filter(l => !query || [l.address, l.ownerName, l.ownerPhone, l.ownerEmail]
                            .some(v => (v || '').toLowerCase().includes(query)));

        // Sort
        rows = rows.sort((a, b) => {
            const av = getSortValue(a, sortCol);
            const bv = getSortValue(b, sortCol);
            let cmp = typeof av === 'number' ? av - bv : av.localeCompare(bv, undefined, { sensitivity: 'base' });
            return sortDir === 'asc' ? cmp : -cmp;
        });

        // Update sort arrows on headers
        document.querySelectorAll('#leadsTable th').forEach(th => {
            th.classList.remove('sort-asc', 'sort-desc');
        });
        const headers = ['address','riskLevel','ownerName','ownerPhone','hailSize','lastStormDate','estimatedDamage','roofAge','propertyType','ownerEmail'];
        const colIdx = headers.indexOf(sortCol);
        if (colIdx >= 0) {
            const ths = document.querySelectorAll('#leadsTable th');
            ths[colIdx].classList.add(sortDir === 'asc' ? 'sort-asc' : 'sort-desc');
        }

        // Render rows
        const body = document.getElementById('leadsBody');
        document.getElementById('leadsEmpty').classList.add('hidden');
        document.getElementById('leadsNoMatch').classList.add('hidden');

        if (allLeads.length === 0) {
            body.innerHTML = '';
            document.getElementById('leadsEmpty').classList.remove('hidden');
            return;
        }
        if (rows.length === 0) {
            body.innerHTML = '';
            document.getElementById('leadsNoMatch').classList.remove('hidden');
            return;
        }

        body.innerHTML = rows.map(l => buildRow(l)).join('');
    }

    // ── Build table row ─────────────────────────────────────────────────
    function buildRow(lead) {
        const rc = { High:'badge-high', Medium:'badge-medium', Low:'badge-low' }[lead.riskLevel] || 'badge-low';
        const isEditing = editingId === lead.id;

        const ownerNameCell  = isEditing
            ? `<input class="owner-input" id="eName_${lead.id}" value="${escapeAttr(lead.ownerName || '')}" placeholder="Owner name…" />`
            : `<span class="text-slate-300">${escapeHtml(lead.ownerName || '')}${!lead.ownerName ? '<span class="text-slate-600 italic">—</span>' : ''}</span>`;
        const ownerPhoneCell = isEditing
            ? `<input class="owner-input" id="ePhone_${lead.id}" value="${escapeAttr(lead.ownerPhone || '')}" placeholder="(555) 000-0000" />`
            : `<span class="text-slate-300">${lead.ownerPhone ? `<a href="tel:${escapeAttr(lead.ownerPhone)}" class="hover:text-brand transition-colors">${escapeHtml(lead.ownerPhone)}</a>` : '<span class="text-slate-600 italic">—</span>'}</span>`;
        const ownerEmailCell = isEditing
            ? `<input class="owner-input" id="eEmail_${lead.id}" value="${escapeAttr(lead.ownerEmail || '')}" placeholder="owner@example.com" />`
            : `<span class="text-slate-300">${lead.ownerEmail ? `<a href="mailto:${escapeAttr(lead.ownerEmail)}" class="hover:text-brand transition-colors truncate block max-w-[140px]">${escapeHtml(lead.ownerEmail)}</a>` : '<span class="text-slate-600 italic">—</span>'}</span>`;

        const actionCell = isEditing
            ? `<div class="flex items-center justify-center gap-1">
                  <button onclick="saveOwner(${lead.id})"
                      class="w-7 h-7 rounded-lg flex items-center justify-center bg-green-500/20 hover:bg-green-500/40 text-green-400 border border-green-500/30 transition" title="Save">
                      <i class="fa-solid fa-check text-xs"></i>
                  </button>
                  <button onclick="cancelEdit()"
                      class="w-7 h-7 rounded-lg flex items-center justify-center bg-slate-600/40 hover:bg-slate-600 text-slate-400 border border-slate-600 transition" title="Cancel">
                      <i class="fa-solid fa-xmark text-xs"></i>
                  </button>
               </div>`
            : `<div class="flex items-center justify-center gap-1">
                  <button onclick="startEdit(${lead.id})"
                      class="w-7 h-7 rounded-lg flex items-center justify-center bg-slate-700 hover:bg-slate-600 text-slate-400 hover:text-brand border border-slate-600 transition" title="Edit owner info">
                      <i class="fa-solid fa-pen text-xs"></i>
                  </button>
                  <button onclick="deleteLead(${lead.id}, this)"
                      class="w-7 h-7 rounded-lg flex items-center justify-center bg-red-500/10 hover:bg-red-500/30 text-red-400 border border-red-500/20 transition" title="Delete lead">
                      <i class="fa-solid fa-trash-can text-xs"></i>
                  </button>
               </div>`;

        return `
        <tr data-lead-id="${lead.id}" class="${isEditing ? 'editing' : ''}">
            <td class="font-medium text-white" style="max-width:200px">
                <span class="block truncate" title="${escapeAttr(lead.address)}">${escapeHtml(lead.address)}</span>
                ${lead.sourceAddress ? `<span class="block text-xs text-slate-500 truncate" title="${escapeAttr(lead.sourceAddress)}">from ${escapeHtml(lead.sourceAddress)}</span>` : ''}
            </td>
            <td><span class="${rc} px-2 py-0.5 rounded-full text-xs font-bold">${escapeHtml(lead.riskLevel)}</span></td>
            <td class="hidden sm:table-cell">${ownerNameCell}</td>
            <td class="hidden sm:table-cell whitespace-nowrap">${ownerPhoneCell}</td>
            <td class="hidden md:table-cell whitespace-nowrap">${escapeHtml(lead.hailSize)}</td>
            <td class="hidden md:table-cell whitespace-nowrap">${escapeHtml(lead.lastStormDate)}</td>
            <td class="hidden lg:table-cell">${escapeHtml(lead.estimatedDamage)}</td>
            <td class="hidden lg:table-cell whitespace-nowrap">${lead.roofAge} yrs</td>
            <td class="hidden lg:table-cell">${escapeHtml(lead.propertyType)}</td>
            <td class="hidden xl:table-cell">${ownerEmailCell}</td>
            <td class="sticky-actions">${actionCell}</td>
        </tr>`;
    }

    // ── Edit owner info ────────────────────────────────────────────────
    function startEdit(id) {
        editingId = id;
        renderTable();
        // Focus first input
        const input = document.getElementById(`eName_${id}`);
        if (input) input.focus();
    }

    function cancelEdit() {
        editingId = null;
        renderTable();
    }

    async function saveOwner(id) {
        const name  = document.getElementById(`eName_${id}`)?.value.trim()  || null;
        const phone = document.getElementById(`ePhone_${id}`)?.value.trim() || null;
        const email = document.getElementById(`eEmail_${id}`)?.value.trim() || null;

        try {
            const resp = await fetch(`/Leads/${id}/Owner`, {
                method:  'PATCH',
                headers: { 'Content-Type': 'application/json' },
                body:    JSON.stringify({ ownerName: name, ownerPhone: phone, ownerEmail: email })
            });
            if (!resp.ok) throw new Error(`HTTP ${resp.status}`);

            // Update local data
            const lead = allLeads.find(l => l.id === id);
            if (lead) { lead.ownerName = name; lead.ownerPhone = phone; lead.ownerEmail = email; }

            editingId = null;
            renderTable();
            showToast('Owner info saved', true);
        } catch (e) {
            showToast('Save failed: ' + e.message, false);
        }
    }

    // ── Delete ─────────────────────────────────────────────────────────
    async function deleteLead(id, btn) {
        if (!confirm('Delete this lead? This cannot be undone.')) return;
        btn.disabled = true;
        try {
            const resp = await fetch(`/Leads/${id}`, { method: 'DELETE' });
            if (resp.status === 204) {
                allLeads = allLeads.filter(l => l.id !== id);
                updateFilterCounts();
                renderTable();
                showToast('Lead deleted', true);
            } else { throw new Error(`HTTP ${resp.status}`); }
        } catch (e) {
            btn.disabled = false;
            showToast('Delete failed: ' + e.message, false);
        }
    }

    // ── Export CSV ─────────────────────────────────────────────────────
    function exportCSV() {
        const cols = ['address','riskLevel','hailSize','lastStormDate','estimatedDamage','roofAge',
                      'propertyType','ownerName','ownerPhone','ownerEmail','sourceAddress','savedAt'];
        const header = ['Address','Risk Level','Hail Size','Last Storm Date','Est. Damage','Roof Age (yrs)',
                        'Property Type','Owner Name','Owner Phone','Owner Email','Source Address','Saved At'];
        const csv = [header.join(','),
            ...allLeads.map(l => cols.map(c => `"${(l[c] ?? '').toString().replace(/"/g, '""')}"`).join(','))
        ].join('\n');
        const a = document.createElement('a');
        a.href = URL.createObjectURL(new Blob([csv], { type: 'text/csv' }));
        a.download = `StormLeads_${new Date().toISOString().slice(0,10)}.csv`;
        a.click();
    }

    // ── Helpers ────────────────────────────────────────────────────────
    function setLoading(on) {
        document.getElementById('leadsLoading').classList.toggle('hidden', !on);
        if (on) document.getElementById('leadsBody').innerHTML = '';
    }

    function escapeHtml(s) {
        if (s === null || s === undefined) return '';
        return String(s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
    }
    function escapeAttr(s) { return escapeHtml(s); }

    // ── Enrich owner names via Regrid ───────────────────────────────
    async function enrichAll() {
        const btn = document.getElementById('enrichBtn');
        btn.disabled = true;
        btn.innerHTML = '<svg class="spinner inline w-3 h-3 mr-1.5" viewBox="0 0 24 24" fill="none"><circle cx="12" cy="12" r="10" stroke="currentColor" stroke-width="3" stroke-dasharray="60" stroke-dashoffset="20"/></svg>Enriching…';
        try {
            const resp = await fetch('/Leads/EnrichAll', { method: 'POST' });
            const r    = await resp.json();
            if (!resp.ok) throw new Error(r.error || 'HTTP ' + resp.status);
            if (r.enriched > 0) {
                showToast('Owner names added for ' + r.enriched + ' lead' + (r.enriched !== 1 ? 's' : ''), true);
                await loadLeads();
            } else if (r.queued === 0) {
                showToast('All leads already have owner names', true);
            } else {
                showToast('No owner data found — check Regrid token in appsettings.json', false);
            }
        } catch (e) {
            showToast('Enrichment failed: ' + e.message, false);
        } finally {
            btn.disabled = false;
            btn.innerHTML = '<i class="fa-solid fa-wand-magic-sparkles text-brand mr-1.5"></i>Enrich Owners';
        }
    }
