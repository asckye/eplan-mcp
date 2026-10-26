using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PropertySetExecuteTests
{
    internal static PropertySetChange Technical(string text = " DC 24V;I/O@板 ") => new()
    {
        Target = new PropertyTarget(deviceTag: "=AP-K1"),
        Property = new PropertySelector(propertyId: 20027),
        Value = new PropertyTypedValue("text", text: text),
    };

    internal static PropertySetArgs Args(List<PropertySetChange>? changes = null)
    {
        changes ??= [PropertySetPlannerTests.Change(), Technical()];
        using var planSession = new FakePropertySetSession();
        return new PropertySetArgs
        {
            Project = PropertySetPlannerTests.Project,
            Changes = changes,
            Approved = planSession.Plan(changes),
        };
    }

    internal static PropertySetData Execute(FakePropertySetSession session, FakeTransactions transactions, PropertySetArgs? args = null) =>
        (PropertySetData)new PropertySetExecuteOperation(_ => session, transactions, 2026).Execute(args ?? Args())!;

    [Fact]
    public void Execute_B1_MergesTranslationsWritesLiteral20027AndCommitsOnce()
    {
        using var session = new FakePropertySetSession();
        var transactions = new FakeTransactions();
        PropertySetData data = Execute(session, transactions);

        Assert.Equal(ExecutionState.Committed, data.Execution);
        Assert.Equal("AI: eplan_property_set", data.Undo);
        Assert.Equal(2, data.Plan.Changes.Count);
        Assert.Equal("输送; @电机", session.Objects[1].Properties[20011].Texts["zh_CN"]);
        Assert.Equal("Antrieb", session.Objects[1].Properties[20011].Texts["de_DE"]);
        Assert.Equal("independent", session.Objects[1].Properties[20011].Texts["??_??"]);
        Assert.Equal(" DC 24V;I/O@板 ", session.Objects[1].Properties[20027].Display);
        Assert.Equal("14 W", session.Objects[2].Properties[20027].Display);
        Assert.Equal("旧文本", session.Objects[2].Properties[20011].Texts["zh_CN"]);
        Assert.Equal(["Begin AI: eplan_property_set", "Commit", "CloseUndo", "Dispose"], transactions.Calls);
        Assert.All(session.Objects.Values, obj => Assert.Equal(4, obj.ValueRequests.Count));
        Assert.Empty(PropertyWriteWhitelist.Entries);
        session.AssertDisposed();
    }

    [Theory]
    [InlineData("fingerprint")]
    [InlineData("guard")]
    [InlineData("translation")]
    [InlineData("auxiliary")]
    [InlineData("parts")]
    public void Execute_StaleEvidence_RefusesBeforeTransaction(string drift)
    {
        using var session = new FakePropertySetSession();
        var transactions = new FakeTransactions();
        PropertySetArgs args = Args();
        switch (drift)
        {
            case "fingerprint":
                args.Approved.Plan.Fingerprint += "stale";
                break;
            case "guard":
                args.Approved.Guards[0].Parts = ["tampered"];
                break;
            case "translation":
                session.Objects[1].Properties[20011].Texts["de_DE"] = "changed";
                break;
            case "auxiliary":
                session.Inventory[1].Page = "other";
                break;
            case "parts":
                session.Inventory[0].Parts = ["changed"];
                break;
        }

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Execute(session, transactions, args));
        Assert.Equal(ErrorReason.PlanStale, error.Error.Reason);
        Assert.Equal(ExecutionState.NotStarted, error.Error.Execution);
        Assert.Empty(transactions.Calls);
        Assert.Equal(0, session.Writes);
        session.AssertDisposed();
    }

    [Theory]
    [InlineData("written_text")]
    [InlineData("written_technical")]
    [InlineData("translation")]
    [InlineData("auxiliary")]
    [InlineData("identity")]
    [InlineData("main_identity")]
    [InlineData("definition")]
    [InlineData("parts")]
    [InlineData("empty_auxiliary")]
    public void Execute_ReadbackMismatch_RollsBackEveryWrittenItem(string mismatch)
    {
        using var session = new FakePropertySetSession();
        var transactions = new FakeTransactions { Snapshot = session.RestorePoint };
        session.AfterWrite = _ =>
        {
            if (session.Writes != 2)
            {
                return;
            }

            switch (mismatch)
            {
                case "written_text":
                    session.Objects[1].Properties[20011].Texts["zh_CN"] = "lost";
                    break;
                case "written_technical":
                    session.Objects[1].Properties[20027].Display = "24 V";
                    break;
                case "translation":
                    session.Objects[1].Properties[20011].Texts.Remove("de_DE");
                    break;
                case "auxiliary":
                    session.Objects[2].Properties[20027].Display = "changed";
                    break;
                case "identity":
                    session.Inventory[1].Pos = "9,9";
                    break;
                case "main_identity":
                    session.Inventory[0].Name = "=AP-changed";
                    break;
                case "definition":
                    session.Objects[1].Properties[20011].Metadata.IsReadOnly = true;
                    break;
                case "parts":
                    session.Inventory[0].Parts = ["changed"];
                    break;
                case "empty_auxiliary":
                    session.Objects[2].Properties[20011].Empty = true;
                    break;
            }
        };

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Execute(session, transactions));
        Assert.Equal(ErrorReason.EplanOperationFailed, error.Error.Reason);
        Assert.Equal(ExecutionState.RolledBack, error.Error.Execution);
        Assert.Equal("旧文本", session.Objects[1].Properties[20011].Texts["zh_CN"]);
        Assert.Equal("14 W", session.Objects[1].Properties[20027].Display);
        Assert.Equal(["Begin AI: eplan_property_set", "CloseUndo", "Rollback", "Dispose"], transactions.Calls);
        session.AssertDisposed();
    }

    [Theory]
    [InlineData("write")]
    [InlineData("readback")]
    [InlineData("release")]
    [InlineData("cancel")]
    public void Execute_ExceptionOrCancellation_ReleasesAllListsAndRollsBack(string stage)
    {
        using var session = new FakePropertySetSession();
        var transactions = new FakeTransactions { Snapshot = session.RestorePoint };
        var context = new OperationContext("write", true, null);
        session.AfterWrite = _ =>
        {
            if (session.Writes == 2)
            {
                if (stage == "write")
                {
                    throw new InvalidOperationException("setter failed");
                }

                if (stage == "cancel")
                {
                    context.Cancel();
                }

                if (stage == "readback")
                {
                    session.BeforeInventory = () => throw new InvalidOperationException("inventory failed");
                }
            }
        };
        if (stage == "release")
        {
            session.OnDispose = () => throw new InvalidOperationException("release failed");
        }

        Assert.ThrowsAny<Exception>(() => context.Run(() => Execute(session, transactions)));
        Assert.Contains("Rollback", transactions.Calls);
        Assert.DoesNotContain("Commit", transactions.Calls);
        Assert.Equal("旧文本", session.Objects[1].Properties[20011].Texts["zh_CN"]);
        Assert.Equal("14 W", session.Objects[1].Properties[20027].Display);
        session.AssertDisposed();
    }

    [Theory]
    [InlineData("property.opened")]
    [InlineData("definition.opened")]
    [InlineData("metadata")]
    [InlineData("value.opened")]
    [InlineData("texts.opened")]
    [InlineData("text")]
    [InlineData("display")]
    public void Execute_PlanningException_ReleasesPartialResourcesWithoutTransaction(string stage)
    {
        using var session = new FakePropertySetSession();
        session.Objects[2].OnAccess = access =>
        {
            if (access == stage)
            {
                throw new InvalidOperationException("getter failed");
            }
        };
        var transactions = new FakeTransactions();
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Execute(session, transactions));
        Assert.Equal(ExecutionState.NotStarted, error.Error.Execution);
        Assert.Empty(transactions.Calls);
        session.AssertDisposed();
    }

    [Theory]
    [InlineData("Commit")]
    [InlineData("Rollback")]
    public void Execute_TransactionFailure_ReportsUnknown(string failAt)
    {
        using var session = new FakePropertySetSession();
        var transactions = new FakeTransactions { FailAt = failAt };
        if (failAt == "Rollback")
        {
            session.AfterWrite = _ => throw new InvalidOperationException("write failed");
        }

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Execute(session, transactions));
        Assert.Equal(ErrorReason.OutcomeUnknown, error.Error.Reason);
        Assert.Equal(ExecutionState.Unknown, error.Error.Execution);
        session.AssertDisposed();
    }

    [Fact]
    public void Execute_CloseUndoFailure_ReportsCommittedWarning()
    {
        using var session = new FakePropertySetSession();
        PropertySetData data = Execute(session, new FakeTransactions { FailAt = "CloseUndo" });
        Assert.Equal(ExecutionState.Committed, data.Execution);
        Assert.NotNull(data.Warning);
        session.AssertDisposed();
    }

    [Fact]
    public void Execute_NoChanges_DoesNotOpenTransactionOrLeaveUndo()
    {
        using var session = new FakePropertySetSession();
        var transactions = new FakeTransactions();
        PropertySetData data = Execute(session, transactions, Args([PropertySetPlannerTests.Change("旧文本"), Technical("14 W")]));
        Assert.Equal(ExecutionState.NotStarted, data.Execution);
        Assert.Null(data.Undo);
        Assert.Empty(transactions.Calls);
        session.AssertDisposed();
    }

    [Theory]
    [InlineData(20011)]
    [InlineData(20027)]
    public void Execute_ClearOneValue_AllowsNativeEmptyAndPreservesOtherValues(int id)
    {
        using var session = new FakePropertySetSession();
        PropertySetChange change = id == 20011 ? PropertySetPlannerTests.Change("") : Technical("");
        PropertySetData data = Execute(session, new FakeTransactions(), Args([change]));
        Assert.Equal(ExecutionState.Committed, data.Execution);
        Assert.Equal("Antrieb", session.Objects[1].Properties[20011].Texts["de_DE"]);
        Assert.Equal("旧文本", session.Objects[2].Properties[20011].Texts["zh_CN"]);
        session.AssertDisposed();
    }

    [Theory]
    [InlineData("14 W")]
    [InlineData("DC 24V")]
    [InlineData("I/O Board")]
    [InlineData("de_DE@literal;en_US@literal;")]
    [InlineData("\t 中文; @ \n")]
    public void Plan_20027_UsesLiteralIndependentTextAndItsOwnDefinition(string text)
    {
        using var session = new FakePropertySetSession();
        PropertySetPlan plan = session.Plan([Technical(text)]);
        Assert.Equal(text, PropertyValueComparison.TextIn(plan.Guards[0].After, TextLanguages.Independent));
        Assert.Equal("opaque", plan.Guards[0].Definition.ValueType);
        Assert.Equal(40, plan.Guards[0].Definition.MaximumLength);
        Assert.All(session.Objects.Values, obj => Assert.Equal(0, obj.StoredReads));
        Assert.Empty(PropertyWriteWhitelist.Entries);
    }

    [Fact]
    public void Plan_20027_RejectsLanguageAndOwnLengthLimit()
    {
        using var session = new FakePropertySetSession();
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() =>
            session.Plan([Technical(new string('x', 41))])).Error.Reason);
        PropertySetChange change = Technical();
        change.Language = "zh_CN";
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => session.Plan([change])).Error.Reason);
    }

    [Theory]
    [InlineData("planning", ExecutionState.NotStarted)]
    [InlineData("writing", ExecutionState.RolledBack)]
    [InlineData("release", ExecutionState.RolledBack)]
    public void Execute_TimeBudgetExceeded_RefusesOrRollsBackBeforeCommit(string stage, string expected)
    {
        using var session = new FakePropertySetSession();
        var transactions = new FakeTransactions { Snapshot = session.RestorePoint };
        TimeSpan elapsed = stage == "planning" ? TimeSpan.FromSeconds(31) : TimeSpan.Zero;
        if (stage == "release")
        {
            session.OnDispose = () => elapsed = TimeSpan.FromSeconds(31);
        }
        else
        {
            session.AfterWrite = _ => elapsed = TimeSpan.FromSeconds(31);
        }

        var operation = new PropertySetExecuteOperation(_ => session, transactions, 2026, () => elapsed);
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => operation.Execute(Args()));
        Assert.Equal(ErrorReason.Timeout, error.Error.Reason);
        Assert.Equal(expected, error.Error.Execution);
        Assert.DoesNotContain("Commit", transactions.Calls);
        Assert.Equal("旧文本", session.Objects[1].Properties[20011].Texts["zh_CN"]);
        Assert.Equal(1, session.Disposals);
        Assert.All(session.Objects.Values.SelectMany(o => o.Resources), r => Assert.Equal(1, r.Disposals));
    }

    [Theory]
    [InlineData("type")]
    [InlineData("format")]
    [InlineData("read_only")]
    [InlineData("licence")]
    public void Plan_20027_RuntimeDefinitionDrift_NeverReadsTheValue(string drift)
    {
        using var session = new FakePropertySetSession();
        PropertyDefinitionMetadata definition = session.Objects[1].Properties[20027].Metadata;
        switch (drift)
        {
            case "type":
                definition.ValueType = "number";
                break;
            case "format":
                definition.Format = null;
                break;
            case "read_only":
                definition.IsReadOnly = true;
                break;
            case "licence":
                definition.IsPropertyLicensed = false;
                break;
        }

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => session.Plan([Technical()]));
        Assert.Equal(drift == "licence" ? ErrorReason.LicenceMissing : ErrorReason.PolicyDenied, error.Error.Reason);
        Assert.Empty(session.Objects[1].ValueRequests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Execute_MultipleObjects_SecondWriteExceptionRestoresBothOrCommitsTogether(bool fail)
    {
        using var session = new FakePropertySetSession();
        using var planning = new FakePropertySetSession();
        foreach (FakePropertySetSession work in new[] { session, planning })
        {
            work.Inventory[1].Main = true;
            work.Inventory[1].Name = "=AP-K2";
        }

        PropertySetChange second = Technical();
        second.Target = new PropertyTarget(deviceTag: "=AP-K2");
        var changes = new List<PropertySetChange> { PropertySetPlannerTests.Change(), second };
        var args = new PropertySetArgs
        {
            Project = PropertySetPlannerTests.Project,
            Changes = changes,
            Approved = planning.Plan(changes),
        };
        var transactions = new FakeTransactions { Snapshot = session.RestorePoint };
        session.AfterWrite = _ =>
        {
            if (fail && session.Writes == 2)
            {
                throw new InvalidOperationException("second object's setter failed");
            }
        };

        if (fail)
        {
            Assert.Throws<InvalidOperationException>(() => Execute(session, transactions, args));
            Assert.Equal("旧文本", session.Objects[1].Properties[20011].Texts["zh_CN"]);
            Assert.Equal("14 W", session.Objects[2].Properties[20027].Display);
            Assert.Contains("Rollback", transactions.Calls);
        }
        else
        {
            Assert.Equal(ExecutionState.Committed, Execute(session, transactions, args).Execution);
            Assert.Equal("输送; @电机", session.Objects[1].Properties[20011].Texts["zh_CN"]);
            Assert.Equal(" DC 24V;I/O@板 ", session.Objects[2].Properties[20027].Display);
            Assert.Single(transactions.Calls, c => c == "Commit");
        }

        session.AssertDisposed();
    }

    [Fact]
    public void Execute_OpeningSessionOrBeginningTransactionFails_IsNotStarted()
    {
        var transactions = new FakeTransactions();
        var opening = new PropertySetExecuteOperation(_ => throw new InvalidOperationException("open failed"), transactions, 2026);
        Assert.Equal(ExecutionState.NotStarted, Assert.Throws<BridgeErrorException>(() => opening.Execute(Args())).Error.Execution);
        Assert.Empty(transactions.Calls);
        using var session = new FakePropertySetSession();
        transactions.FailAt = "Begin";
        Assert.Equal(ExecutionState.NotStarted, Assert.Throws<BridgeErrorException>(() => Execute(session, transactions)).Error.Execution);
        Assert.Equal(0, session.Writes);
        session.AssertDisposed();
    }

    [Theory]
    [InlineData(20024)]
    [InlineData(20022)]
    [InlineData(3012)]
    public void Execute_OutsideB1_RefusesBeforeTransactionAndPropertyAccess(int id)
    {
        using var session = new FakePropertySetSession();
        var transactions = new FakeTransactions();
        PropertySetArgs args = Args([PropertySetPlannerTests.Change()]);
        args.Changes[0].Property = new PropertySelector(propertyId: id);

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Execute(session, transactions, args));

        Assert.Equal(ErrorReason.PolicyDenied, error.Error.Reason);
        Assert.Equal(ExecutionState.NotStarted, error.Error.Execution);
        Assert.Equal(1, session.Disposals);
        Assert.Equal(0, session.Writes);
        Assert.Empty(transactions.Calls);
        Assert.All(session.Objects.Values, adapter => Assert.Equal(0, adapter.Opens));
    }
}
