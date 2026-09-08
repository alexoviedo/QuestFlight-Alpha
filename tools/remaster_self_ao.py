"""Bounded self-occlusion staging for accepted Unity traffic meshes.

CPU NumPy ray casting, using existing geometry only. This does not reconstruct
high-poly detail and never includes a ground plane or another vehicle instance.
Only an unused, originally opaque UNorm8 color alpha channel can be staged.
"""
from __future__ import annotations

import hashlib
from pathlib import Path
import re

import numpy as np
import yaml


def read_mesh(path, require_opaque=True):
    text = path.read_text(encoding="utf-8")
    mesh = yaml.safe_load("\n".join(text.splitlines()[3:]))["Mesh"]
    data = mesh["m_VertexData"]
    channels = data["m_Channels"]
    active = [c for c in channels if c["dimension"]]
    if any(c["stream"] != 0 or c["format"] not in (0, 2) for c in active):
        raise ValueError("Only audited single-stream Float32/UNorm8 meshes are supported")
    stride = max(c["offset"] + c["dimension"] * (4 if c["format"] == 0 else 1) for c in active)
    count = data["m_VertexCount"]
    blob = bytes.fromhex(data["_typelessdata"])
    if len(blob) != count * stride:
        raise ValueError("Vertex payload/stride mismatch")
    for index in (0, 1):
        if channels[index]["format"] != 0 or channels[index]["dimension"] != 3:
            raise ValueError("Expected Float32 positions/normals")
    color = channels[3]
    if color["format"] != 2 or color["dimension"] != 4:
        raise ValueError("Expected an existing UNorm8 RGBA stream")
    def floats(index):
        return np.ndarray((count, 3), dtype="<f4", buffer=blob,
                          offset=channels[index]["offset"], strides=(stride, 4)).copy()
    colors = np.ndarray((count, 4), dtype=np.uint8, buffer=blob,
                        offset=color["offset"], strides=(stride, 1)).copy()
    if require_opaque and not np.all(colors[:, 3] == 255):
        raise ValueError("Alpha is occupied or already baked; use the preserved original")
    if any(s["topology"] != 0 or s["baseVertex"] != 0 for s in mesh["m_SubMeshes"]):
        raise ValueError("Unexpected topology/base vertex")
    indices = np.frombuffer(bytes.fromhex(mesh["m_IndexBuffer"]),
                            dtype="<u4" if mesh["m_IndexFormat"] else "<u2").astype(int)
    if len(indices) % 3 or indices.max() >= count:
        raise ValueError("Invalid triangle indices")
    positions, normals = floats(0), floats(1)
    if not np.isfinite(positions).all() or not np.isfinite(normals).all():
        raise ValueError("Nonfinite mesh")
    return text, blob, stride, color["offset"], positions, normals, colors, indices.reshape(-1, 3)


def accessibility(positions, normals, triangles, radius=0.4, samples=48):
    """Cosine-weighted hemisphere visibility; two-sided finite-distance triangles."""
    lengths = np.linalg.norm(normals, axis=1)
    if np.any(lengths < 0.5):
        raise ValueError("Invalid normals")
    normals = normals / lengths[:, None]
    u = (np.arange(samples) + 0.5) / samples
    phi = np.arange(samples) * (np.pi * (3 - np.sqrt(5)))
    hemi = np.stack((np.sqrt(u) * np.cos(phi), np.sqrt(u) * np.sin(phi), np.sqrt(1 - u)), axis=1)
    a, b, c = triangles[:, 0], triangles[:, 1], triangles[:, 2]
    e1, e2 = b - a, c - a
    result = []
    for start in range(0, len(positions), 16):
        n = normals[start:start + 16]
        axis = np.tile([0., 1., 0.], (len(n), 1))
        axis[np.abs(n[:, 1]) > 0.9] = [1., 0., 0.]
        t = np.cross(axis, n)
        t /= np.linalg.norm(t, axis=1)[:, None]
        bitangent = np.cross(n, t)
        directions = (t[:, None, :] * hemi[None, :, 0:1] +
                      bitangent[:, None, :] * hemi[None, :, 1:2] +
                      n[:, None, :] * hemi[None, :, 2:3]).reshape(-1, 3)
        origins = np.repeat(positions[start:start + len(n)] + n * 0.002, samples, axis=0)
        p = np.cross(directions[:, None, :], e2[None, :, :])
        det = np.einsum("rtk,tk->rt", p, e1)
        inv = np.divide(1., det, out=np.zeros_like(det), where=np.abs(det) > 1e-9)
        delta = origins[:, None, :] - a
        bary_u = np.einsum("rtk,rtk->rt", delta, p) * inv
        q = np.cross(delta, e1)
        bary_v = np.einsum("rk,rtk->rt", directions, q) * inv
        distance = np.einsum("tk,rtk->rt", e2, q) * inv
        hits = (np.abs(det) > 1e-9) & (bary_u >= 0) & (bary_v >= 0) & (bary_u + bary_v <= 1) & (distance > 0.001) & (distance < radius)
        result.extend(1. - hits.any(axis=1).reshape(len(n), samples).mean(axis=1))
    return np.array(result)


def stage(path: Path, output: Path):
    text, blob, stride, offset, positions, normals, colors, indices = read_mesh(path)
    visible = accessibility(positions, normals, positions[indices])
    alpha = np.rint((0.85 + 0.15 * visible) * 255).astype(np.uint8)
    # Bake the material eligibility mask too, avoiding a runtime classification
    # mask/strength lerp. Only body and trim receive this restrained treatment.
    code = colors[:, :3] >= 128
    protected = ~(code[:, 0] & ~code[:, 2])
    alpha[protected] = 255
    changed = bytearray(blob)
    for index, value in enumerate(alpha):
        changed[index * stride + offset + 3] = int(value)
    # Exact byte assertion protects every other vertex attribute.
    restored = bytearray(changed)
    restored[offset + 3::stride] = blob[offset + 3::stride]
    if bytes(restored) != blob:
        raise AssertionError("Protected vertex bytes changed")
    modified, count = re.subn(r"(_typelessdata: )([0-9a-fA-F]+)",
                             lambda m: m[1] + changed.hex(), text)
    if count != 1:
        raise ValueError("Unexpected serialized vertex payload")
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(modified, encoding="utf-8", newline="\n")
    return {"source": str(path), "source_sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
            "output": str(output), "sha256": hashlib.sha256(output.read_bytes()).hexdigest(),
            "backend": "CPU NumPy finite-distance self-raycast", "radius_m": 0.4, "samples": 48,
            "vertex_count": len(positions), "changed_alpha_count": int(np.sum(alpha != 255)),
            "alpha_min": int(alpha.min()), "protected_vertex_bytes_equal": True,
            "vertex_stream_bytes_delta": 0, "status": "staged-not-integrated"}
