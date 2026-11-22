# Build context: ChubbClaims_FE/
# Stage 1 builds the production bundle; stage 2 serves it with nginx, which also forwards /api and
# /hubs to the API container, so the browser talks to one origin (as with the dev server's proxy).
FROM node:24-alpine AS build
WORKDIR /src

# Install first (cached layer as long as the lock file doesn't change).
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund

COPY . .
RUN npm run build

FROM nginx:1.28-alpine AS runtime
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/dist/ChubbClaims/browser /usr/share/nginx/html
EXPOSE 80
HEALTHCHECK --interval=10s --timeout=3s --retries=3 CMD wget -q --spider http://127.0.0.1/ || exit 1
