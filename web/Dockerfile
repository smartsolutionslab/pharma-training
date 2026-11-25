# The web app's production build, which the edge's image carries in ./wwwroot and serves (src/AppHost/Wiring/WebBuild.cs,
# docs/adr/0005). The context is the pnpm workspace, web/, so its lockfile is in it. The image is only built, never run:
# its last stage holds the build output alone, where Aspire copies it from (/app/dist).
FROM node:26.13.0-slim@sha256:5adad64f9a1fca2d51972caa65983634c8a774eb300d72b20f7f2a0f60368072 AS build
RUN corepack enable
WORKDIR /web
COPY package.json pnpm-lock.yaml pnpm-workspace.yaml ./
COPY app/package.json app/
RUN --mount=type=cache,target=/root/.local/share/pnpm/store pnpm install --frozen-lockfile
COPY app/ app/
RUN pnpm --filter app build

FROM scratch
COPY --from=build /web/app/dist /app/dist
