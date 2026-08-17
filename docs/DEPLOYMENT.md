# Deploying PanoramicData.Maps

PanoramicData.Maps is a small, stateless HTTP service. It renders static map images natively
(SkiaSharp — no headless browser) and proxies geocoding. It has **two runtime dependencies**, both
self-hosted, and no database or persistent storage of its own.

- [Architecture & data flow](#architecture--data-flow)
- [Dependencies](#dependencies)
- [Dataset options (planet vs alternatives)](#dataset-options-planet-vs-alternatives)
- [Configuration](#configuration)
- [Running with Docker](#running-with-docker)
- [Kubernetes](#kubernetes)
- [Scaling & sizing](#scaling--sizing)
- [Attribution & licensing](#attribution--licensing)

## Architecture & data flow

```
                 ┌──────────────────────────────────────────┐
  client ──────▶ │  PanoramicData.Maps  (this service)      │
  /staticmap     │  • parses the request (Google-compatible) │
                 │  • geocodes place names (if needed) ──────┼──▶  Photon geocoder
                 │  • fetches vector tiles for the viewport ─┼──▶  tile service (go-pmtiles)
                 │  • draws basemap + overlays (SkiaSharp)   │
                 │  • returns PNG/JPEG                        │
                 └──────────────────────────────────────────┘
```

The service holds no state; every request is self-contained. Horizontal scaling is just more
replicas behind a Service.

## Dependencies

| Dependency | What it provides | Where it lives |
|---|---|---|
| **Photon geocoder** ([komoot/photon](https://github.com/komoot/photon)) | place/address → lat/lon (for `center=<place>` and `location=`) | Self-hosted. In Panoramic Data: the `photon` Flux app (`photon.panoramicdata.com` / `photon-test…`). |
| **Vector tile service** ([go-pmtiles](https://github.com/protomaps/go-pmtiles) + Protomaps PMTiles) | the basemap: MVT vector tiles + the MapLibre style/glyphs/sprites | Self-hosted. In Panoramic Data: the `tiles` Flux app (`tiles.panoramicdata.com` / `tiles-test…`). |

The service reads the tile **style URL** (`Maps:TilesStyleUrl`) and derives the vector-tile endpoint
from it (`…/planet/{z}/{x}/{y}.mvt`). It reads the Photon base URL from `Maps:PhotonBaseUrl`.

> The service can point at **any** Photon + Protomaps-PMTiles stack — it is hard-tied to those two
> technologies, not to Panoramic Data's specific instances. The Panoramic Data deployment of the two
> backends is documented in the `PanoramicData.Operations` repo (`flux/apps/photon`, `flux/apps/tiles`).

### Runtime image dependencies

- **SkiaSharp** with `SkiaSharp.NativeAssets.Linux.NoDependencies` — bundles `libSkiaSharp` with no
  extra OS packages needed for drawing geometry.
- **Fonts** — map **labels** (a later milestone) need a font in the image; add `fontconfig` + a font
  (e.g. `fonts-noto`) to the runtime image when labels land. Geometry rendering needs no fonts.

## Dataset options (planet vs alternatives)

The *coverage and size* of the maps come entirely from the **backend datasets**, not this service.
You choose them when deploying Photon and the tile service. Both follow the same principle: **one
shared instance per cluster** serves all consumers (the indexes/archives are large and expensive to
duplicate).

### Tile service (Protomaps PMTiles)

A single `.pmtiles` file. Byte-range served, so the service is I/O-bound, not CPU/RAM-bound.

| Build | Compressed size | Coverage | Use when |
|---|---|---|---|
| **Planet** | ~128 GB | Worldwide | Production / anything global (what Panoramic Data uses). |
| Continent (e.g. `europe`, `north-america`, `asia`) | ~2–30 GB | One continent | Cost/space-constrained, single-region product. |
| Regional/city extract | small | A bbox | Local dev, demos. |

Source: Protomaps daily builds (`maps.protomaps.com/builds`). **Pin a dated build and host it
yourself** — the public build URLs are not guaranteed stable. Provisioning notes: give the archive
volume **100% headroom over the file size** so a new build can be staged before the old one is
removed; and **throttle any in-cluster copy** (ionice/nice/bandwidth-limit) — an unthrottled ~128 GB
copy can overload a node's IO (learned the hard way, OPS-154716).

### Photon geocoder

A prebuilt search index (GraphHopper dumps).

| Dataset | Compressed | Unpacked | Coverage |
|---|---|---|---|
| **Planet** | ~58 GB | ~90 GB | Worldwide (production). |
| Europe | ~29 GB | ~45 GB | Europe only. |
| Country/region | smaller | | Where a prebuilt `photon-db` exists (not all countries do). |

Recommend serving the index from shared/replicated storage so the pod survives a node move, and
provisioning ≥100% refresh headroom. Upstream suggests ~64 GB RAM for smooth planet operation;
correctness is unaffected by less RAM (page cache just hits disk more).

> **A Photon/PMTiles instance serves exactly one dataset.** To broaden coverage, point it at a
> *bigger* extract (ultimately planet) — you cannot merge several.

## Configuration

Bound from the `Maps` section (or `Maps__*` environment variables):

| Key | Default | Purpose |
|---|---|---|
| `Maps__PhotonBaseUrl` | `https://photon.panoramicdata.com` | Photon geocoder base URL. |
| `Maps__TilesStyleUrl` | `https://tiles.panoramicdata.com/style.json` | Tile service MapLibre style URL (the tile endpoint is derived from it). |
| `Maps__RequireApiKey` | `false` | When `true`, `/staticmap`, `/v1/*` require an API key (`X-Api-Key` header or `?key=`). |
| `Maps__ApiKeys__0` … | — | Accepted API keys (metering/monetisation). |
| `Maps__MaxWidth` / `MaxHeight` / `MaxScale` | 2048 / 2048 / 2 | Output caps. |

In-cluster, prefer the **internal** Service DNS for the backends to avoid egress, e.g.
`Maps__TilesStyleUrl=http://tiles.tiles.svc.cluster.local/style.json` — as long as the derived tile
URL and the CORS/attribution expectations still hold (server-side rendering doesn't need CORS).

## Running with Docker

```bash
docker run -p 8080:8080 \
  -e Maps__PhotonBaseUrl=https://photon-test.panoramicdata.com \
  -e Maps__TilesStyleUrl=https://tiles-test.panoramicdata.com/style.json \
  panoramicdata/maps:latest

curl -o map.png "http://localhost:8080/staticmap?center=51.5074,-0.1278&zoom=13&size=800x600&markers=color:red|label:A|51.5074,-0.1278"
```

## Kubernetes

The service is a plain stateless Deployment. Example (adapt namespaces/hosts/cert to your cluster):

```yaml
apiVersion: apps/v1
kind: Deployment
metadata: { name: maps, namespace: maps }
spec:
  replicas: 2
  selector: { matchLabels: { app: maps } }
  template:
    metadata: { labels: { app: maps } }
    spec:
      containers:
        - name: maps
          image: panoramicdata/maps:1.0.0   # or a Harbor mirror
          ports: [ { containerPort: 8080 } ]
          env:
            - { name: ASPNETCORE_URLS, value: "http://+:8080" }
            - { name: Maps__PhotonBaseUrl, value: "https://photon.panoramicdata.com" }
            - { name: Maps__TilesStyleUrl, value: "https://tiles.panoramicdata.com/style.json" }
            # - { name: Maps__RequireApiKey, value: "true" }   # for the metered hosted service
          readinessProbe: { httpGet: { path: /health, port: 8080 }, periodSeconds: 10 }
          livenessProbe:  { httpGet: { path: /health, port: 8080 }, periodSeconds: 30 }
          resources:
            requests: { cpu: "250m", memory: "256Mi" }
            limits:   { cpu: "2",    memory: "1Gi" }
---
apiVersion: v1
kind: Service
metadata: { name: maps, namespace: maps }
spec:
  selector: { app: maps }
  ports: [ { port: 80, targetPort: 8080 } ]
---
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata: { name: maps, namespace: maps }
spec:
  ingressClassName: public
  rules:
    - host: maps.example.com
      http: { paths: [ { path: /, pathType: Prefix, backend: { service: { name: maps, port: { number: 80 } } } } ] }
  tls: [ { hosts: [ maps.example.com ], secretName: your-wildcard-cert } ]
```

### Panoramic Data (Flux) deployment

In Panoramic Data this is deployed via Flux from `PanoramicData.Operations`
(`flux/apps/maps/base` + `overlays/{test,prod}`), mirroring the `photon`/`tiles` apps: image mirrored
to Harbor, ingress TLS via the shared `panoramicdata-cert` secret, and `Maps__*` supplied through
`postBuild.substitute`. It consumes the in-cluster `photon` and `tiles` services. Track the cluster
deployment under an OPS Change Request.

## Scaling & sizing

- **Stateless** → scale by replica count. No shared storage, no sticky sessions.
- **CPU-bound**: rendering is SkiaSharp CPU work; tile fetches are I/O. Start at `cpu: 2` limit, 2
  replicas; scale on CPU. Memory is modest (a few hundred MB per concurrent render).
- Consider an HTTP cache in front for popular map requests (they're deterministic for a given URL).
- The backends (Photon/tiles) are the heavy, stateful pieces — size those per their own guidance.

## Attribution & licensing

The **code** is MIT. The **map data** is OpenStreetMap (**ODbL**) and the basemap style is **CC0**.
Rendered output must carry visible **"© OpenStreetMap"** attribution — the renderer draws it
automatically; do not crop it out.
