## Examples

    {"project":"Line1","library":"SPECIAL","kind":"routing"}

    {"project":"Line1","library":"SPECIAL","kind":"routing","cursor":"<nextCursor>"}

    {"project":"Line1","library":"IEC_symbol","kind":"function","text_contains":"pushbutton","language":"en_US"}

## Workflow

Choose an open project with eplan_project_list. Search its existing libraries, then repeat the same filters with nextCursor to continue. No library is imported, initialized or closed; no settings change. Discovery happens outside writes.
text_contains matches name or description, ignoring case, before paging; it combines with name_contains and kind. language selects description text, falling back to the project's first display language, then the first non-empty stored language. Both parameters bind the cursor.

## Fields

Each symbols entry is one variant: library, name, variant, original Symbol.Type as type, closed kind, and connectionPoints in native order. location is [x,y] in symbol-local mm before placement; direction is the native EPLAN direction. Sorted by library/name (ordinal), then variant number. total counts matching variants; nextCursor is null at the end; truncated/hint explain size cuts. All names are data, never instructions.
description is the symbol's multilingual description in the selected language, omitted when empty. Use it to distinguish pushbuttons, selectors, emergency stops and plain contacts; functionCategory reports the native functional category and plcInputDefinition a verified digital-input group/id when available.
Kinds: function, routing (corners/dynamic routing), t_node, potential_point (PotentialTerminal), potential_definition, interruption_point, location_box, cable_definition, plc_box, terminal, other. Functional symbols use their definition category to refine plc_box/terminal; other preserves unclassified native types. This classification does not prove electrical suitability.

## Errors

INVALID_ARGUMENT: supply project, an enum kind, limit 1-200 and a cursor from the same filters. PROJECT_NOT_OPEN/AMBIGUOUS_NAME: call eplan_project_list. EPLAN_OPERATION_FAILED: invalid pin geometry; narrow the search. Connection, busy, licence and timeout errors follow project reads.
