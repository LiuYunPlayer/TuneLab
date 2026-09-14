"""Wire tagged values for PropertyObject maps (scripting/wire spec, §5.3).

Host↔plugin property maps ride as JSON objects whose VALUES are tagged:
  {"b": bool} | {"n": number} | {"s": string} | {"o": {...}} | {"a": [...]} | null(=Null)
The walker finds property maps by the key "properties" (camelCase, host naming policy) and
converts to/from plain Python natives so plugin authors never see the tags.
"""
import math


def encode_value(v):
    if isinstance(v, bool):
        return {"b": v}
    if v is None:
        return None
    if isinstance(v, (int, float)):
        if isinstance(v, float) and math.isnan(v):
            return {"n": "NaN"}          # 规范形态：字符串字面量（宿主 JsonDocument 不接受裸 NaN token）
        if isinstance(v, float) and math.isinf(v):
            return {"n": "Infinity" if v > 0 else "-Infinity"}
        return {"n": float(v)}
    if isinstance(v, str):
        return {"s": v}
    if isinstance(v, dict):
        return {"o": {k: encode_value(x) for k, x in v.items()}}
    if isinstance(v, (list, tuple)):
        return {"a": [encode_value(x) for x in v]}
    raise TypeError("cannot encode property value of type " + type(v).__name__)


def decode_value(v):
    if v is None:
        return None
    if isinstance(v, bool):
        return v
    if isinstance(v, (int, float)):
        return float(v)
    if isinstance(v, str):
        return v
    if isinstance(v, list):
        return [decode_value(x) for x in v]
    if isinstance(v, dict):
        if len(v) == 1:
            (tag, payload), = v.items()
            if tag in ("b", "n", "s", "o", "a"):
                if tag == "o":
                    return decode_map(payload)
                if tag == "a":
                    return [decode_value(x) for x in payload]
                if tag == "n" and isinstance(payload, str):
                    return {"NaN": float("nan"), "Infinity": float("inf"), "-Infinity": float("-inf")}[payload]
                return payload
        # single-key dicts with a non-reserved key, or multi-key dicts: raw nested property map
        return decode_map(v)
    raise TypeError("cannot decode property value")


def decode_map(m):
    return {k: decode_value(x) for k, x in m.items()}


def _special(v):
    return isinstance(v, float) and (math.isnan(v) or math.isinf(v))


def _special_str(v):
    if math.isnan(v):
        return "NaN"
    return "Infinity" if v > 0 else "-Infinity"


def walk_encode(node):
    """Encode a project tree on the way OUT to the host.

    - key \"properties\" (dict) → every value becomes a tagged property value
    - any bare NaN/±Infinity float anywhere → its string literal (wire spec: NaN/±Infinity
      are ALWAYS JSON strings — illegal bare tokens must never hit the pipe)
    """
    if isinstance(node, list):
        return [walk_encode(x) for x in node]
    if isinstance(node, tuple):
        return [walk_encode(x) for x in node]
    if isinstance(node, dict):
        out = {}
        for k, val in node.items():
            if k == "properties" and isinstance(val, dict):
                out[k] = {pk: encode_value(pv) for pk, pv in val.items()}
            else:
                out[k] = walk_encode(val)
        return out
    if _special(node):
        return _special_str(node)
    return node


def walk_decode(node):
    """Decode property maps inside a project tree coming FROM the host into plugin-plain form."""
    if isinstance(node, list):
        return [walk_decode(x) for x in node]
    if isinstance(node, dict):
        out = {}
        for k, val in node.items():
            if k == "properties" and isinstance(val, dict):
                out[k] = decode_map(val)
            else:
                out[k] = walk_decode(val)
        return out
    return node
