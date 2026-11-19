#!/usr/bin/env bash
# Smoke test of the PUBLISHED artifact: check the .nupkg holds only lib/ (+ README and NuGet metadata), install it
# from a local folder feed into an empty console project and run the README "Testing" example (offline, against
# MockOnlineSim) on net8.0, and on .NET Framework 4.7.2 where a runner exists (Windows natively, elsewhere Mono).
# Usage: tests/smoke-artifact.sh [feed-dir]   (default artifacts/package; run `dotnet pack -o artifacts/package` first)
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
feed="$(cd "${1:-$root/artifacts/package}" && pwd)"
nupkg="$(find "$feed" -maxdepth 1 -name 'OnlineSim.Api.*.nupkg' ! -name '*.snupkg' | head -n 1)"
[ -n "$nupkg" ] || { echo "no OnlineSim.Api .nupkg in $feed" >&2; exit 1; }
version="$(basename "$nupkg" .nupkg)"
version="${version#OnlineSim.Api.}"
[ -f "${nupkg%.nupkg}.snupkg" ] || { echo "missing symbol package next to $nupkg" >&2; exit 1; }

# 1. Package contents.
expected='OnlineSim.Api.nuspec
README.md
[Content_Types].xml
_rels/.rels
lib/net8.0/OnlineSim.Api.dll
lib/net8.0/OnlineSim.Api.xml
lib/netstandard2.0/OnlineSim.Api.dll
lib/netstandard2.0/OnlineSim.Api.xml
package/services/metadata/core-properties/*.psmdcp'
actual="$(unzip -Z1 "$nupkg" | sed -E 's#core-properties/[^/]+\.psmdcp$#core-properties/*.psmdcp#' | LC_ALL=C sort)"
if [ "$actual" != "$(printf '%s\n' "$expected" | LC_ALL=C sort)" ]; then
  echo "unexpected package contents:" >&2
  echo "$actual" >&2
  exit 1
fi
echo "packed $(basename "$nupkg"): contents ok"

# 2. An empty console app inside the ignored artifacts/ dir, cut off from the repo's build settings.
app="$root/artifacts/smoke/app"
rm -rf "$root/artifacts/smoke"
mkdir -p "$app"
echo '<Project />' > "$app/Directory.Build.props"
echo '<Project />' > "$app/Directory.Build.targets"

frameworks="net8.0"
netfx=""
case "$(uname -s)" in
  MINGW* | MSYS* | CYGWIN*) netfx="native" ;;
  *) if command -v mono > /dev/null 2>&1; then netfx="mono"; fi ;;
esac
[ -n "$netfx" ] && frameworks="net8.0;net472"

cat > "$app/nuget.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="local"><package pattern="OnlineSim.Api" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
EOF

cat > "$app/SmokeApp.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFrameworks>$frameworks</TargetFrameworks>
    <LangVersion>12.0</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <!-- A fresh package folder: never a stale OnlineSim.Api from the global cache. -->
    <RestorePackagesPath>\$(MSBuildThisFileDirectory)../packages</RestorePackagesPath>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="OnlineSim.Api" Version="$version" />
  </ItemGroup>
</Project>
EOF

# 3. The README "Testing" example, taken from the README inside the package.
unzip -p "$nupkg" README.md | awk '
  /^## Testing/ { section = 1; next }
  section && /^## / { exit }
  section && /^```csharp/ { code = 1; next }
  code && /^```/ { exit }
  code { print }
' > "$app/Program.cs"
[ -s "$app/Program.cs" ] || { echo 'README.md has no "## Testing" csharp example' >&2; exit 1; }

dotnet build "$app/SmokeApp.csproj" -c Release
dotnet "$app/bin/Release/net8.0/SmokeApp.dll"
echo "README \"Testing\" example on net8.0: ok"

case "$netfx" in
  native) "$app/bin/Release/net472/SmokeApp.exe" && echo "README \"Testing\" example on .NET Framework 4.7.2: ok" ;;
  mono) mono "$app/bin/Release/net472/SmokeApp.exe" && echo "README \"Testing\" example on .NET Framework 4.7.2 (Mono $(mono --version | head -n 1 | sed -E 's/.*version ([^ ]+).*/\1/')): ok" ;;
  *) echo "no .NET Framework runner here (needs Windows or Mono): net472 skipped" ;;
esac
