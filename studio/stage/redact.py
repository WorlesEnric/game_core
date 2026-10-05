"""D9 wire redaction contract; fixed messages never include original secret values."""
import json
import re

TOKEN = re.compile(r'\b(?:et[kpta]_[A-Za-z0-9_.~+/=-]+|sk-[A-Za-z0-9_-]+)|\bBearer\s+[^\s"\',;<>]+', re.I)
KEY = re.compile(r'key|token|secret', re.I)
PAIR = re.compile(r'("(?:[^"\\]|\\.)*(?:key|token|secret)(?:[^"\\]|\\.)*"\s*:\s*)("(?:[^"\\]|\\.)*"|[^,}\]\r\n]+)', re.I)


def redact(text):
    def clean(value):
        if isinstance(value, dict):
            return {k: '[REDACTED]' if KEY.search(k) else clean(v) for k, v in value.items()}
        if isinstance(value, list):
            return [clean(v) for v in value]
        return TOKEN.sub('[REDACTED]', value) if isinstance(value, str) else value
    try:
        return json.dumps(clean(json.loads(text)), ensure_ascii=False) + '\n'
    except ValueError:
        return TOKEN.sub('[REDACTED]', PAIR.sub(r'\1"[REDACTED]"', text))
