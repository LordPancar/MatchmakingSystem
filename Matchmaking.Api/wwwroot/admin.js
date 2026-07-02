const $ = id => document.getElementById(id);

// --- Guard: admin değilse giriş sayfasına ---
const token = localStorage.getItem('token');
const isAdmin = localStorage.getItem('isAdmin') === '1';
if (!token || !isAdmin) location.href = 'login.html';
$('who').textContent = localStorage.getItem('username');

const authHeaders = () => ({ 'Authorization': 'Bearer ' + token });
const jsonAuthHeaders = () => ({ 'Authorization': 'Bearer ' + token, 'Content-Type': 'application/json' });

function toLogin() {
    localStorage.removeItem('token');
    localStorage.removeItem('username');
    localStorage.removeItem('isAdmin');
    location.href = 'login.html';
}

$('logoutBtn').addEventListener('click', async () => {
    await fetch('/api/auth/logout', { method: 'POST', headers: authHeaders() }).catch(() => { });
    toLogin();
});

function renderRows(body, rows, fn) { body.innerHTML = rows.map(fn).join(''); }

// --- Bot ekle (admin) ---
async function seed(userId, score) {
    const res = await fetch('/api/matchmaking/seed', {
        method: 'POST', headers: jsonAuthHeaders(),
        body: JSON.stringify({ userId: userId || null, score })
    });
    if (res.status === 401 || res.status === 403) { toLogin(); return; }
    const d = await res.json().catch(() => ({}));
    $('seedMsg').textContent = res.ok ? (d.userId + ' eklendi') : (d.message || 'Hata');
    refresh();
}
$('addBotBtn').addEventListener('click', () => {
    const id = $('botId').value.trim();
    const s = $('botScore').value ? Number($('botScore').value) : null;
    seed(id, s);
});
$('randomBtn').addEventListener('click', () => seed(null, null));

// --- Oyuncu sil / online-offline (admin) ---
async function deletePlayer(userId) {
    await fetch('/api/matchmaking/player/' + encodeURIComponent(userId), { method: 'DELETE', headers: authHeaders() });
    refresh();
}
async function toggleOnline(userId, online) {
    await fetch('/api/matchmaking/player/' + encodeURIComponent(userId) + '/online', {
        method: 'POST', headers: jsonAuthHeaders(), body: JSON.stringify({ enabled: online })
    });
    refresh();
}

// --- Simülatör (admin) ---
async function loadSimulator() {
    try { const s = await fetch('/api/matchmaking/simulator').then(r => r.json()); $('simToggle').checked = s.enabled; } catch (e) { }
}
$('simToggle').addEventListener('change', async e => {
    await fetch('/api/matchmaking/simulator', { method: 'POST', headers: jsonAuthHeaders(), body: JSON.stringify({ enabled: e.target.checked }) });
});

// --- Hesap ekle / admin yap-al / hesap sil (tümü admin) ---
async function createUser() {
    const res = await fetch('/api/admin/users', {
        method: 'POST', headers: jsonAuthHeaders(),
        body: JSON.stringify({ username: $('newUser').value.trim(), password: $('newPass').value, isAdmin: $('newAdmin').checked })
    });
    if (res.status === 401 || res.status === 403) { toLogin(); return; }
    const d = await res.json().catch(() => ({}));
    $('userMsg').textContent = res.ok ? (d.username + ' eklendi') : (d.message || 'Hata');
    if (res.ok) { $('newUser').value = ''; $('newPass').value = ''; $('newAdmin').checked = false; }
    loadUsers();
}
$('addUserBtn').addEventListener('click', createUser);

async function setRole(username, isAdmin) {
    const res = await fetch('/api/admin/users/' + encodeURIComponent(username) + '/role', {
        method: 'PUT', headers: jsonAuthHeaders(), body: JSON.stringify({ isAdmin })
    });
    if (res.status === 401 || res.status === 403) { toLogin(); return; }
    const d = await res.json().catch(() => ({}));
    if (!res.ok) $('userMsg').textContent = d.message || 'Hata';
    loadUsers();
}

async function deleteUser(username) {
    const res = await fetch('/api/admin/users/' + encodeURIComponent(username), { method: 'DELETE', headers: authHeaders() });
    if (res.status === 401 || res.status === 403) { toLogin(); return; }
    const d = await res.json().catch(() => ({}));
    if (!res.ok) $('userMsg').textContent = d.message || 'Hata';
    refresh();   // hem hesaplar hem leaderboard değişebilir
}

// --- Kayıtlı hesaplar (tüm veritabanı) ---
async function loadUsers() {
    const res = await fetch('/api/admin/users', { headers: authHeaders() });
    if (res.status === 401 || res.status === 403) { toLogin(); return; }
    const users = await res.json();
    $('uCount').textContent = users.length;
    renderRows($('uBody'), users, u => {
        const d = new Date(u.createdAtUtc).toLocaleString();
        const roleBtn = u.isAdmin
            ? `<button onclick="setRole('${u.username}', false)">Admin al</button>`
            : `<button onclick="setRole('${u.username}', true)">Admin yap</button>`;
        return `<tr><td>${u.id}</td><td>${u.username}</td><td>${u.isAdmin ? '✓' : ''}</td><td>${d}</td>` +
            `<td>${roleBtn} <button onclick="deleteUser('${u.username}')">Sil</button></td></tr>`;
    });
}

async function refresh() {
    try {
        const lb = await fetch('/api/matchmaking/leaderboard').then(r => r.json());
        $('lbCount').textContent = lb.length;
        renderRows($('lbBody'), lb, (r, i) =>
            `<tr><td>${i + 1}</td><td>${r.userId}</td><td>${r.score}</td>` +
            `<td>${r.online ? 'online' : 'offline'}</td>` +
            `<td><button onclick="toggleOnline('${r.userId}', ${!r.online})">${r.online ? 'Offline yap' : 'Online yap'}</button> ` +
            `<button onclick="deletePlayer('${r.userId}')">Sil</button></td></tr>`);
        loadUsers();
    } catch (e) { }
}

// --- Başlangıç --- (admin paneli düşük trafik; basit poll yeterli)
loadSimulator();
refresh();
setInterval(refresh, 3000);
