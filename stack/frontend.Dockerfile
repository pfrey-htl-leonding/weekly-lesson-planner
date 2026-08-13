FROM node:24-bookworm-slim AS build
WORKDIR /source

COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci

COPY frontend/ ./
RUN npm run build

FROM nginx:1.28-alpine AS runtime
COPY stack/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /source/dist/frontend/browser /usr/share/nginx/html

EXPOSE 80

