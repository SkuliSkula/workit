#!/bin/sh
# Inject the API base URL from the API_BASE_URL environment variable
# into the appsettings.json served by nginx.
if [ -n "$API_BASE_URL" ]; then
    sed -i "s|__API_BASE_URL__|${API_BASE_URL}|g" /usr/share/nginx/html/appsettings.json
fi
