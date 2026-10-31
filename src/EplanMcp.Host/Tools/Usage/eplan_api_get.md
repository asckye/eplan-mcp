## Examples

Describe a property dictionary:

    {"id":"D:Function"}

Describe all overloads of a method:

    {"id":"O:Eplan.EplApi.HEServices.Translate.TranslateObjects","limit":10}

## Workflow

Use eplan_api_search to discover exact IDs. Pass nextCursor as cursor with the same id and limit until it is null: overloads precede evidence. partial means only some counted overloads are reached. Evidence describes tool-level coverage bounds. Property counts include obsolete IDs; eplan_property_schema describes restrictions and eplan_property_get reads supported targets. Unknown IDs return RESOURCE_NOT_FOUND; invalid cursors return INVALID_ARGUMENT and require restarting without cursor.

## Fields

catalogVersion identifies the shipped EPLAN API version.

entry describes group status and reaching tools. items contains overload signatures, parameters, returnType, nullable summaries, dictionary propertyCount/reading guidance or evidence references. total counts detail records; nextCursor, truncated and hint describe continuation.

## Errors

RESOURCE_NOT_FOUND: discover an exact id with eplan_api_search. INVALID_ARGUMENT: correct all reported violations; omit an invalid cursor to restart.
