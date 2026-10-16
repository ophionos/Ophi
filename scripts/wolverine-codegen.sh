#!/usr/bin/env bash
# Regenerate the Wolverine handler code that the split API and worker hosts load in
# TypeLoadMode.Static (src/Ophi.Api/Internal/Generated, src/Ophi.Worker/Internal/Generated).
# Run it after adding, removing, or changing a handler, its validator, or a dependency it injects,
# and commit the result. CI runs it and fails when the checked-in code differs.
#
#   scripts/wolverine-codegen.sh [configuration]     default: Debug; builds first
#
# The hosts are configured as in production (durable Postgres transport), but nothing connects:
# codegen only builds the host, and Program.cs skips migrations under the codegen command.
set -euo pipefail

cd "$(dirname "$0")/.."
configuration="${1:-Debug}"

export ASPNETCORE_ENVIRONMENT=Production DOTNET_ENVIRONMENT=Production
export POSTGRES_CONNECTION="Host=127.0.0.1;Port=1;Database=codegen;Username=codegen;Password=codegen"
unset ENABLE_WORKER ConnectionStrings__Postgres

dotnet build src/Ophi.Api --configuration "$configuration" --nologo --verbosity quiet

for project in Ophi.Api Ophi.Worker; do
	rm -rf "src/$project/Internal/Generated"
	dotnet run --no-build --configuration "$configuration" --project "src/$project" -- codegen write > /dev/null
	echo "$project: $(find "src/$project/Internal/Generated" -name '*.cs' | wc -l) generated files"
done
