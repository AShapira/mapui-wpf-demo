# 06 — Manage layers, select features, and highlight geometry

## Purpose and prerequisites

Connect the layer tree to the rendered map and distinguish selection state from source data. Use only the bundled local layers for this lesson.

## Step by step

1. Expand Map layers. The tree lists user layers **top first**, while Mapsui's collection draws bottom first. Read the index conversion in the move command.
2. Uncheck a layer. Its `Enabled` value changes and the map refreshes. Check it again.
3. Select the layer's tree row (click the row area beside its checkbox). Drag Selected layer opacity. The slider binds to a value between zero and one.
4. Use Up and Down. Confirm that overlapping geometry follows the new drawing order.
5. Use Zoom. The command fits the selected layer extent with a small margin. An empty WFS layer has no extent until data arrives.
6. Click a visible point. The map-control hit test returns a feature from explicitly supplied memory layers. The details panel shows its scalar attributes.
7. Read `FeatureLayers.Select`. It clones the geometry, clears copied feature styles, and puts the clone in a separate selection layer. The original geometry and attributes are unchanged.
8. Click Clear selection, then remove a selected user layer. The selection must not remain as an orphan highlight.

```powershell
dotnet test tests/OgcDemo.Tests --no-restore --filter "FullyQualifiedName~Highlight|FullyQualifiedName~LayerOrdering|FullyQualifiedName~OldRequest"
```

## Design choices

The selection layer is always above user layers and is excluded from hit testing and the layer tree. Otherwise users could select the highlight instead of the original feature. `MainViewModel` owns a single selection for clarity; multiple selection is an extension.

Layer addition/removal is dynamic. Configured OGC profiles are chosen from the service selector, GeoJSON comes from a file dialog, and generated graphics come from a command. Profile authoring remains in JSON rather than a complex connection-editor UI.

A disabled WFS layer stops participating in refreshes. Layer changes invalidate outstanding generations so an old request cannot reintroduce removed data. WMS and WMTS images do not expose local geometry for this picking path; the WMS lesson uses GetFeatureInfo.

## Checkpoint

Visibility, opacity, order, zoom, removal, and clear-selection work without network access. Orange highlighting follows the source feature shape, including polygon holes. The application never saves these temporary selection changes into the input GeoJSON file.
