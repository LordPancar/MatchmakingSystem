import http from 'k6/http';
import { textSummary } from 'https://jslib.k6.io/k6-summary/0.0.2/index.js';

export const options = {
    vus: 50,
    iterations: 1000,
};

export default function () {
    const payload = JSON.stringify({
        userId: `user-${__VU}-${__ITER}`,
        score: 1000 + Math.floor(Math.random() * 1000),
    });
    http.post('http://localhost:8080/api/matchmaking/queue', payload, {
        headers: { 'Content-Type': 'application/json' },
    });
}

// Test bitince k6 bunu cagirir: hem ekrana basar hem de dosyaya yazar.
export function handleSummary(data) {
    return {
        'stdout': textSummary(data, { indent: ' ', enableColors: true }),
        'loadtest-results/k6-result.txt': textSummary(data, { indent: ' ', enableColors: false }),
        'loadtest-results/k6-summary.json': JSON.stringify(data, null, 2),
    };
}
