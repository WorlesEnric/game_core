"""Fail-closed path and data policy shared by slot construction and verification."""
from pathlib import Path
import re

DATA = {'.png', '.jpg', '.jpeg', '.webp', '.wav', '.ogg', '.mp3', '.json', '.txt', '.csv', '.fbx', '.glb', '.gltf'}
FORBIDDEN_DIRS = {'editor', 'plugins', 'tests', 'library', 'temp', 'logs', 'obj', 'bin'}


def relative(value: str) -> str:
    if (not isinstance(value, str) or not value or
            any(c in value for c in '\\:\x00<>"&*?') or
            any(p in ('', '.', '..') for p in value.split('/')) or
            any(ord(c) < 32 for c in value)):
        raise ValueError('stage_path_invalid: expected a normalized relative path')
    return value


def contained(root: Path, value: str) -> Path:
    relative(value)
    path = root
    if root.is_symlink():
        raise ValueError('stage_path_link: root is a link')
    for part in value.split('/'):
        path = path / part
        if path.is_symlink():
            raise ValueError('stage_path_link: links are refused')
    if not path.resolve().is_relative_to(root.resolve()):
        raise ValueError('stage_path_escape: outside root')
    return path


def data_path(value: str, settings=False) -> None:
    relative(value)
    parts = value.split('/')
    if parts[0] != 'Assets' or len(parts) < 2 or any(p.lower() in FORBIDDEN_DIRS for p in parts):
        raise ValueError('stage_input_executable: forbidden input directory')
    ext = Path(value).suffix.lower()
    if ext not in DATA and not (settings and value.startswith('Assets/Settings/') and ext == '.asset'):
        raise ValueError('stage_input_executable: only media, plain data and mesh files are allowed')


def validate_meta(path: Path) -> None:
    text = path.read_text(encoding='utf-8')
    # Only standard data importers. ScriptedImporter can invoke arbitrary editor hooks.
    importers = re.findall(r'^([A-Za-z]+Importer):', text, re.M)
    if len(importers) != 1 or importers[0] not in {
            'DefaultImporter', 'TextureImporter', 'AudioImporter', 'ModelImporter',
            'TextScriptImporter', 'NativeFormatImporter'}:
        raise ValueError('stage_importer_forbidden: unsupported importer')
    if re.search(r'^[ \t]*(externalObjects|userData):[ \t]*[^{}\s]', text, re.M):
        raise ValueError('stage_importer_forbidden: custom importer settings')
