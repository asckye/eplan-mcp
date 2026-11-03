using System.Globalization;
using EplanMcp.Contracts.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace EplanMcp.Host.Tests.Logging;

[Trait("Size", "Small")]
public sealed class RedactingConsoleFormatterTests
{
    [Fact]
    public void Write_RedactsFormattedMessageAndException()
    {
        const string message = "password=abc123 Authorization: Bearer abc.def https://user:pw@host/";
        var exception = new InvalidOperationException("token=exception-token");
        using ServiceProvider services = Configure(redacting: true);

        string output = Write(services, LogLevel.Error, message, exception);

        Assert.Contains(SecretRedactor.Placeholder, output, StringComparison.Ordinal);
        foreach (string credential in new[] { "abc123", "abc.def", "user:pw", "exception-token" })
        {
            Assert.DoesNotContain(credential, output, StringComparison.Ordinal);
        }
        Assert.Contains("https://[REDACTED]@host/", output, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException: token=[REDACTED]", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("password=", "abc123")]
    [InlineData("Authorization: Bearer", "abc.def")]
    public void Write_RedactsCredentialsAfterJoiningLines(string prefix, string credential)
    {
        using ServiceProvider services = Configure(redacting: true);
        string message = prefix + Environment.NewLine + credential;

        string output = Write(services, LogLevel.Error, message, new InvalidOperationException(message));

        Assert.DoesNotContain(credential, output, StringComparison.Ordinal);
        Assert.Contains(SecretRedactor.Placeholder, output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(LogLevel.Trace)]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.Information)]
    [InlineData(LogLevel.Warning)]
    [InlineData(LogLevel.Error)]
    [InlineData(LogLevel.Critical)]
    public void Write_OrdinaryTextMatchesSimpleConsoleExactly(LogLevel level)
    {
        using ServiceProvider actual = Configure(redacting: true);
        using ServiceProvider expected = Configure(redacting: false);
        foreach (string message in new[] { "Bridge ready: 2 requests.", "line one" + Environment.NewLine + "line two", "" })
        {
            Assert.Equal(Write(expected, level, message), Write(actual, level, message));
            var exception = new InvalidOperationException("Ordinary failure.", new IOException("Inner failure."));
            Assert.Equal(Write(expected, level, message, exception), Write(actual, level, message, exception));
        }
    }

    [Fact]
    public void ConfigureLogging_AllLevelsGoToStandardError()
    {
        using ServiceProvider services = Configure(redacting: true);

        Assert.Equal(LogLevel.Trace, services.GetRequiredService<IOptions<ConsoleLoggerOptions>>().Value.LogToStandardErrorThreshold);
    }

    private static ServiceProvider Configure(bool redacting)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            if (redacting)
            {
                Program.ConfigureLogging(logging, services);
            }
            else
            {
                logging.AddSimpleConsole(options =>
                {
                    options.SingleLine = true;
                    options.ColorBehavior = LoggerColorBehavior.Disabled;
                });
            }
        });
        return services.BuildServiceProvider();
    }

    private static string Write(ServiceProvider services, LogLevel level, string message, Exception? exception = null)
    {
        string? name = services.GetRequiredService<IOptions<ConsoleLoggerOptions>>().Value.FormatterName;
        ConsoleFormatter formatter = services.GetServices<ConsoleFormatter>().Single(value => value.Name == name);
        var entry = new LogEntry<string>(level, "Test.Category", new EventId(17), message, exception, static (state, _) => state);
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        formatter.Write(in entry, null, writer);
        return writer.ToString();
    }
}
