#!/bin/sh
# AZURE MIGRATION: new file.
#
# Renders /config.json from config.template.json using whatever
# ORIGINATION_URL / UNDERWRITING_URL / FUNDING_URL / SERVICING_URL env vars
# this container was started with, then hands off to nginx. This is what
# lets the exact same built image serve the right backend URLs in local
# docker-compose (http://localhost:5100 etc, since the browser and the
# containers share the host there) and in Azure Container Apps (each
# service's real public HTTPS FQDN) without rebuilding the image — only the
# env vars differ, set in docker-compose.yml locally and in
# infra/main.bicep for Azure.
set -e

envsubst '${ORIGINATION_URL} ${UNDERWRITING_URL} ${FUNDING_URL} ${SERVICING_URL}' \
  < /usr/share/nginx/html/config.template.json \
  > /usr/share/nginx/html/config.json

exec nginx -g 'daemon off;'
