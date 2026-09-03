FROM node:22-alpine AS build
WORKDIR /workspace
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
ARG VITE_API_BASE_URL=
ENV VITE_API_BASE_URL=${VITE_API_BASE_URL}
RUN npm run build

FROM nginx:1.27-alpine AS runtime
RUN rm -f /etc/nginx/conf.d/default.conf && \
    mkdir -p /tmp/nginx/client_temp /tmp/nginx/proxy_temp /tmp/nginx/fastcgi_temp /tmp/nginx/uwsgi_temp /tmp/nginx/scgi_temp && \
    chown -R nginx:nginx /tmp/nginx /var/cache/nginx /etc/nginx
COPY --chown=nginx:nginx deploy/nginx/nginx.conf /etc/nginx/nginx.conf
COPY --chown=nginx:nginx deploy/nginx/proxy_params /etc/nginx/proxy_params
COPY --chown=nginx:nginx deploy/nginx/start.sh /usr/local/bin/cute-oa-nginx
COPY --from=build --chown=nginx:nginx /workspace/dist/ /usr/share/nginx/html/
RUN chmod 0555 /usr/local/bin/cute-oa-nginx
USER nginx
EXPOSE 8080 8443
HEALTHCHECK --interval=15s --timeout=5s --start-period=10s --retries=4 CMD wget --quiet --spider --no-check-certificate https://127.0.0.1:8443/health || exit 1
CMD ["/usr/local/bin/cute-oa-nginx"]
