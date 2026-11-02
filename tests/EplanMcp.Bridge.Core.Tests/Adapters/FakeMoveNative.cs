using EplanMcp.Bridge.Core.Tests.Adapters;

#pragma warning disable CA1822 // Fake members preserve the native instance API used by TransactionScope.

namespace EplanMcp.Bridge.Core.Tests.Adapters
{
    internal static class FakeMoveNative
    {
        [ThreadStatic] private static List<string>? _calls;
        [ThreadStatic] public static int Value;
        [ThreadStatic] public static Action? EndTransaction;
        [ThreadStatic] private static List<int>? _history;
        public static List<string> Calls => _calls ??= [];
        public static List<int> History => _history ??= [];
        public static void UndoPrevious()
        {
            Value = History[History.Count - 1];
            History.RemoveAt(History.Count - 1);
        }
    }
}

namespace Eplan.EplApi.DataModel
{
    internal sealed class SafetyPoint : IDisposable
    {
        private readonly int _before = FakeMoveNative.Value;
        private readonly int _historyCount = FakeMoveNative.History.Count;
        public static SafetyPoint Create() { FakeMoveNative.Calls.Add("Safety"); return new(); }
        public void Commit() => FakeMoveNative.Calls.Add("Commit");
        public void Rollback()
        {
            FakeMoveNative.Calls.Add("Rollback");
            FakeMoveNative.Value = _before;
            FakeMoveNative.History.RemoveRange(_historyCount, FakeMoveNative.History.Count - _historyCount);
        }
        public void Dispose() => FakeMoveNative.Calls.Add("ReleaseSafety");
    }
    internal sealed class UndoManager : IDisposable
    {
        private UndoStep? _step;
        public UndoStep CreateUndoStep() { FakeMoveNative.Calls.Add("Undo"); return _step = new(); }
        public void Dispose()
        {
            // Enforce native protocol ownership; do not simulate nondeterministic finalizer timing in Small tests.
            Assert.True(_step is null || _step.Disposed, "Release the undo protocol before its manager.");
            FakeMoveNative.Calls.Add("ReleaseUndo");
        }
    }
    internal sealed class UndoStep : IDisposable
    {
        private readonly int _before = FakeMoveNative.Value;
        public bool Disposed { get; private set; }
        public void SetUndoDescription(string description) => Assert.StartsWith("AI: eplan_", description, StringComparison.Ordinal);
        public void CloseUndo()
        {
            FakeMoveNative.Calls.Add("CloseUndo");
            if (FakeMoveNative.Value != _before)
            { FakeMoveNative.History.Add(_before); }
        }
        public void DoUndo() { FakeMoveNative.Calls.Add("DoUndo"); FakeMoveNative.Value = _before; }
        public void Dispose() { Disposed = true; FakeMoveNative.Calls.Add("ReleaseStep"); }
    }
    internal sealed class TransactionManager
    {
        public Transaction CreateTransaction() { FakeMoveNative.Calls.Add("Transaction"); return new(); }
    }
    internal sealed class Transaction : IDisposable
    {
        private readonly int _before = FakeMoveNative.Value;
        private readonly Action? _endTransaction = FakeMoveNative.EndTransaction;
        public void Commit() { FakeMoveNative.Calls.Add("Commit"); _endTransaction?.Invoke(); }
        public void Abort() { FakeMoveNative.Calls.Add("Abort"); FakeMoveNative.Value = _before; _endTransaction?.Invoke(); }
        public void Dispose() { FakeMoveNative.Calls.Add("ReleaseTransaction"); _endTransaction?.Invoke(); }
    }
}

namespace Eplan.EplApi.ApplicationFramework
{
    internal enum QuietModes { ShowNoDialogs }
    internal sealed class QuietModeStep : IDisposable
    {
        public QuietModeStep(QuietModes mode) { Assert.Equal(QuietModes.ShowNoDialogs, mode); FakeMoveNative.Calls.Add("Quiet"); }
        public void Dispose() => FakeMoveNative.Calls.Add("EndQuiet");
    }
}
