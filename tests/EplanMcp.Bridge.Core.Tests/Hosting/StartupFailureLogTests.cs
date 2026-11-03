using System.Globalization;
using System.Reflection;
using EplanMcp.Bridge.Eplan.Hosting;

namespace EplanMcp.Bridge.Core.Tests.Hosting;

[Trait("Size", "Small")]
public sealed class StartupFailureLogTests
{
    [Fact]
    public void Describe_RedactsCompleteExceptionText()
    {
        Exception exception = CaptureException();

        string text = StartupFailureLog.Describe(exception);

        Assert.DoesNotContain("abc123", text, StringComparison.Ordinal);
        Assert.DoesNotContain("abc.def", text, StringComparison.Ordinal);
        Assert.DoesNotContain("user:pw", text, StringComparison.Ordinal);
        Assert.DoesNotContain("inner-secret", text, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", text, StringComparison.Ordinal);
        Assert.Contains(exception.StackTrace!, text, StringComparison.Ordinal);
        Assert.Contains(exception.InnerException!.StackTrace!, text, StringComparison.Ordinal);
        MethodInfo redactor = typeof(StartupFailureLog).GetMethod("Redact", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.True((redactor.MethodImplementationFlags & MethodImplAttributes.NoInlining) != 0);
    }

    [Fact]
    public void Describe_OrdinaryException_PreservesFullText()
    {
        var exception = new InvalidOperationException("Bridge could not start.");

        Assert.Equal(exception.ToString(), StartupFailureLog.Describe(exception));
    }

    [Theory]
    [InlineData(typeof(FileNotFoundException))]
    [InlineData(typeof(FileLoadException))]
    [InlineData(typeof(TypeLoadException))]
    [InlineData(typeof(BadImageFormatException))]
    [InlineData(typeof(MissingMethodException))]
    [InlineData(typeof(TypeInitializationException))]
    public void Describe_RedactionUnavailable_OmitsEveryMessageButKeepsDiagnostics(Type failureType)
    {
        Exception exception = CaptureException();

        string text = StartupFailureLog.Describe(exception, _ => throw CreateLoadFailure(failureType));

        Assert.StartsWith("redaction unavailable; messages omitted", text, StringComparison.Ordinal);
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            Assert.DoesNotContain(current.Message, text, StringComparison.Ordinal);
            Assert.Contains(current.GetType().FullName!, text, StringComparison.Ordinal);
            Assert.Contains("HResult=0x" + current.HResult.ToString("X8", CultureInfo.InvariantCulture), text, StringComparison.Ordinal);
            Assert.Contains(current.StackTrace!, text, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("abc123", text, StringComparison.Ordinal);
        Assert.DoesNotContain("abc.def", text, StringComparison.Ordinal);
        Assert.DoesNotContain("user:pw", text, StringComparison.Ordinal);
        Assert.DoesNotContain("inner-secret", text, StringComparison.Ordinal);
    }

    private static Exception CreateLoadFailure(Type type) => type == typeof(TypeInitializationException)
        ? new TypeInitializationException("Redactor", new FileNotFoundException())
        : (Exception)Activator.CreateInstance(type)!;

    private static InvalidOperationException CaptureException()
    {
        try
        {
            try
            {
                throw new ArgumentException("token=inner-secret");
            }
            catch (ArgumentException inner)
            {
                throw new InvalidOperationException("password=abc123 Authorization: Bearer abc.def https://user:pw@host/", inner);
            }
        }
        catch (InvalidOperationException exception)
        {
            return exception;
        }
    }
}
