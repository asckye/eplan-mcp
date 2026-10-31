## Examples

Find translation methods without EPLAN:

    {"namespace":"Eplan.EplApi.HEServices","member_name":"Translate","kind":"method","limit":10}

Find excluded groups and inspect their reasons:

    {"status":"excluded","limit":10}

## Workflow

Pass an item's id or group to eplan_api_get. Repeat all filters and limit with nextCursor as cursor to continue. implemented includes automated_test and live_verified; verified means live_verified. Missing summaries cannot match summary_text. Catalog discovery never authorizes execution.

## Fields

catalogVersion identifies the shipped EPLAN API version.

items contains id, group, kind, short signature, status, excludedReason, tools and partial. total counts all matches; nextCursor, truncated and hint describe continuation.

## Errors

INVALID_ARGUMENT: omit an invalid cursor to restart, or correct all reported filter/limit violations.
