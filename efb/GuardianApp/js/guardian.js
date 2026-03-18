/**
 * Flight Guardian EFB — Client-side JavaScript
 *
 * Polls the Guardian HTTP API and updates the UI.
 * Designed for MSFS 2024 Coherent GT browser engine.
 */

const API_BASE = 'http://localhost:9847';
const POLL_INTERVAL_MS = 2000;

let alertHistory = [];
let currentScreen = 'dashboard';

// ── Screen Navigation ──

function showScreen(screen) {
    document.querySelectorAll('.screen').forEach(s => s.classList.remove('active'));
    document.querySelectorAll('.nav-tab').forEach(t => t.classList.remove('active'));

    document.getElementById(`screen-${screen}`).classList.add('active');
    document.querySelector(`[data-screen="${screen}"]`).classList.add('active');

    currentScreen = screen;
}

// ── API Polling ──

async function fetchStatus() {
    try {
        const res = await fetch(`${API_BASE}/api/status`);
        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        const data = await res.json();
        updateDashboard(data);
        setConnectionState('connected');
    } catch (e) {
        setConnectionState('disconnected');
    }
}

async function fetchAlerts() {
    try {
        const res = await fetch(`${API_BASE}/api/alerts`);
        if (!res.ok) return;
        alertHistory = await res.json();
        if (currentScreen === 'history') renderAlertHistory();
    } catch (e) {
        // Silently fail — connection issue handled by status poll
    }
}

function setConnectionState(state) {
    const dot = document.getElementById('connection-dot');
    const text = document.getElementById('connection-text');

    dot.className = `status-dot ${state}`;
    text.textContent = state === 'connected' ? 'Connected'
        : state === 'connecting' ? 'Connecting...'
        : 'Disconnected';
}

// ── Dashboard Updates ──

function updateDashboard(status) {
    // Flight phase
    document.getElementById('flight-phase').textContent = status.flight_phase;
    const sterileBadge = document.getElementById('sterile-badge');
    sterileBadge.style.display = status.sterile_cockpit ? '' : 'none';

    // Alert counts
    const counts = status.active_alert_count;
    document.getElementById('count-critical').textContent = counts.critical;
    document.getElementById('count-warning').textContent = counts.warning;
    document.getElementById('count-advisory').textContent = counts.advisory;

    // Aircraft info
    document.getElementById('aircraft-name').textContent =
        status.aircraft_name || status.aircraft_id || '---';
    document.getElementById('uptime').textContent = formatUptime(status.uptime_sec);

    // Rules summary
    renderRules(status.rules);

    // Latest alert banner
    updateLatestBanner();
}

function updateLatestBanner() {
    const banner = document.getElementById('latest-alert');
    const active = alertHistory.filter(a => a.severity !== 'Info');

    if (active.length === 0) {
        banner.style.display = 'none';
        return;
    }

    const latest = active[0];
    banner.style.display = 'flex';
    banner.className = `alert-banner severity-${latest.severity.toLowerCase()}`;

    document.getElementById('latest-severity').textContent = latest.severity.toUpperCase();
    document.getElementById('latest-rule').textContent = latest.rule_id;
    document.getElementById('latest-text').textContent = latest.text || latest.text_key;
    document.getElementById('latest-time').textContent = formatTime(latest.timestamp);
}

function renderRules(rules) {
    const container = document.getElementById('rules-summary');
    if (!rules || rules.length === 0) return;

    container.innerHTML = rules.map(r => {
        const dotClass = r.state === 'Enabled' ? 'enabled'
            : r.state === 'DisabledCrashed' ? 'crashed'
            : 'disabled';
        return `<div class="rule-chip">
            <span class="rule-dot ${dotClass}"></span>
            <span>${r.rule_id}</span>
        </div>`;
    }).join('');
}

// ── Alert History ──

function renderAlertHistory() {
    const list = document.getElementById('alert-list');
    const countBadge = document.getElementById('history-count');
    countBadge.textContent = `${alertHistory.length} alerts`;

    list.innerHTML = alertHistory.map((alert, i) => {
        const sevClass = `severity-${alert.severity.toLowerCase()}`;
        const sevColor = getSeverityColor(alert.severity);
        return `<div class="alert-item ${sevClass}" onclick="showAlertDetail(${i})">
            <span class="alert-item-time">${formatTime(alert.timestamp)}</span>
            <span class="alert-item-severity" style="color:${sevColor}">${alert.severity.toUpperCase()}</span>
            <span class="alert-item-text">${alert.text || alert.text_key}</span>
        </div>`;
    }).join('');
}

function showAlertDetail(index) {
    const alert = alertHistory[index];
    if (!alert) return;

    document.getElementById('detail-rule').textContent = `${alert.rule_id} — ${alert.text_key}`;
    document.getElementById('detail-severity').textContent = alert.severity;
    document.getElementById('detail-time').textContent = alert.timestamp;
    document.getElementById('detail-phase').textContent = alert.flight_phase;
    document.getElementById('detail-textkey').textContent = alert.text_key;

    const telDiv = document.getElementById('detail-telemetry');
    if (alert.telemetry && Object.keys(alert.telemetry).length > 0) {
        telDiv.innerHTML = Object.entries(alert.telemetry)
            .map(([k, v]) => `<div>${k}: ${typeof v === 'number' ? v.toFixed(2) : v}</div>`)
            .join('');
    } else {
        telDiv.innerHTML = '<div style="color:var(--text-muted)">No telemetry data</div>';
    }

    document.getElementById('alert-detail').style.display = 'block';
}

function closeDetail() {
    document.getElementById('alert-detail').style.display = 'none';
}

// ── Settings ──

async function updateSetting(type) {
    const body = {};

    switch (type) {
        case 'sterile':
            body.sterile_cockpit_manual = document.getElementById('setting-sterile').checked;
            break;
        case 'audio':
            body.audio_enabled = document.getElementById('setting-audio').checked;
            break;
        case 'sensitivity':
            body.sensitivity = document.getElementById('setting-sensitivity').value;
            break;
    }

    try {
        await fetch(`${API_BASE}/api/settings`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body),
        });
    } catch (e) {
        console.error('Failed to update setting:', e);
    }
}

async function silenceAlarm() {
    try {
        await fetch(`${API_BASE}/api/silence`, { method: 'POST' });
    } catch (e) {
        console.error('Failed to silence alarm:', e);
    }
}

// ── Utilities ──

function formatTime(isoString) {
    if (!isoString) return '---';
    try {
        const d = new Date(isoString);
        return d.toLocaleTimeString('en-US', { hour12: false, hour: '2-digit', minute: '2-digit', second: '2-digit' });
    } catch {
        return isoString;
    }
}

function formatUptime(sec) {
    if (!sec || sec < 0) return '---';
    const m = Math.floor(sec / 60);
    const h = Math.floor(m / 60);
    if (h > 0) return `${h}h ${m % 60}m`;
    return `${m}m ${Math.floor(sec % 60)}s`;
}

function getSeverityColor(severity) {
    switch ((severity || '').toLowerCase()) {
        case 'critical': return 'var(--critical)';
        case 'warning': return 'var(--warning)';
        case 'advisory': return 'var(--advisory)';
        default: return 'var(--info)';
    }
}

// ── Polling Loop ──

async function poll() {
    await fetchStatus();
    await fetchAlerts();
}

// Start polling
poll();
setInterval(poll, POLL_INTERVAL_MS);
