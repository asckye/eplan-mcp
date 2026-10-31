## Examples
    {"object_type":"Function","limit":10}
## Workflow
Find exact dictionary types in an open project. Pass each locator to eplan_object_get. Repeat filters with cursor to continue.
## Fields
project is resolved; items contain locator and name. status is supported or unsupported; reason explains missing adapters. Supported dictionaries: Page, Function, Connection, TerminalStrip, Cable, PlaceHolder, LocationBox, InterruptionPoint, PotentialDefinition and MacroBox. Terminal, PLC, BlackBox and PLCTerminal select function classes using the Function dictionary; Terminal includes PLC terminals, PLCTerminal selects only PLC connection points. BlackBox maps to BoxedDevice. LocationBox is a structure box. MacroBox names are empty. ArticleReference is transient and awaits a stable parent/reference-position locator; PlcIO is an unbooked address entry. Cursor expires after ten minutes and checks previously returned content.
## Errors
INVALID_ARGUMENT: correct arguments or restart without cursor. PROJECT_NOT_OPEN: use eplan_project_list. No writes occur.
