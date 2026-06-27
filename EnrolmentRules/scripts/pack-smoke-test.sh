#!/usr/bin/env bash
#
# Phase 4 packaging smoke test: pack the libraries to a local feed, install
# EnrolmentRules.Engine into a throwaway console app from that feed, and prove
# the packaged public API (EnrolmentEngine.CreateAsync) loads and evaluates a
# student. Guards the packaging config (PackageId, versions, inter-package
# ProjectReference->PackageReference rewrite) that the unit suite cannot reach.
#
# Requires the .NET SDK and network access (transitive deps restore from nuget.org).
# Run from anywhere; paths are resolved relative to this script.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo="$(cd "$here/.." && pwd)"
base="$(grep -oE '<Version>[^<]+</Version>' "$repo/Directory.Build.props" | sed -E 's/<\/?Version>//g')"
# A unique pre-release suffix per run so the global NuGet cache (keyed by version) can never
# serve a stale copy in place of the freshly packed one.
version="$base-smoke$(date +%Y%m%d%H%M%S)"

work="$(mktemp -d)"
feed="$work/feed"
app="$work/app"
trap 'rm -rf "$work"' EXIT

echo "==> packing libraries ($version) to local feed"
dotnet pack "$repo/EnrolmentRules.slnx" -c Release -o "$feed" -p:Version="$version" -v quiet

echo "==> scaffolding throwaway consumer app"
mkdir -p "$app"
cat > "$app/nuget.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF

cat > "$app/app.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="EnrolmentRules.Engine" Version="$version" />
  </ItemGroup>
</Project>
EOF

cat > "$app/Program.cs" <<'EOF'
using EnrolmentRules.Domain;
using EnrolmentRules.Engine;

var workflows = args[0];
var data = args[1];
Console.WriteLine($"workflows={workflows}");
Console.WriteLine($"data={data}");

var engine = await EnrolmentEngine.CreateAsync(workflows, data, DateOnly.FromDateTime(DateTime.Today));

var student = new StudentInput(
    "SMOKE",
    new Dictionary<string, int>
    {
        ["english_language"] = 7, ["maths"] = 7, ["physics"] = 6,
        ["chemistry"] = 6, ["biology"] = 6,
    },
    []);

var result = await engine.EvaluateAsync(student);
Console.WriteLine($"eligible={result.Eligible} recommendations={result.Recommendations.Count}");
return result.Eligible ? 0 : 1;
EOF

echo "==> restoring + running consumer against the packed Engine"
dotnet run --project "$app" -c Release -- "$repo/workflows" "$repo/data"

echo "==> PASS: packaged Engine installs from the feed and evaluates a student"
