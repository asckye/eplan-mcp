using System.Globalization;

namespace EplanMcp.Host.Audit;

/// <summary><c>eplanmcp audit verify</c>: checks the audit hash chain and exits 1 where it breaks (Q9).</summary>
internal static class AuditCommand
{
    public static int Verify(TextWriter output, CultureInfo culture, HostDataLocations locations)
    {
        bool chinese = culture.TwoLetterISOLanguageName == "zh";
        if (!Directory.Exists(locations.Audit))
        {
            output.WriteLine(chinese ? $"没有审计记录：{locations.Audit}" : $"No audit records: {locations.Audit}");
            return 0;
        }

        (int records, AuditBreak? broken) = AuditVerifier.Verify(locations.Audit);
        if (broken is null)
        {
            output.WriteLine(chinese ? $"审计哈希链完整：{records} 条记录" : $"The audit chain is intact: {records} records");
            return 0;
        }

        string where = $"{broken.File}:{broken.Line.ToString(CultureInfo.InvariantCulture)}";
        output.WriteLine(broken.Problem switch
        {
            AuditProblem.Changed when chinese => $"审计哈希链在 {where} 断开：这条记录被改动过",
            AuditProblem.Changed => $"The audit chain breaks at {where}: the record was changed",
            AuditProblem.Missing when chinese => $"审计哈希链在 {where} 断开：它之前的记录被删除，或者它是插入的",
            AuditProblem.Missing => $"The audit chain breaks at {where}: a record before it was removed, or it was inserted",
            _ when chinese => $"审计哈希链在 {where} 断开：这一行不是审计记录",
            _ => $"The audit chain breaks at {where}: the line is not an audit record",
        });
        output.WriteLine(chinese ? $"之前的 {records} 条记录完整。" : $"The {records} records before it are intact.");
        return 1;
    }
}
