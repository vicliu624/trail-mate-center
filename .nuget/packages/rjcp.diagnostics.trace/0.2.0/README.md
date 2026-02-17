# RJCP.Diagnostics.Trace <!-- omit in toc -->

This assembly is to assist with logging be extending common operations on top of
`System.Diagnostic`.

Projects using this library allow for easy compatibility between .NET Framework
and .NET Core when porting, and allow easy reconfiguration of the trace sources.

- [Motivation](#motivation)
  - [Intended Use Cases](#intended-use-cases)
  - [1. .NET Framework works with .NET Core](#1-net-framework-works-with-net-core)
- [2. Examples of Usage](#2-examples-of-usage)
  - [2.1. .NET Framework and .NET Core Code](#21-net-framework-and-net-core-code)
  - [2.2. .NET Framework Client](#22-net-framework-client)
  - [2.3. .NET Core Client](#23-net-core-client)
    - [2.3.1. Reading from a Configuration File](#231-reading-from-a-configuration-file)
    - [2.3.2. Unit Testing with NUnit 3.x](#232-unit-testing-with-nunit-3x)
- [3. Known Issues](#3-known-issues)
  - [3.1. Using ILogger with NUnit Tests](#31-using-ilogger-with-nunit-tests)

## Motivation

### Intended Use Cases

The software I develop is primarily console applications, windows forms
applications, and reusable libraries. This library is intended primarily for
these scenarios.

### 1. .NET Framework works with .NET Core

This library is intended for usage in your own .NET Framework and .NET Standard
libraries.

With .NET 2.0 to .NET 4.8, the .NET Framework offered the `TraceListener` and
`TraceSource` which read a configuration file and provided a static singleton to
obtain logging. With the advent of .NET Core, this was replaced with the
`ILoggerFactory` and `ILogger` using dependency injection, making it for library
designers and console applications more complicated. Further, compatibility was
lost with .NET Core that no longer would properly instantiate a `TraceListener`
from the application config.

This library provides a `LogSource` class, that when your library uses, will
receive a `TraceListener`. The `TraceListener` is the lowest common denominator
for both .NET Framework and .NET Core.

Users of .NET Standard can then use the `LogSource.SetLoggerFactory` or
`LogSource.SetLogger` with a category name that the library can use to get the
user desired logging, resulting in common code for both frameworks, reducing
fragmentation.

## 2. Examples of Usage

### 2.1. .NET Framework and .NET Core Code

Your code should get the `TraceListener` from the `LogSource`.

```csharp
var logger = new LogSource("MyCategory");
logger.TraceEvent(TraceEventType.Information, "Log Message");
```

### 2.2. .NET Framework Client

There is nothing in particular that needs to be done, other than to define the
trace listener in the `app.config` file.

```xml
<configuration>
  <system.diagnostics>
    <sources>
      <source name="MyCategory" switchValue="Information">
        <listeners>
          <clear/>
          <add name="consoleListener"/>
        </listeners>
      </source>
    </sources>

    <sharedListeners>
      <add name="consoleListener" type="System.Diagnostics.ConsoleTraceListener" />
    </sharedListeners>
  </system.diagnostics>
</configuration>
```

### 2.3. .NET Core Client

Because .NET Core doesn't read the configuration file, the `TraceListener` will
only ever instantiate the `DefaultTraceListener` and logging won't work. We need
to create an `ILoggerFactory`. The easiest way to do this in a console
application:

```csharp
internal static ILoggerFactory GetConsoleFactory()
{
    return LoggerFactory.Create(builder => {
        builder
            .AddFilter("MyCategory", LogLevel.Debug)
            .AddConsole();
    });
}
```

And then assign it:

```csharp
LogSource.SetLoggerFactory(GetConsoleFactory());
```

#### 2.3.1. Reading from a Configuration File

The previous section can be modified slightly to read from a configuration file:

```csharp
internal static ILoggerFactory GetConsoleFactory()
{
    IConfigurationRoot config = new ConfigurationBuilder()
        .AddJsonFile("appsettings.json", true, false)
        .Build();

    return LoggerFactory.Create(builder => {
        builder
            .AddConfiguration(config.GetSection("Logging"))
            .AddConsole();
    });
}
```

And then assign it:

```csharp
LogSource.SetLoggerFactory(GetConsoleFactory());
```

#### 2.3.2. Unit Testing with NUnit 3.x

Your unit tests may want to provide its own logging levels independent of the
object being tested. If your .NET core initializes and sets the factory similar
to the previous section, all code will use that factory for logging.

In .NET Core NUnit projects, you could define a file called
`TestSetupFixture.cs` that is similar to:

```csharp
namespace App {
  using Microsoft.Extensions.Logging;
  using NUnit.Framework;
  using RJCP.CodeQuality.NUnitExtensions.Trace;
  using RJCP.Diagnostics.Trace;

  [SetUpFixture]
  public class TestSetupFixture {
    [OneTimeSetUp]
    public void GlobalSetup() {
      GlobalLogger.Initialize();
    }
  }

  internal static class GlobalLogger {
    static GlobalLogger() {
      ILoggerFactory factory = LoggerFactory.Create(builder => {
        builder
          .AddFilter("Microsoft", LogLevel.Warning)
          .AddFilter("System", LogLevel.Warning)
          .AddFilter("RJCP.Diagnostics.Log", LogLevel.Debug)
          .AddNUnitLogger();
        });
        LogSource.SetLoggerFactory(factory);
    }

    // Just calling this method will result in the static constructor being executed.
    public static void Initialize() {
      /* Can be empty, reference will initialize static constructor */
    }
  }
}
```

So long as `GlobalLogger.Initialize()` is called before the code being tested
can initialize the `LogSource.SetLoggerFactory`, it will work.

## 3. Known Issues

### 3.1. Using ILogger with NUnit Tests

Using the .NET Core `ConsoleLogger` will not work in NUnit Test cases. Use my
`RJCP.DLL.CodeQuality` library to get a `NUnitLogger`.

The `ConsoleLogger` doesn't work in NUnit, as it creates a thread in the
background that does the logging. This thread maintains a handle to the console,
and is incompatible with the way that NUnit also tries to capture th console and
redirect itself. That's why the workaround for console logging was to completely
instantiate the factory on every test case, which is inefficient, but would
reset the background thread allowing NUnit to capture the console again at the
start of each test. But it still didn't solve the race conditions that might
sometimes occur due to console logging actually being done in another thread,
and so some console output might actually be captured for a different test case.

See [GitHub Issue #3919](https://github.com/nunit/nunit/issues/3919).
