## Examples
    {"object_type":"Function","limit":10}
## Workflow
Find exact dictionary types in an open project. Pass each locator to eplan_object_get. Repeat filters with cursor to continue.
## Fields
project is resolved; items contain locator and name. status is supported or unsupported; reason explains missing adapters. Page, Function and Connection are implemented in this batch. Other types need a reviewed adapter. Cursor expires after ten minutes and checks previously returned content.
## Errors
INVALID_ARGUMENT: correct arguments or restart without cursor. PROJECT_NOT_OPEN: use eplan_project_list. No writes occur.
