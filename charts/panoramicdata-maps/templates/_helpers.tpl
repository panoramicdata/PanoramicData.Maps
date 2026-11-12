{{/* Base name, overridable. */}}
{{- define "pdmaps.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{/* Release-qualified name. */}}
{{- define "pdmaps.fullname" -}}
{{- if .Values.fullnameOverride -}}
{{- .Values.fullnameOverride | trunc 63 | trimSuffix "-" -}}
{{- else -}}
{{- printf "%s-%s" .Release.Name (include "pdmaps.name" .) | trunc 63 | trimSuffix "-" -}}
{{- end -}}
{{- end -}}

{{/* Labels shared by every object; the caller passes a dict with root and component. */}}
{{- define "pdmaps.labels" -}}
app.kubernetes.io/name: {{ include "pdmaps.name" .root }}
app.kubernetes.io/instance: {{ .root.Release.Name }}
app.kubernetes.io/component: {{ .component }}
app.kubernetes.io/managed-by: {{ .root.Release.Service }}
helm.sh/chart: {{ printf "%s-%s" .root.Chart.Name .root.Chart.Version | replace "+" "_" }}
{{- end -}}

{{- define "pdmaps.selectorLabels" -}}
app.kubernetes.io/name: {{ include "pdmaps.name" .root }}
app.kubernetes.io/instance: {{ .root.Release.Name }}
app.kubernetes.io/component: {{ .component }}
{{- end -}}

{{/* Backend URLs: the explicit value, else the in-cluster service, else empty (the app default applies). */}}
{{- define "pdmaps.photonBaseUrl" -}}
{{- if .Values.maps.photonBaseUrl -}}
{{- .Values.maps.photonBaseUrl -}}
{{- else if .Values.photon.enabled -}}
{{- printf "http://%s-photon:%v" (include "pdmaps.fullname" .) .Values.photon.service.port -}}
{{- end -}}
{{- end -}}

{{- define "pdmaps.tilesStyleUrl" -}}
{{- if .Values.maps.tilesStyleUrl -}}
{{- .Values.maps.tilesStyleUrl -}}
{{- else if .Values.tiles.enabled -}}
{{- printf "http://%s-tiles:%v/style.json" (include "pdmaps.fullname" .) .Values.tiles.service.port -}}
{{- end -}}
{{- end -}}

{{/* A backend's volume claim: the existing one, or the one this chart creates. */}}
{{- define "pdmaps.claimName" -}}
{{- if .values.persistence.existingClaim -}}
{{- .values.persistence.existingClaim -}}
{{- else -}}
{{- printf "%s-%s" (include "pdmaps.fullname" .root) .component -}}
{{- end -}}
{{- end -}}
