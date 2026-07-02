const $ = id => document.getElementById(id);

// --- Auth guard: token yoksa giriş sayfasına gönder ---
const token = localStorage.getItem('token');
const username = localStorage.getItem('username');
if (!token) location.href = 'login.html';
$('who').textContent = username;
if (localStorage.getItem('isAdmin') === '1') $('adminLink').style.display = 'inline';   // admin linkini göster

let waitingData = [];

function toLogin() {
    localStorage.removeItem('token');
    localStorage.removeItem('username');
    localStorage.removeItem('isAdmin');
    location.href = 'login.html';
}

$('joinBtn').addEventListener('click', async () => {
    const res = await fetch('/api/matchmaking/queue', {
        method: 'POST',
        headers: { 'Authorization': 'Bearer ' + token }
    });
    if (res.status === 401) { toLogin(); return; }   // token süresi dolmuş
    $('msg2').textContent = 'Kuyruğa katıldın';
    refresh();
});

$('logoutBtn').addEventListener('click', async () => {
    await fetch('/api/auth/logout', { method: 'POST', headers: { 'Authorization': 'Bearer ' + token } }).catch(() => { });
    toLogin();
});

function renderRows(body, rows, fn) { body.innerHTML = rows.map(fn).join(''); }

function renderWaiting() {
    $('wCount').textContent = waitingData.length;
    renderRows($('wBody'), waitingData, r => {
        const wait = r.joinedAtUtc
            ? Math.floor((Date.now() - new Date(r.joinedAtUtc)) / 1000) + ' sn'
            : '-';
        return `<tr><td>${r.userId}</td><td>${r.score}</td><td>${wait}</td></tr>`;
    });
}

async function refresh() {
    try {
        const [lb, w, h] = await Promise.all([
            fetch('/api/matchmaking/leaderboard').then(r => r.json()),
            fetch('/api/matchmaking/waiting').then(r => r.json()),
            fetch('/api/matchmaking/history').then(r => r.json())
        ]);
        $('lbCount').textContent = lb.length;
        renderRows($('lbBody'), lb, (r, i) =>
            `<tr><td>${i + 1}</td><td>${r.userId}</td><td>${r.score}</td><td>${r.online ? 'online' : 'offline'}</td></tr>`);
        waitingData = w;
        renderWaiting();

        $('hCount').textContent = h.length;
        renderRows($('hBody'), h, m => {
            const t = new Date(m.completedAtUtc).toLocaleTimeString();
            return `<tr><td>${m.winnerId}</td><td>${m.loserId}</td><td>${m.winnerScore} / ${m.loserScore}</td><td>${t}</td></tr>`;
        });
    } catch (e) { }
}

let refreshTimer = null;
function scheduleRefresh() {
    if (refreshTimer) return;
    refreshTimer = setTimeout(() => { refreshTimer = null; refresh(); }, 500);
}

// --- SignalR (push) ---
const connection = new signalR.HubConnectionBuilder()
    .withUrl('/hub/matchmaking')
    .withAutomaticReconnect()
    .build();
connection.on('matchCompleted', scheduleRefresh);
connection.onreconnected(() => { $('conn').textContent = 'bağlı'; refresh(); });
connection.onclose(() => { $('conn').textContent = 'bağlantı kapandı'; });
connection.start()
    .then(() => { $('conn').textContent = 'bağlı'; })
    .catch(() => { $('conn').textContent = 'bağlanamadı'; });

// --- Başlangıç ---
refresh();
setInterval(renderWaiting, 1000);   // sadece "Bekleme" sayacını yerelde günceller (ağ yok)
