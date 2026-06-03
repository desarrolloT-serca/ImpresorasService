<?php

return [
    'api_url' => env('API_URL', 'http://localhost:5105'),
    'ping_interval_seconds' => (int) env('PING_INTERVAL_SECONDS', 30),
];
