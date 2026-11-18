using EplanMcp.Bridge.Core.Tests.Adapters;

#pragma warning disable CA1822 // Fake members preserve the native instance API used by TransactionScope.

namespace EplanMcp.Bridge.Core.Tests.Adapters
{
    internal static class FakeMoveNative
    {
        [ThreadStatic] private static List<string>? _calls;
        [ThreadStatic] public static int Value;
        [ThreadStatic] public static int OpenUndoSteps;
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
        private readonly bool _insideUndo = FakeMoveNative.OpenUndoSteps > 0;
        private bool _ended;

        public void Commit()
        {
            FakeMoveNative.Calls.Add("Commit");
            _ended = true;
        }
        public void Rollback()
        {
            FakeMoveNative.Calls.Add("Rollback");
            Restore();
            _ended = true;
        }

        private void Restore()
        {
            // A7: a safety point created inside an existing undo step cannot roll back.
            if (_insideUndo)
            {
                return;
            }

            FakeMoveNative.Value = _before;
            FakeMoveNative.History.RemoveRange(_historyCount, FakeMoveNative.History.Count - _historyCount);
        }
        public void Dispose()
        {
            // A7/B7: releasing an uncommitted safety point restores the simulated page property.
            if (!_ended)
            {
                Restore();
                _ended = true;
            }
            FakeMoveNative.Calls.Add("ReleaseSafety");
        }
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
        private bool _closed;
        public UndoStep() => FakeMoveNative.OpenUndoSteps++;
        public bool Disposed { get; private set; }
        public void SetUndoDescription(string description) => Assert.StartsWith("AI: eplan_", description, StringComparison.Ordinal);
        public void CloseUndo()
        {
            FakeMoveNative.Calls.Add("CloseUndo");
            if (!_closed)
            {
                FakeMoveNative.OpenUndoSteps--;
                _closed = true;
            }
            if (FakeMoveNative.Value != _before)
            { FakeMoveNative.History.Add(_before); }
        }
        public void DoUndo()
        {
            FakeMoveNative.Calls.Add("DoUndo");
            // A7: DoUndo has no effect after CloseUndo.
            if (!_closed)
            {
                FakeMoveNative.Value = _before;
            }
        }

        public void Dispose()
        {
            if (!Disposed && !_closed)
            {
                FakeMoveNative.OpenUndoSteps--;
            }
            Disposed = true;
            FakeMoveNative.Calls.Add("ReleaseStep");
        }
    }
    internal sealed class TransactionManager
    {
        public Transaction CreateTransaction() { FakeMoveNative.Calls.Add("Transaction"); return new(); }
    }
    internal sealed class Transaction : IDisposable
    {
        private readonly int _before = FakeMoveNative.Value;
        private readonly Action? _endTransaction = FakeMoveNative.EndTransaction;
        [ThreadStatic] private static Transaction? _current;
        private readonly Transaction? _parent = _current;
        private readonly List<Function> _created = [];
        private readonly List<Function> _deviceFunctions = [];
        private bool _ended;

        public Transaction() => _current = this;
        public static Transaction? Current => _current;

        public void TrackCreatedFunction(Function function)
        {
            function.AwaitingCommit = true;
            _created.Add(function);
        }

        public void TrackUnplacedDeviceFunctions(Function[] functions)
        {
            _deviceFunctions.AddRange(functions);
            foreach (Function function in functions.Where(f => !f.IsPlaced))
            {
                function.TemporaryIdentifier = true;
            }
        }

        public void Commit()
        {
            FakeMoveNative.Calls.Add("Commit");
            long nextId = _deviceFunctions.Select(f => f.ObjectIdentifier).DefaultIfEmpty().Max();
            foreach (Function function in _deviceFunctions.Where(f => f.TemporaryIdentifier))
            {
                function.ObjectIdentifier = ++nextId;
                function.TemporaryIdentifier = false;
            }
            foreach (Function function in _created)
            {
                function.AwaitingCommit = false;
            }
            End();
        }

        public void Abort()
        {
            FakeMoveNative.Calls.Add("Abort");
            Restore();
            End();
        }

        private void Restore()
        {
            FakeMoveNative.Value = _before;
            // A13: abandoning a transaction discards functions created using the page/symbol overload.
            foreach (Function function in _created)
            {
                function.Remove();
            }
        }

        private void End()
        {
            _ended = true;
            _current = _parent;
            _endTransaction?.Invoke();
        }

        public void Dispose()
        {
            FakeMoveNative.Calls.Add("ReleaseTransaction");
            // A7: releasing an uncommitted transaction restores the simulated page property.
            if (!_ended)
            {
                Restore();
                End();
            }
            else
            {
                _endTransaction?.Invoke();
            }
        }
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
