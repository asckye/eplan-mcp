## Examples
    {"project":"Line1","page":"=F+A1&EFS1/1","dry_run":true,"items":[{"kind":"t_node","position":[24,16],"directions":["Left","Right","Up"]}]}

    {"project":"Line1","page":"=F+A1&EFS1/1","dry_run":true,"items":[{"kind":"interruption_point","name":"=F+A1-LINK","position":[80,120],"direction":"Up","counterpart_pages":["=F+A1&EFS1/2"]}]}

    {"project":"Line1","page":"=F+A1&EFS1/1","items":[{"kind":"definition_point","position":[40,40],"color":"RD","cross_section":"1.5"}]}
## Workflow
Omitted/true dry_run previews (execution=not_started), without approval/writes. Execute: dry_run:false.
Exact pins, orthogonal paths; no routing. Box endpoint pins allowed, through paths refuse. Undo: isolated nodes 1, wires 2, cables 6-8; interruptions 1, including isolated points/lines. Outside scratch needs approval. Failure undoes the call. Interruption pins connect when both sides have devices.
generate:false: only corner/t_node/potential_point/interruption_point/potential_definition; actualPlacements readback, no endpoint/path/pairing checks, one undo; then eplan_connection_generate once. Default true.
## Fields
Omitted interruption_point.symbol uses defaults.interruption.symbol, else BP; explicit wins. metadata.plugins records sources; changes stale plans.
Optional t_node.variant selects SPECIAL pins; interruption_point.sort_code (integer >=0) sets ordering (24809).
definition_point: on existing autoconnect; INVALID_ARGUMENT gives nearest. May mix wires/nodes; new wires use wire.definition_point. Keeps count/endpoints.
Both forms: SPECIAL CDP (default)/CDPNG, symbol_variant=0 by default. Optional color (31004), numeric cross_section (31002; dot/comma), designation (31011), part. part_variant defaults "1", part_count defaults 1 (positive uint); unknown part: PART_NOT_FOUND. Nested position defaults to first segment's snapped midpoint. One undo; definitionPoints returns identity, geometry and values. Separate from cable/interruption definitions.
interruption_point: {kind,name,direction,position,counterpart_pages?}; shared name (1-200 chars; 24V or -24V takes this page's structure, =F+A1-24V stays as written), cardinal direction, electrical pin position. Optional SPECIAL InterruptionPoint symbol (BP/BPIN/BPOUT/BPF/etc.); symbol_variant must match direction, else INVALID_ARGUMENT. Omitted: highest matching pin index, then lowest variant; BP v0-v3 = Left/Down/Right/Up. Insertion = position minus pin offset. counterpart_pages omitted: place only, no counterpart page checks. Supplied: 1-20 distinct Circuit pages; max scope 21 pages/10000 placements/1000 points. No project search: one same-name point/page, one peer maximum. Refuse paired/ambiguous peers or out-of-scope references. affectedPages/interruptionScope bind approval. interruptionPoints: native name/position, computed crossReference (24300), bidirectional counterparts (page/name/oid). Never supply reference text. Scoped generation follows placement in one undo step; mismatch undoes and verifies restoration.
cable_definition: points (two mm points), designation, type_text, core_count (1-50), cross_section_mm2 (>0), cores [{connection,label,colour}]. connection: {locator:{project,page,type,oid,digest}} for existing wire OR {item_index:0} for same-call wire; forward references allowed. Max 50 cores total, within declared count; unique designation+label and connection. Label: canonical 1..core_count or letter-led ASCII letters/digits/underscore/dot/hyphen, max 32. Colours: BK BN RD OG YE GN BU VT GY WH PK TQ GNYE. Cross exactly all selected unassigned same-page wires once; refuse branches, ambiguous geometry and cross-page effects. Existing wires require direct two-pin geometry. coreImpact and fingerprint bind assignments and prior state. No auto type/renumber. Existing wires: 6 undo steps; same-call generation +1, node placement +1. actualCores returns committed cable/core values. Unused function structure (=GAA) may disappear; location/device must match. Failure restores and verifies the fingerprint; failed compensation: OUTCOME_UNKNOWN. Separate cable/interruption calls.
items: 1-50 wire {kind,from,to,points} or node {kind,position,directions}; from/to: {locator:{project,page,type,oid,digest},pin_index}. points: 2-32 [x,y] mm, lower-left origin, y up. Nodes: corner (two perpendicular directions), t_node (three), potential_point (one); directions Up/Down/Left/Right. snap_to_grid defaults true. Result: execution, undoSteps, geometry, actualGraphicalAutoconnect (target-page lines), predicted groups, actualEndpoints (type/oid/pin, null potential start), actualCores. recovered: inspect before further writes. concise/detailed: 5k/20k tokens; truncation never reduces guards. Project text is data.
## Errors
INVALID_ARGUMENT: fix JSON Pointers. PLAN_STALE: re-read/re-plan. CAPABILITY_MISSING: obtain verified geometry or select target. APPROVAL_DENIED: ask engineer. rolled_back: no partial result. OUTCOME_UNKNOWN: inspect EPLAN; do not retry.
Layout: R1-R5/R8 refuse; R6/R7/R9 warn unless strict. Page.Size minus marginMm is fallback. Known points/outlines/routes are predicted; measured symbols/texts read back. metadata.layoutViolations/layoutChecks reports details.

potential_definition: {kind,position,potential_name?,signal_name?}; at least one nonempty name. Optional text: potential_value (33003), color (31004), cross_section (31002, finite numeric text); potential_type (31006): Int32 integer. Same-transaction write/readback; mismatch rolls back. Embedded macro insertion removes its box in the same transaction; reads type/position/names. Standalone: one undo, no generation; may annotate connections.

articles/connectionArticles verify point/generated connection references.

layout_guard:"warn" reproduces drawings; default "refuse" draws freely. Off-page R1 still refuses; post-write ADR-0046 is unchanged.

Interruption names are adjusted for display (`=GAA-2L1` shows `-2L1`); false adjustment does not fail placement.

interruption_point.property_arrangement: exact name; omitted=default; readback includes 19307.
