# The web app's production build, which the edge's image carries in ./wwwroot and serves (src/AppHost/Wiring/WebBuild.cs,
# docs/adr/0005). The context is the pnpm workspace, web/, so its lockfile is in it. The image is only built, never run:
# its last stage holds the build output alone, where Aspire copies it from (/app/dist).
FROM node:22.23.5-slim AS build
RUN corepack enable
WORKDIR /web
COPY package.json pnpm-lock.yaml pnpm-workspace.yaml ./
COPY app/package.json app/
RUN --mount=type=cache,target=/root/.local/share/pnpm/store pnpm install --frozen-lockfile
COPY app/ app/
RUN pnpm --filter app build

FROM scratch
COPY --from=build /web/app/dist /app/dist
