# Imagen de la web. El build de Vite produce estáticos; lo que queda en la imagen final es
# nginx sirviéndolos, sin Node ni `node_modules`.
FROM node:22-alpine AS construccion
WORKDIR /origen

# `npm ci` respeta el lockfile al pie de la letra: la imagen no puede resolver una versión
# distinta de la que se probó en el CI.
COPY package.json package-lock.json ./
RUN npm ci

COPY . .
# `npm run build` ejecuta `tsc --noEmit` antes de Vite: el cliente está tipado desde el
# OpenAPI, así que un cambio de contrato en la Api rompe aquí y no en una pantalla.
RUN npm run build

FROM nginx:1.31-alpine AS final

COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=construccion /origen/dist /usr/share/nginx/html

EXPOSE 8080

HEALTHCHECK --interval=10s --timeout=3s --start-period=5s --retries=6 \
    CMD wget --quiet --spider --tries=1 http://127.0.0.1:8080/ || exit 1
