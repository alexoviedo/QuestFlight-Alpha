# Kloppenheim 03 Pure Sky source and license

The committed HDR derivative comes from [Kloppenheim 03 (Pure Sky)](https://polyhaven.com/a/kloppenheim_03_puresky), authored by Greg Zaal with pure-sky edits by Jarod Guest and distributed by Poly Haven under [CC0 1.0](https://polyhaven.com/license).

The official 4096 × 2048 Radiance HDR download was retrieved from Poly Haven’s API-listed download endpoint on 2026-07-16. Its MD5 matches the official API value `09c9e57a51e6eed81ac70e194d58eab2`; its SHA-256 is `f906a7334e5d57007fa5516bd141df2ada8fdc3d2ba0c8bdf445d64f1bbb010a`. That 19,682,492-byte source remains outside Git beneath the milestone artifact root.

`Kloppenheim03_PureSky_2K.hdr` is a project-generated 2048 × 1024 derivative. The reproducible tool decodes RGBE to linear radiance, averages exact 2 × 2 source footprints, and re-encodes Radiance scanline RLE without tone mapping or clipping. Its SHA-256 is `ffd28511add85d9508b51f50d3bbe4d822f4a5240800edb4f014edb75abdb1aa`.

The adjacent `SOURCE_MANIFEST.json` records all three approved candidates, current official API metadata, source URLs, authorship, hashes, import settings, external evidence, and the visual-acceptance boundary.
