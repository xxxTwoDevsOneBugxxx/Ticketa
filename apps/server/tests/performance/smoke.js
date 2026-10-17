
import http from 'k6/http';
import { check, group } from 'k6';

const BASE_URL = (__ENV.BASE_URL || 'http://localhost:5260').replace(/\/+$/, '');

export const options = {
    vus: 1,
    iterations: 1,
    thresholds: {
        checks: ['rate==1'],
        http_req_failed: ['rate==0'],
    },
};

function checkJsonResponse(name, response) {
    let validJson = false;

    try {
        JSON.parse(response.body);
        validJson = true;
    } catch (_) {
        // Invalid JSON will fail the check below.
    }

    check(response, {
        [`${name}: HTTP 200`]: (r) => r.status === 200,
        [`${name}: valid JSON`]: () => validJson,
    });
}

export default function () {
    group('Ticketa API Smoke Test', () => {
        const endpoints = [
            {
                name: 'Movies paginated',
                path: '/api/movies?page=1&pageSize=10',
            },
            {
                name: 'Movies now showing',
                path: '/api/movies/NowShowing',
            },
            {
                name: 'Movies coming soon',
                path: '/api/movies/coming-soon',
            },
            {
                name: 'Movies top booked',
                path: '/api/movies/top-booked?count=6',
            },
        ];

        for (const endpoint of endpoints) {
            group(endpoint.name, () => {
                const response = http.get(`${BASE_URL}${endpoint.path}`, {
                    tags: { endpoint: endpoint.name },
                });

                console.log(`Requested URL: ${response.url}`);
                console.log(`Status: ${response.status}`);
                console.log(`Content-Type: ${response.headers['Content-Type']}`);
                console.log(`Body preview: ${response.body.slice(0, 300)}`);

                checkJsonResponse(endpoint.name, response);
            });
        }
    });
}