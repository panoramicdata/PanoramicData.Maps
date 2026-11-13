# Installing with Helm

The chart in [`charts/panoramicdata-maps`](../charts/panoramicdata-maps) deploys the maps server and,
optionally, the two backends it depends on (see [DEPLOYMENT.md](DEPLOYMENT.md#architecture--data-flow)):

| Component | Chart switch | Data volume | Image |
|---|---|---|---|
| Maps server | always | none (stateless) | `ghcr.io/panoramicdata/maps`, built from `PanoramicData.Maps.Server/Dockerfile` |
| Tile server ([go-pmtiles](https://github.com/protomaps/go-pmtiles)) | `tiles.enabled` | PMTiles archive | **you supply it** |
| Photon geocoder ([komoot/photon](https://github.com/komoot/photon)) | `photon.enabled` | Photon index | **you supply it** |

> **Status.** The chart passes `helm lint` and `helm template` with Helm 3.16.2 (defaults, tiles and
> Photon enabled, ingress on, API keys on). It has not been installed on a cluster.

## What you need to provide

1. **A maps image.** CI publishes `ghcr.io/panoramicdata/maps` on every release tag, tagged with the
   version (for example `0.2.24`) and `latest`. Set `maps.image.tag` to a released version rather than
   relying on `latest`. To use your own build instead, `docker build -t <your-registry>/maps:<tag> -f
   PanoramicData.Maps.Server/Dockerfile .`, push it and set `maps.image.repository`; add
   `maps.imagePullSecrets` for a private registry.
2. **Tile and Photon images** if you want them in-cluster. The repository defines neither, so
   `tiles.image.*` and `photon.image.*` are empty placeholders (`CHANGE_ME`) and the chart refuses to
   render a backend whose image is unset. `tiles.command`/`args` and `photon.command`/`args` are
   there for images that need explicit arguments. The tile server must answer `/style.json` and
   `/planet/{z}/{x}/{y}.mvt`; Photon listens on `photon.port`.
3. **The data.** The volumes the chart creates start empty; the chart does not download datasets.
   Choose and size them as described in [Dataset options](DEPLOYMENT.md#dataset-options-planet-vs-alternatives)
   (planet PMTiles is about 128 GB, planet Photon about 90 GB unpacked) and copy them in, or point
   `tiles.persistence.existingClaim` / `photon.persistence.existingClaim` at claims you have already
   populated. Leave 100% headroom for staging a new build.
4. **Or use backends you already run:** leave `tiles.enabled` and `photon.enabled` false and set
   `maps.tilesStyleUrl` and `maps.photonBaseUrl`.

## Install

```bash
helm install maps ./charts/panoramicdata-maps \
  --set maps.image.repository=<your-registry>/maps --set maps.image.tag=<tag> \
  --set maps.photonBaseUrl=https://photon.example.com \
  --set maps.tilesStyleUrl=https://tiles.example.com/style.json
```

With in-cluster backends:

```bash
helm install maps ./charts/panoramicdata-maps -f my-values.yaml
```

```yaml
# my-values.yaml
maps:
  image: { tag: "<released-version>" }
  ingress: { enabled: true, className: nginx, host: maps.example.com, tlsSecretName: maps-tls }
tiles:
  enabled: true
  image: { repository: registry.example.com/go-pmtiles, tag: "1.0" }
  persistence: { size: 256Gi, storageClass: fast }
photon:
  enabled: true
  image: { repository: registry.example.com/photon, tag: "1.0" }
  persistence: { size: 150Gi }
```

Verify with `kubectl port-forward svc/maps-panoramicdata-maps-maps 8080:80` and
`curl http://localhost:8080/health`.

## API keys

Set `maps.requireApiKey=true` and list keys in `maps.apiKeys`. They are stored in a Secret and
injected as `Maps__ApiKeys__N`. Prefer passing them with `--set-file` or a values file kept out of
source control.

## Monitoring data age

`/health` reports `sourceData.tiles` and `sourceData.photon` (age in days, or `unknown`); see
[DEPLOYMENT.md](DEPLOYMENT.md#data-age-on-health). Set the sources through `maps.extraEnv` if the defaults
do not match your backends, for example `Maps__TilesDataUrl`.

## Limits of this chart

- The tile and Photon deployments are single-replica with `Recreate` updates and a `ReadWriteOnce`
  volume. Run them replicated yourself if you need that.
- It does not manage certificates, ingress controllers or dataset downloads.
- Attribution rules in [DEPLOYMENT.md](DEPLOYMENT.md#attribution--licensing) still apply.
