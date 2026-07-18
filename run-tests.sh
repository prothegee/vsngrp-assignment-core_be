#!/bin/bash
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")"

dotnet test VsngrpCoreBe.slnx

./run-tests-db-integrity.sh
