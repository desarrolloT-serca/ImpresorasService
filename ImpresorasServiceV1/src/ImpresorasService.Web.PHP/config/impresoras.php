<?php

return [
    'api_url' => env('API_URL', 'http://localhost:5105'),
    'ping_interval_seconds' => (int) env('PING_INTERVAL_SECONDS', 30),
    // Migracion dashboard: false = solo legacy; true = registrar diffs KPI vs overview (sin cambiar UI).
    'dashboard_log_overview_diff' => filter_var(env('DASHBOARD_LOG_OVERVIEW_DIFF', false), FILTER_VALIDATE_BOOL),
];
