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
| Northeast | green privacy slats with galvanized chain-link | 1.22 m |
| South | green privacy slats with galvanized chain-link | 1.83 m |
| West hangars | green privacy slats with galvanized chain-link | 1.22 m |
| Northwest | green privacy slats with galvanized chain-link | 1.22 m |

Each section uses terrain-conformed panel spans plus bounded physical top rails
and posts. Continuous world-distance UVs and project-authored vertex metadata
drive green vertical slats and a galvanized diamond pattern in one dedicated
fence shader. The representation avoids a large fence texture and avoids
modelling thousands of individual links.

The complete fence costs four renderers/draws and 252 selected/serialized
triangles. It uses one project-authored material and adds no texture, collider,
rigidbody, realtime shadow, runtime callback, LOD group, or per-eye state.
High-altitude views intentionally fall back to the orthophoto rather than
keeping a sub-pixel fence visible.

The green privacy-slat treatment was selected from Alex's external visual
reference during a worn Quest review. That photograph is not committed and is
not used as a texture source.

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
