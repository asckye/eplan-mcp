namespace EplanMcp.Spikes.S37.Driver;

internal static class Checks
{
    internal static int Run()
    {
        int checks = 0;
        void Equal(string expected, string actual)
        {
            if (!string.Equals(expected, actual, StringComparison.Ordinal)) { throw new InvalidOperationException("Mismatch: " + actual); }
            checks++;
        }

        void Refused(string path, string extension = ".elk")
        {
            try { ScratchPaths.Require(path, extension); }
            catch (ArgumentException) { checks++; return; }
            throw new InvalidOperationException("Unsafe path accepted: " + path);
        }

        string root = ScratchPaths.Root;
        Equal(Path.GetFullPath(root + "fixture.elk"), ScratchPaths.Require(root + "a\\..\\fixture.elk", ".elk"));
        Equal(Path.GetFullPath(root.ToUpperInvariant() + "FIXTURE.ELK"), ScratchPaths.Require(root.ToUpperInvariant() + "FIXTURE.ELK", ".elk"));
        Refused(root.TrimEnd('\\') + "-outside\\project.elk");
        Refused(root + "..\\project.elk");
        Refused("project.elk");
        Refused(root + "project.elk:payload.elk");
        Refused(root + "folder.\\project.elk");
        Refused(root + "folder \\project.elk");
        Refused(root + "results.txt", ".jsonl");
        Refused(root + "project.edb\\result.jsonl", ".jsonl");
        Refused(root + "project.edb\\macro.emp", ".emp");
        Equal("=S37C1.EC1+S37C1.A3&EFS1/1", FixtureNames.Target("=EC1+A3&EFS1/1", "EC1", "A3", "S37C1"));
        Equal("=S37C2.F.SUB+S37C2.A2.B&EFS1/2.1", FixtureNames.Target("=F.SUB+A2.B&EFS1/2.1", "F", "A2.B", "S37C2"));
        Equal("=S37C3+S37C3.A1&EFS1/1", FixtureNames.Target("+A1&EFS1/1", "", "A1", "S37C3"));
        Equal("=S37C5.EC1+S37C5&EFS1/1", FixtureNames.Target("=EC1&EFS1/1", "EC1", "", "S37C5"));
        Equal("==ASSIGN=S37C4.EC1+S37C4.A3&EFS1/1", FixtureNames.Target("==ASSIGN=EC1+A3&EFS1/1", "EC1", "A3", "S37C4"));
        Equal("==EC1=S37C4.EC1+S37C4.A3&EFS1/1", FixtureNames.Target("==EC1=EC1+A3&EFS1/1", "EC1", "A3", "S37C4"));
        if (FixtureNames.Target("=EC1+A3&EFS1/1", "EC1", "A3", "S37C1") == FixtureNames.Target("=EC2+A3&EFS1/1", "EC2", "A3", "S37C1"))
        { throw new InvalidOperationException("Distinct source plants collapsed."); }
        checks++;
        Console.WriteLine("Passed " + checks.ToString(System.Globalization.CultureInfo.InvariantCulture) + " scratch-path and naming checks; no EPLAN API loaded.");
        return 0;
    }
}
