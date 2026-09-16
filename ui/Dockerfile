# --- Build stage ---
FROM node:20-alpine AS build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
RUN npx ng build

# --- Serve stage ---
FROM nginx:1.27-alpine
COPY --from=build /app/dist/ui/browser /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf

# AZURE MIGRATION: config.template.json ships inside the image (copied to
# dist root by the Angular build's public/ assets glob — see angular.json);
# docker-entrypoint.sh renders it into config.json using this container's
# env vars every time it starts, so the same image works locally and in
# Azure — see docker-entrypoint.sh for why.
COPY docker-entrypoint.sh /docker-entrypoint.sh
RUN chmod +x /docker-entrypoint.sh

EXPOSE 80
ENTRYPOINT ["/docker-entrypoint.sh"]
