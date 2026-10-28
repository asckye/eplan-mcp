using EplanMcp.Bridge.Core.Tests.Adapters;

#pragma warning disable CA1822 // Fake members preserve the native instance API used by TransactionScope.

namespace EplanMcp.Bridge.Core.Tests.Adapters
{
    internal static class FakeMoveNative
    {
        [ThreadStatic] private static List<string>? _calls;
        [ThreadStatic] public static int Value;
        public static List<string> Calls => _calls ??= [];
    }
}

namespace Eplan.EplApi.DataModel
{
    internal sealed class SafetyPoint : IDisposable
    {
        public static SafetyPoint Create() { FakeMoveNative.Calls.Add("Safety"); return new(); }
        public void Commit() => FakeMoveNative.Calls.Add("Commit");
        public void Rollback() { FakeMoveNative.Calls.Add("Rollback"); FakeMoveNative.Value = 0; }
        public void Dispose() => FakeMoveNative.Calls.Add("ReleaseSafety");
    }
    internal sealed class UndoManager : IDisposable
    {
        public UndoStep CreateUndoStep() { FakeMoveNative.Calls.Add("Undo"); return new(); }
        public void Dispose() => FakeMoveNative.Calls.Add("ReleaseUndo");
    }
    internal sealed class UndoStep
    {
        public void SetUndoDescription(string description) => Assert.Equal("AI: eplan_placement_update", description);
        public void CloseUndo() => FakeMoveNative.Calls.Add("CloseUndo");
        public void DoUndo() { FakeMoveNative.Calls.Add("DoUndo"); FakeMoveNative.Value = 0; }
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
