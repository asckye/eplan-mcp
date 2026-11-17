using EplanMcp.Contracts;
namespace EplanMcp.Bridge.Core.Operations;

public sealed class PlacementEditExecuteOperation(IPlacementEditor editor, ITransactionScopeFactory transactions, Action<OpenProject, BackupTarget?> backup, ISymbolReader? symbols = null) : PlacementEditOperation(editor, transactions, true, backup, symbols), IWriteOperation;
