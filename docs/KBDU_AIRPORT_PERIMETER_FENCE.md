# KBDU Airport Perimeter Fence

## Reference finding

The airport has perimeter/security fencing, but the evidence does not support
portraying it as one uniform, survey-grade security barrier.

- The City of Boulder's 2007 Airport Master Plan identifies an airport security
  fence and discusses fencing/gates as airside access controls.
- The City's 2024 Long-Term Capital and Development Plan reports adequate
  wildlife fencing on the northeast, an older wood fence in poor condition
  south of the west hangars, and sporadic newer black chain-link fencing along
  the south. It also says the AOA fence did not then encompass the whole
  airfield.
- City airport imagery, the airport webcams, the Wikimedia aerial photograph,
  and the Airport-Data terminal photograph were inspected for scale, color, and
  local context. Reference photographs remain external and are not committed.

The simulation closes the requested visual perimeter along the current
OpenStreetMap KBDU aerodrome boundary (way `77513664`) while retaining the
documented section character. This is a scenery alignment decision, not a
survey, access-control plan, or claim about current security infrastructure.

## Authored representation

`AirportPerimeterFenceRoot` is one explicitly serialized child of
`ProductionEnvironmentRoot/AirportContext`. Four fixed section meshes cover
4,273.191 m:

| Section | Visual treatment | Height |
| --- | --- | ---: |
| Northeast | weathered metal wildlife/chain-link | 1.22 m |
| South | low-gloss black chain-link | 1.83 m |
| West hangars | weathered brown with dark metal replacement character | 1.22 m |
| Northwest | weathered metal wildlife/chain-link | 1.22 m |

Each section is one two-sided vertical strip. Continuous world-distance UVs and
project-authored vertex metadata drive an analytic diamond mesh, posts, and
rails in the existing airport baked-lit shader. Alpha-to-coverage under the
product's 4x MSAA avoids committing a large fence texture and avoids modelling
thousands of individual links.

The complete fence costs four renderers/draws and 104 selected/serialized
triangles. It reuses one existing opaque airport material and adds no material,
texture, transparent slot, collider, rigidbody, realtime shadow, runtime
callback, LOD group, or per-eye state. High-altitude views intentionally fall
back to the orthophoto rather than keeping a sub-pixel fence visible.

## Sources

- City of Boulder, *Airport Master Plan* (2007):
  <https://bouldercolorado.gov/projects/airport-master-plan>
- City of Boulder, *Long-Term Capital and Development Plan* (2024):
  <https://boulder.novusagenda.com/agendapublic/AttachmentViewer.ashx?AttachmentID=6895&ItemID=5673>
- City airport page and webcams:
  <https://bouldercolorado.gov/government/departments/airport> and
  <https://bouldercolorado.gov/airport-webcams>
- OpenStreetMap way `77513664`, ODbL 1.0:
  <https://www.openstreetmap.org/way/77513664>
- Wikimedia Commons airport reference, CC BY 4.0:
  <https://commons.wikimedia.org/wiki/File:Boulder_Municipal_Airport.JPG>
- Airport-Data terminal photograph, reference only:
  <https://airport-data.com/airport/photo/007472>
