# Test tiles

`planet-5-14-11.mvt` is a real Mapbox Vector Tile (z5/x14/y11, gzipped as served) captured from
the Panoramic Data Protomaps tile service. It covers the Atlantic west of Galicia and contains:

- `water`: one `ocean` polygon covering the tile;
- `landuse`: one `nature_reserve` MultiPolygon — a *marine* protected area, over open sea;
- `places`: Porto and Santarém.

It is the reproduction for issue #10: the renderer used to fill every `landuse` polygon with a land
green, so this marine reserve appeared as land-coloured blobs in the ocean. The reference
protomaps-light style gives these kinds `fill-opacity: 0` at zoom 6 and below, which is why a
MapLibre client never showed them at this zoom and our renderer did.

Data © OpenStreetMap contributors, ODbL — the same licence and attribution the rendered maps carry.
