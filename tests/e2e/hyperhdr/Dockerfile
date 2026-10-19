# HyperHDR from the project's official GitHub release, for the end-to-end tests
# (tests/Jellyfin.Plugin.HyperionGrabber.Core.Tests/EndToEnd, docs/development/testing.md).
# Throwaway only: the tests reconfigure the LED layout and add an instance.
#
#   docker build -t hyperhdr-e2e tests/e2e/hyperhdr
#   docker run -d --rm --name hyperhdr-e2e -p 127.0.0.1:19400:19400 -p 127.0.0.1:19444:19444 hyperhdr-e2e
#
# HyperHDR's APT repository (https://awawa-dev.github.io) keeps only the latest release, so this pins the release's
# Debian bookworm package instead and checks it against the SHA-256 GitHub lists for that release asset. To test
# another release, change all three arguments.
FROM debian:bookworm-slim

ARG HYPERHDR_TAG=v22.0.0.0
ARG HYPERHDR_DEB=HyperHDR-22.0.0.bookworm-x86_64.deb
ARG HYPERHDR_SHA256=4b2a9278e4df7924348a952c9761abbf6b149002ed720cf4058c6cd3d61be0eb

RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl \
    && curl -fsSL -o "/tmp/${HYPERHDR_DEB}" "https://github.com/awawa-dev/HyperHDR/releases/download/${HYPERHDR_TAG}/${HYPERHDR_DEB}" \
    && echo "${HYPERHDR_SHA256}  /tmp/${HYPERHDR_DEB}" | sha256sum --check --strict - \
    && apt-get install -y --no-install-recommends "/tmp/${HYPERHDR_DEB}" \
    && rm -rf "/tmp/${HYPERHDR_DEB}" /var/lib/apt/lists/* \
    && useradd --system --create-home hyperhdr \
    && dpkg-query --show hyperhdr

USER hyperhdr

# 19400: FlatBuffers server (what the plugin talks to); 19444: JSON-RPC (what the tests inspect); 8090: web UI.
EXPOSE 19400 19444 8090

# --service: console service without a tray icon. Line-buffered, so `docker logs` shows what happened up to the
# moment it is called.
ENTRYPOINT ["stdbuf", "-oL", "-eL", "hyperhdr", "--service", "--userdata", "/home/hyperhdr/.hyperhdr"]
