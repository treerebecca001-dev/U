---
version: alpha
colors:
  primary: "#3c6e9e"
  canvas: "#202020"
  panel: "#292929"
  raised: "#353535"
  border: "#181818"
  text: "#e2e2e2"
  muted: "#b4b4b4"
  selection: "#3c6e9e"
  focus: "#83bdf7"
  warning: "#dcc58c"
  error: "#ef9a9a"
typography:
  ui:
    fontFamily: '"Microsoft JhengHei UI", "Segoe UI", "Noto Sans TC", sans-serif'
    fontSize: "13px"
    lineHeight: "1.5"
  data:
    fontFamily: 'Consolas, "Cascadia Mono", monospace'
    fontSize: "12px"
rounded:
  control: "2px"
spacing:
  compact: "4px"
  normal: "8px"
  panel: "12px"
components:
  button:
    backgroundColor: "{colors.raised}"
    textColor: "{colors.text}"
    rounded: "{rounded.control}"
    height: "28px"
    typography: "{typography.ui}"
  buttonPrimary:
    backgroundColor: "{colors.primary}"
  selected:
    backgroundColor: "{colors.selection}"
  panel:
    backgroundColor: "{colors.panel}"
  input:
    backgroundColor: "{colors.canvas}"
  muted:
    textColor: "{colors.muted}"
  warning:
    textColor: "{colors.warning}"
  error:
    textColor: "{colors.error}"
---
# SceneWorkbench design context

Status: local web development UI, launched from source. The user requested browser development and no packaging on 2026-10-22. The existing Godot desktop prototype remains development history; its export is not a release or acceptance result.

## Authority and audience

The user's requested Unity-like interface and workflow take precedence over generic visual novelty. The primary audience is environment artists editing an existing URP/ASE Unity project. Default locale is zh-TW, with familiar Unity terms retained in English.

## Layout and appearance

- Neutral dark editor surfaces: canvas `#202020`, panel `#292929`, raised controls `#353535`, borders `#181818`, text `#E2E2E2`, selection `#3C6E9E`.
- Semantic error, warning and success indications include text and icons, not color alone.
- Desktop typography: Microsoft JhengHei UI for localized labels, Segoe UI for Latin UI text, Consolas for identifiers and numeric diagnostics.
- Hierarchy left, Scene/Game center, Inspector right, Project/Console below. Each panel owns its scrolling; resizing a panel preserves other panel states.
- Dense, restrained controls and visible focus. Keep names, shortcuts, selection and gizmo behavior close to Unity.

## Runtime owners

Token ownership is Model B: `src/SceneWorkbench.Desktop/EditorTheme.cs` owns the existing palette. `scripts/sync-web-theme.ps1` derives `src/SceneWorkbench.Web/wwwroot/ui/tokens.css`; do not hand-edit that generated file. Web controls consume semantic CSS variables, including the shared scrollbar baseline. The matching values above mirror this accepted identity.

Use Microsoft JhengHei UI / Segoe UI for 13px editor controls and panel labels, 17px dialog titles, and Consolas for coordinates and fileIDs. Borders are 1px, control radius 2px, gaps 4/8/12px. Selection is blue; XYZ axes retain their conventional red/green/blue meaning. The signature is a working scene viewport with the Unity-like panel frame. Keep promotional hero graphics outside the editor.

Web-only success/axis and scrollbar roles live in `wwwroot/ui/editor.css`; they do not duplicate shared palette ownership. Preserve the native prototype's neutral dark panels and blue selection. The web layout is a development adapter; the desktop palette remains canonical.

The web shell owns a viewport-bounded editor on desktop, with separate panel scrollers. Below 900px the same panels become a document-scrolling stacked workspace; no essential action disappears. Inspector action buttons follow normal document flow on narrow screens so they cannot cover coordinate inputs; desktop actions may stay sticky within the Inspector scroller. Inspector fields keep their values on errors. Native select popups are an intentional platform-owned choice for Layer and Static settings.

## Interaction contracts

- Same save, undo, conflict and progress behavior across tools. Edits pass through command history and save transactions.
- Ordinary reversible edits use Undo. Unavailable native features are disabled with a concrete reason.
- Long imports and bake jobs provide progress and cancellation without freezing the interface.
- IME composition must not trigger editor shortcuts or premature search. Search has an explicit clear action.
- Unsaved changes, partial errors, missing assets and unsupported components retain entered values and provide recovery actions.
- Read-only source projects and editable working copies are visibly distinguishable.

## Verification boundary

The user's later desktop-first instruction authorizes the offline open/edit/save loop before all interoperability gates pass. Browser UI reuses that same field-scoped C# core. Verify the actual browser, including selection, validation, undo, native save, reopen, conflict recovery, and a narrow viewport. CLI tests provide no browser completion evidence.

The viewport reads source FBX, supported native Mesh YAML and read-only expanded Prefabs. Fixed, source-hash-pinned adapters preview the project's 14 ASE Shaders and basic URP surfaces. Material Inspector edits existing native saved properties; Shader source, ASE graphs, keywords and unknown objects remain preserved. Inspector material/model viewports share `ui/preview-surface.js`, including deterministic WebGL context release. A separate view toggle displays Client collision-helper materials without changing native activation.

Appearance parity remains unverified. ASE SceneDepth soft intersections, URP lighting/baking/post-processing, Canvas Stencil/ClipRect, complete FBX subasset identity/axes, Rig/animation, native Terrain editing, complete Prefab workflows, Play and baking retain their own acceptance gates. Unsupported resources show diagnostics rather than silently becoming supported assets.

Map_60001 is the primary preview acceptance scene. Native FBX instance overrides retain exact decimal IDs and material slot references; structural uniqueness is required before preview applies them. Unknown Shader saved properties and the hash-pinned water template may supply explicitly labelled, read-only surface approximations. Solid diagnostic geometry preserves unresolved objects; counts distinguish approximation from unresolved material. Inspector components use negative inset margins only inside the padded form, so Renderer material links and vector fields fit the panel at 1178px without hiding overflow.

Terrain height preview uses the original binary type tree and full source sample grid with its native parent transforms. The Inspector owns a read-only terrain section for resolution, size, local height range, world origin, holes storage and exact layer references. Missing layers use a labelled checker surface and a separate viewport count. No replacement flat ground, random relief or inferred layer assignment is accepted as native terrain preview. Height authoring, nonempty holes and full Terrain Lit shading retain separate gates.

StylizedWater2's read-only preview is pinned to the original template and eleven library hashes. A local geometry depth pass supplies shoreline/depth color; native keywords and values govern waves, sharp intersections, distance normals and translucency. Map_60001's lake has zero wave speed; both materials have zero normal strength and disable surface foam/refraction/caustics. Preserve these values. The material ball has no scene depth or scene shadows and says so. Changed or missing libraries produce an unresolved diagnostic. The unique default baked reflection comes only from matching-scene LightingData PPtrs and the original EXR, with source hash and cubemap-import checks. Native directional shadow settings, renderer flags, layers and masks govern the preview shadow pass; leaf cutout textures remain cut out in caster depth. The 2048 fallback shadow map is a preview quality choice, not an assertion of URP's quality settings. Full GI, CSM/Shadowmask, local probe blending, baked lighting writes and Unity pixel comparison retain separate gates.
