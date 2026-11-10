## Examples

Make the N potential light blue, then let the wires follow:

    {"project":"Line1","page":"=GAA+A1&EFS1/1","items":[{"locator":{"project":"C:/Scratch/Line1","page":"=GAA+A1&EFS1/1","type":4,"oid":20417,"digest":"0000000000000000000000000000000000000000000000000000000000000000"},"color":"BU"}],"dry_run":false}

Then eplan_connection_generate with {"project":"Line1","pages":["=GAA+A1&EFS1/1","=GAA+A1&EFS1/2"],"rebuild":true,"dry_run":false}.

## Workflow

1. Find the potential definition: eplan_object_find with object_type PotentialDefinition, or eplan_page_get with include_drawings (kind PotentialDefinition; potential points SPECIAL/PCP read the same way).
2. Preview (dry_run omitted or true): before and after per requested property; unchanged values are not written.
3. Execute with dry_run false: one transaction, read back, one Ctrl+Z. Inside scratch no approval; outside scratch the write policy applies.
4. Wires keep their old colour until eplan_connection_generate runs with rebuild:true on the pages that carry the potential. A colour set on the wire itself by a connection definition point stays; change that point instead.

## Fields

items: 1-50 {locator, potential_name?, signal_name?, color?, cross_section?, potential_value?, potential_type?}; at least one property each. color (31004) is EPLAN's colour code (BK, BU, GNYE, WH...); cross_section (31002) is text such as 1.5, stored with a decimal comma; potential_value (33003) such as 230V; potential_type (31006) an integer. Result items: locator, before (every current value, with the potential name), after (the requested values).

## Errors

INVALID_ARGUMENT: fix the listed pointers; a locator of anything but PotentialDefinition is refused. PLAN_STALE: the object or a value changed after the preview; read the page and call again. EPLAN_OPERATION_FAILED with rolled_back: EPLAN read back another value; nothing changed.
