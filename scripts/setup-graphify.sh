#!/usr/bin/env bash
# Activa graphify en este repo: hook PreToolUse de Claude Code + hooks de git.
# Ejecutar UNA vez desde la raíz del repo, en tu máquina. Requiere Python 3.10+.
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

command -v graphify >/dev/null || python3 -m pip install --user graphifyy

graphify claude install   # sección en CLAUDE.md + PreToolUse hook en .claude/settings.json
graphify hook install     # post-commit / post-checkout / post-merge (actualiza el grafo sin LLM)
graphify hook status

echo "Listo. Con código en el repo, abre Claude Code y ejecuta: /graphify ."
