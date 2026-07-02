const $ = id => document.getElementById(id);

// Zaten girişliyse doğrudan panele git.
if (localStorage.getItem('token')) location.href = 'index.html';

async function auth(path) {
    const res = await fetch('/api/auth/' + path, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username: $('username').value.trim(), password: $('password').value })
    });
    const data = await res.json().catch(() => ({}));
    if (res.ok) {
        localStorage.setItem('token', data.token);
        localStorage.setItem('username', data.username);
        localStorage.setItem('isAdmin', data.isAdmin ? '1' : '');
        location.href = data.isAdmin ? 'admin.html' : 'index.html';   // admin → admin paneli
    } else {
        $('msg').textContent = data.message || 'Hata';
    }
}

$('loginBtn').addEventListener('click', () => auth('login'));
$('registerBtn').addEventListener('click', () => auth('register'));
$('password').addEventListener('keydown', e => { if (e.key === 'Enter') auth('login'); });
