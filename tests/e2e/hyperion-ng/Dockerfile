# Hyperion.ng from the project's official APT repository, for the end-to-end tests
# (tests/Jellyfin.Plugin.HyperionGrabber.Core.Tests/EndToEnd, docs/development/testing.md).
# Throwaway only: the tests reconfigure the LED layout and add an instance.
#
#   docker build -t hyperion-ng-e2e tests/e2e/hyperion-ng
#   docker run -d --rm --name hyperion-ng-e2e -p 127.0.0.1:19400:19400 -p 127.0.0.1:19444:19444 hyperion-ng-e2e
#
# The repository keeps only the current release, so this always tests the Hyperion.ng users install today.
FROM debian:bookworm-slim

RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl gpg \
    && curl -fsSL https://releases.hyperion-project.org/hyperion.pub.key \
        | gpg --dearmor -o /usr/share/keyrings/hyperion.pub.gpg \
    && echo "deb [signed-by=/usr/share/keyrings/hyperion.pub.gpg] https://apt.releases.hyperion-project.org/ bookworm main" \
        > /etc/apt/sources.list.d/hyperion.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends hyperion \
    && rm -rf /var/lib/apt/lists/* \
    && useradd --system --create-home hyperion \
    && dpkg-query --show hyperion

USER hyperion

# No display in a container.
ENV QT_QPA_PLATFORM=offscreen

# 19400: FlatBuffers server (what the plugin talks to); 19444: JSON-RPC (what the tests inspect); 8090: web UI.
EXPOSE 19400 19444 8090

# Line-buffered, so `docker logs` shows what happened up to the moment it is called.
ENTRYPOINT ["stdbuf", "-oL", "-eL", "hyperiond", "--service", "--info", "--userdata", "/home/hyperion/.hyperion"]
