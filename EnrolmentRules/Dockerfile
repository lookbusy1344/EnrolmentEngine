# syntax=docker/dockerfile:1
#
# Container image for the EnrolmentRules.Web front-end.
#
# Build context MUST be the EnrolmentRules/ folder (this file's directory), not the
# monorepo root: the Web project pulls in sibling projects, the shared
# Directory.Build.props/.editorconfig, and the workflows/ + data/ content trees, all of
# which live under EnrolmentRules/.
#
#   docker build -t enrolment-web .
#   docker run --rm -p 8080:8080 enrolment-web
#
# See docs/deployment.md for hosting and OrbStack instructions.

# ---- build stage -----------------------------------------------------------
# The runtime stage is Ubuntu 24.04 "Noble" chiseled (distroless-style: no shell, no
# package manager, non-root by default). To change it, swap the runtime FROM tag — the
# SDK tag here can stay put, as the publish is RID-targeted rather than tied to the OS.
# See docs/deployment.md "Base image / OS".
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# The Web project references sibling projects, shared build props, and copies
# workflows/ + data/ as content — a partial copy is fragile, so copy the whole
# folder and let .dockerignore prune build junk (bin/obj/artifacts/tests).
COPY . .

# ReadyToRun pre-compiles IL to native code at publish time, taking JIT work off the
# cold-start path. It is RID-specific, so derive the RID from the build's target platform.
# PublishReadyToRun must be set on *restore* as well, or the crossgen2 package is absent
# and publish fails with NETSDK1094.
ARG TARGETARCH
RUN RID=linux-$(echo "${TARGETARCH:-amd64}" | sed 's/amd64/x64/') && echo "$RID" > /rid \
    && dotnet restore src/EnrolmentRules.Web/EnrolmentRules.Web.csproj -r "$RID" -p:PublishReadyToRun=true

# Framework-dependent publish; the aspnet runtime image supplies the framework (itself
# already R2R-compiled), so only the app's own assemblies need crossgen here. Trimming is
# deliberately NOT enabled: RulesEngine compiles the YAML lambda expressions by reflection
# at runtime, which a trimmed assembly graph would break.
# workflows/ and data/ are copied into /app via the csproj's CopyToPublishDirectory
# rules, so the app's ContentRootPath resolves them at runtime.
RUN dotnet publish src/EnrolmentRules.Web/EnrolmentRules.Web.csproj \
    -c Release -o /app --no-restore -r "$(cat /rid)" --self-contained false \
    -p:PublishReadyToRun=true

# ---- runtime stage ---------------------------------------------------------
# Chiseled: half the size of the Debian default tag, and a much smaller attack surface.
# It ships no ICU and so defaults to DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true — fine
# here, as the app formats and compares with InvariantCulture and ordinal rules throughout
# and does no timezone or localization work. Code needing ICU must move to -chiseled-extra.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app

COPY --from=build /app ./

# Kestrel listens on 8080 (the .NET image default; also Cloud Run's default PORT).
# Hosts that inject a different port: override ASPNETCORE_HTTP_PORTS (see docs).
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# Run as the non-root user baked into the .NET runtime image.
USER $APP_UID

# A RID-targeted publish emits an apphost, so invoke it directly rather than through the
# `dotnet` muxer (the chiseled image ships no shell to resolve one anyway).
ENTRYPOINT ["./EnrolmentRules.Web"]
