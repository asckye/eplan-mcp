## Examples

Preview the multi-line page of a card, with the macro from its part:

    {"project": "Line1", "devices": [{"tag": "=F+A1-A1", "multi_line_page": "=F+A1&EFS1/10"}]}

Generate two cards with a chosen macro for the second:

    {"project": "Line1", "devices": [{"tag": "=F+A1-A1", "multi_line_page": "=F+A1&EFS1/10"}, {"tag": "=F+A1-A2", "macro": "PXC/2861344.ema", "multi_line_page": "=F+A1&EFS1/11"}], "dry_run": false}

## Workflow

1. The device must be in the project first: insert its overview macro (eplan_macro_insert with representation Overview), or import the PLC data. `eplan_device_list` with category `PLCBox` lists the tags.
2. Omitted or true dry_run previews (execution=not_started) and writes nothing; `changes` lists each new page with its device and macro. Execute with dry_run:false.
3. EPLAN creates the pages and places the macro's functions, which join the device: no rename is needed afterwards. Existing representations are kept and verified before committing.
4. One Ctrl+Z reverts the whole call. Outside scratch the engineer approves.

## Fields

- `single_line_page`, `multi_line_page`, `overview_page`, `rack_page`: a page that is given selects that representation. Each must be new and is used once; two macros on one page would overlap.
- `macro`: a path below the macro directory; omit it for the macro EPLAN knows from the device's part.
- `pages`: per generated page `name`, `type`, `functions` (at most 200) and `functionCount`.

## Errors

INVALID_ARGUMENT=not a PLC device, no macro, or a page that exists or repeats; MACRO_NOT_FOUND=use eplan_macro_list; PLAN_STALE=preview again; EPLAN_OPERATION_FAILED=EPLAN refused or generated no page, nothing was committed; OUTCOME_UNKNOWN=inspect before retrying.
