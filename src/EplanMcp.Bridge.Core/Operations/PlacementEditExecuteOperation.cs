using EplanMcp.Contracts;
namespace EplanMcp.Bridge.Core.Operations;

public sealed class PlacementEditExecuteOperation(IPlacementEditor editor, ITransactionScopeFactory transactions, Action<OpenProject, BackupTarget?> backup) : PlacementEditOperation(editor, transactions, true, backup), IWriteOperation;
