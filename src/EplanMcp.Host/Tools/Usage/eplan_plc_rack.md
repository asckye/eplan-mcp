## Examples

Preview a CPU with its bus adapter and two cards (changes nothing):

    {"rack_page": "=K1+UC1&EFS1/1", "cards": [
      {"tag": "=K1+UC1-KE1", "part": "SIE.6ES7510-1SJ01-0AB0", "overview_page": "=K1+UC1&EFA1/1", "picture_variant": 1,
       "sub_devices": [{"macro_suffix": "-K1", "tag": "=K1+UC1-KEC1", "part": "SIE.6ES7193-6AR00-0AA0"}]},
      {"tag": "=K1+UC1-KE2", "part": "SIE.6ES7136-6BA01-0CA0", "overview_page": "=K1+UC1&EFA1/2", "picture_variant": 6,
       "extra_parts": ["SIE.6ES7193-6BP00-0BA0"]},
      {"tag": "=K1+UC1-KE3", "part": "SIE.6ES7131-6BH01-0BA0", "overview_page": "=K1+UC1&EFA1/3", "picture_variant": 6,
       "extra_parts": ["SIE.6ES7193-6BP00-0DA0"]}]}

Draw it: the same call with "dry_run": false.

## Workflow

1. Create the pages first with eplan_page_create: one empty Circuit page for the rack and one empty Overview page per card.
2. Look up each card's part with eplan_part_search; the part must name a window macro. eplan_macro_get lists the macro's MultiLine variants; ET 200SP pictures: DI/DQ light 5, dark 6, F-DI 6, CPU 1, CM PtP 3.
3. Give every card a new full tag. Put the BaseUnit (light 0BA0 for the first card of a potential group, dark 0DA0 for the others) in extra_parts.
4. Map sub-devices of the macro (a CPU's bus adapter, macro suffix -K1) with sub_devices; an unmapped one becomes the card's tag plus its suffix.
5. Preview, check changes (rack x, width, parts per card), then call again with dry_run false. One Ctrl+Z in EPLAN takes the whole rack back.
6. Then draw the function pages: call each card's points with eplan_device_place (plc_connection_point); supply interruption points and labels on the rack page with eplan_connection_create and eplan_placement_create.

## Fields

- `changes[]`: one per card: `name` is the tag; `newValue` lists the parts, the overview page and variant, the picture variant, rack x and width, and the sub-devices.
- `cards[]` (after drawing): `tag`, `overviewPage`, `rackX`, `width`, `parts` as read back from the card's main function, `subDevices`.
- `execution`: not_started (preview), committed, or unknown; `undo`, `auditId`, `recovered`, `hint`.

## Errors

- INVALID_ARGUMENT: every argument problem at once in `metadata.violations`: missing tags, parts, pages or picture variants; a tag already used; a page that is not empty or not of the right type; a variant the macro lacks; a rack wider than its page (split the cards over two rack pages).
- MACRO_NOT_FOUND: a card's part names no window macro. PART_NOT_FOUND: a part is not in the parts database.
- PLAN_STALE: a page, a macro or the tags in use changed after the preview; preview again.
- EPLAN_OPERATION_FAILED: an insert, rename or part did not come out as planned; the whole rack was rolled back.
