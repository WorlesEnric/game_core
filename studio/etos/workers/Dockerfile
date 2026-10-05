# Offline schema checking layer over the existing gc-mechanic SDK/cache image.
ARG MECHANIC_IMAGE=localhost/gc-mechanic-base:current
FROM ${MECHANIC_IMAGE}
USER root
RUN apt-get update && apt-get install -y --no-install-recommends python3-jsonschema \
    && rm -rf /var/lib/apt/lists/* /var/cache/apt/archives/*.deb
COPY check-output.py gc-mechanic.md /opt/gamecore-worker/
COPY schemas/ /opt/gamecore-worker/schemas/
RUN python3 -c 'from jsonschema import Draft202012Validator' \
    && chmod -R a+rX /opt/gamecore-worker
