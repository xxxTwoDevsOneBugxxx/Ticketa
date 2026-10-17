
import http from 'k6/http';
import { check, group, sleep } from 'k6';

const BASE_URL = (
    __ENV.BASE_URL || 'https://localhost:7225'
).replace(/\/+$/, '');

// Load test accounts from test-users.json or fallback to TEST_USERS_JSON env var.
let TEST_USERS = [];
try {
    const fileContent = open('./test-users.json');
    TEST_USERS = JSON.parse(fileContent);
} catch (_) {
    try {
        TEST_USERS = JSON.parse(__ENV.TEST_USERS_JSON || '[]');
    } catch (e) {
        TEST_USERS = [];
    }
}


export const options = {
    scenarios: {
        baseline: {
            executor: 'constant-vus',
            vus: 5,
            duration: '5m',
            gracefulStop: '10s',
        },
    },

    thresholds: {
        checks: ['rate>=0.99'],
        http_req_failed: ['rate<0.01'],
        http_req_duration: [
            'p(95)<500',
            'p(99)<1000',
        ],
    },
};

// These variables are maintained independently by each VU.
let accessToken = null;
let loginAttempted = false;

function parseJson(response) {
    try {
        return JSON.parse(response.body);
    } catch (_) {
        return null;
    }
}

function validateResponse(name, response) {
    const body = parseJson(response);

    check(response, {
        [`${name}: HTTP 200`]: (r) => r.status === 200,
        [`${name}: valid JSON`]: () => body !== null,
    });

    return body;
}

function get(path, name, authenticated = false) {
    const headers = {
        Accept: 'application/json',
    };

    if (authenticated && accessToken) {
        headers.Authorization = `Bearer ${accessToken}`;
    }

    const endpointTags = {
        'Login': 'login',
        'Movies paginated': 'movies_list',
        'Movies coming soon': 'movies_coming_soon',
        'Movies top booked': 'movies_top_booked',
    };

    const response = http.get(`${BASE_URL}${path}`, {
        headers,
        tags: {
            endpoint: endpointTags[name] || 'unknown',
        },
        timeout: '10s',
    });

    return validateResponse(name, response);
}

function loginOncePerVu() {
    if (loginAttempted) {
        return;
    }

    loginAttempted = true;

    if (TEST_USERS.length === 0) {
        return;
    }

    // Prefer one distinct test account per VU.
    // If there are fewer accounts than VUs, accounts will be reused.
    const account = TEST_USERS[(__VU - 1) % TEST_USERS.length];

    if (!account || !account.email || account.password === 'YOUR_PASSWORD_HERE') {
        return;
    }

    const response = http.post(
        `${BASE_URL}/api/auth/login`,
        JSON.stringify({
            email: account.email,
            password: account.password,
        }),
        {
            headers: {
                'Content-Type': 'application/json',
                Accept: 'application/json',
            },
            tags: { endpoint: 'login' },
            timeout: '10s',
        },
    );

    const body = validateResponse('Login', response);

    check(body || {}, {
        'Login: access token received': (r) =>
            typeof r.accessToken === 'string' &&
            r.accessToken.length > 0,
    });

    if (body && typeof body.accessToken === 'string') {
        accessToken = body.accessToken;
    }
}

export default function () {
    group('Ticketa API baseline without NowShowing', () => {
        // Each VU logs in once, then reuses its access token.
        loginOncePerVu();

        sleep(1 + Math.random() * 2);

        // Browse the paginated movie catalog.
        get(
            '/api/movies?page=1&pageSize=10',
            'Movies paginated',
        );

        sleep(1 + Math.random() * 2);

        // Occasionally browse other catalog sections.
        if (Math.random() < 0.3) {
            get(
                '/api/movies/coming-soon',
                'Movies coming soon',
            );
        }

        if (Math.random() < 0.3) {
            get(
                '/api/movies/top-booked?count=6',
                'Movies top booked',
            );
        }
    });

    // Pause before the next simulated user journey.
    sleep(2 + Math.random() * 3);
}