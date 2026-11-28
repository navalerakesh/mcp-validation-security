using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using Mcp.Benchmark.CLI.Abstractions;
using Mcp.Benchmark.CLI.Models;
using Mcp.Benchmark.CLI.Utilities;

namespace Mcp.Benchmark.Tests.Unit.Utilities;

public sealed class WindowsJobInteropTests
{
    [Fact]
    public void Contract_IsInternalAndExposesOnlyOwnedProcessOperations()
    {
        var contract = typeof(IWindowsJobProcess);
        contract.IsNotPublic.Should().BeTrue();
        typeof(IDisposable).IsAssignableFrom(contract).Should().BeTrue();
        typeof(WindowsJobProcess).IsNotPublic.Should().BeTrue();
        contract.IsAssignableFrom(typeof(WindowsJobProcess)).Should().BeTrue();
        contract.GetProperties().Select(property => (property.Name, property.PropertyType)).Should().BeEquivalentTo(
            new[] { ("StandardOutput", typeof(Stream)), ("StandardError", typeof(Stream)), ("ActiveProcessCount", typeof(uint)) });
        contract.GetMethods().Where(method => !method.IsSpecialName).Select(method => method.Name)
            .Should().BeEquivalentTo("Resume", "TryGetExitCode", "Terminate");
    }

    [Theory]
    [InlineData("StartupInfo", 68, 104)]
    [InlineData("StartupInfoEx", 72, 112)]
    [InlineData("ProcessInformation", 16, 24)]
    [InlineData("JobBasicLimitInformation", 48, 64)]
    [InlineData("IoCounters", 48, 48)]
    [InlineData("JobExtendedLimitInformation", 112, 144)]
    [InlineData("JobBasicAccountingInformation", 48, 48)]
    public void NativeStructures_MatchWindowsAbiForCurrentPointerWidth(string name, int size32, int size64)
    {
        var nativeType = NestedType(name);
        nativeType.IsNestedPrivate.Should().BeTrue();
        nativeType.IsLayoutSequential.Should().BeTrue();
        Marshal.SizeOf(nativeType).Should().Be(IntPtr.Size == 8 ? size64 : size32);
    }

    [Theory]
    [InlineData("StartupInfo", "Flags", 44, 60)]
    [InlineData("StartupInfo", "StandardInput", 56, 80)]
    [InlineData("StartupInfo", "StandardError", 64, 96)]
    [InlineData("StartupInfoEx", "AttributeList", 68, 104)]
    [InlineData("ProcessInformation", "ProcessId", 8, 16)]
    [InlineData("JobBasicLimitInformation", "LimitFlags", 16, 16)]
    [InlineData("JobBasicLimitInformation", "MinimumWorkingSetSize", 20, 24)]
    [InlineData("JobBasicLimitInformation", "Affinity", 32, 48)]
    [InlineData("JobExtendedLimitInformation", "IoInfo", 48, 64)]
    [InlineData("JobExtendedLimitInformation", "ProcessMemoryLimit", 96, 112)]
    [InlineData("JobBasicAccountingInformation", "ActiveProcesses", 40, 40)]
    public void NativeFields_HaveExpectedOffsets(string structure, string field, int offset32, int offset64)
    {
        Marshal.OffsetOf(NestedType(structure), field).ToInt32().Should().Be(IntPtr.Size == 8 ? offset64 : offset32);
    }

    [Fact]
    public void NativeFields_UsePointerSizedHandlesAndFixedWidthCounters()
    {
        NestedType("JobBasicLimitInformation").GetField("Affinity")!.FieldType.Should().Be(typeof(nuint));
        NestedType("JobBasicLimitInformation").GetField("MinimumWorkingSetSize")!.FieldType.Should().Be(typeof(nuint));
        NestedType("JobBasicAccountingInformation").GetField("ActiveProcesses")!.FieldType.Should().Be(typeof(uint));
        NestedType("JobBasicAccountingInformation").GetField("TotalUserTime")!.FieldType.Should().Be(typeof(long));
        NestedType("StartupInfo").GetField("StandardInput")!.FieldType.Should().Be(typeof(IntPtr));
        NestedType("ProcessInformation").GetField("Process")!.FieldType.Should().Be(typeof(IntPtr));
    }

    [Fact]
    public void DeclaredFlags_RequireAtomicSuspendedJobAndExplicitHandleInheritanceWithoutBreakaway()
    {
        Constant<uint>("CreationFlags").Should().Be(0x00000004u | 0x00080000u);
        Constant<uint>("JobLimitFlags").Should().Be(0x00002000u);
        Constant<uint>("StartfUseStdHandles").Should().Be(0x00000100u);
        Constant<uint>("JobListAttribute").Should().Be(0x0002000Du);
        Constant<uint>("HandleListAttribute").Should().Be(0x00020002u);
        Constant<int>("BasicAccountingInformation").Should().Be(1);
        Constant<int>("ExtendedLimitInformation").Should().Be(9);
    }

    [Fact]
    public void NativeImports_AreExactWin32CallsWithErrorCaptureAndNoPidLookup()
    {
        var methods = NestedType("NativeMethods").GetMethods(BindingFlags.Static | BindingFlags.NonPublic);
        methods.Select(method => method.Name).Should().NotContain(new[] { "OpenProcess", "AssignProcessToJobObject", "ShellExecuteW" });
        foreach (var method in methods)
        {
            var import = method.GetCustomAttribute<DllImportAttribute>()!;
            import.Value.Should().Be("kernel32.dll");
            import.ExactSpelling.Should().BeTrue();
            import.SetLastError.Should().Be(method.Name != "DeleteProcThreadAttributeList");
        }
        var create = methods.Single(method => method.Name == "CreateProcessW");
        create.GetCustomAttribute<DllImportAttribute>()!.CharSet.Should().Be(CharSet.Unicode);
        create.GetParameters().Single(parameter => parameter.Name == "inheritHandles")
            .GetCustomAttribute<MarshalAsAttribute>()!.Value.Should().Be(UnmanagedType.Bool);
    }

    [Fact]
    public void NativeFailure_PreservesNumericCodeWithoutLaunchData()
    {
        var factory = typeof(WindowsJobProcess).GetMethod("NativeFailure", BindingFlags.NonPublic | BindingFlags.Static,
            null, [typeof(string), typeof(int)], null)!;
        var error = (Win32Exception)factory.Invoke(null, ["CreateProcessW", 5])!;
        error.NativeErrorCode.Should().Be(5);
        error.Message.Should().Be("CreateProcessW failed (Win32 error 5).");
    }

    [Fact]
    public void CommandPreparation_UsesExistingQuotingWithNoShellExpansion()
    {
        var executable = Path.Combine(Path.GetTempPath(), "fixture app.exe");
        string[] arguments = ["", "a b", "a\"b", "tail\\", "& echo secret", "$(secret)", "%SECRET%"];
        WindowsJobProcess.PrepareCommandLine(executable, arguments)
            .Should().Be(WindowsSupervisorProtocol.BuildCommandLine(executable, arguments));
        WindowsJobProcess.PrepareCommandLine(executable, []).Should().Be('"' + executable + '"');
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("relative-secret.exe")]
    [InlineData("secret\0.exe")]
    [InlineData("\"secret.exe\"")]
    public void CommandPreparation_RejectsInvalidApplicationWithoutEchoingInput(string executable)
    {
        var prepare = () => WindowsJobProcess.PrepareCommandLine(executable, []);
        prepare.Should().Throw<ArgumentException>().Which.Message.Should().NotContain("secret");
    }

    [Fact]
    public void CommandPreparation_RejectsNullOversizedOrMalformedArgumentsWithoutEchoingInput()
    {
        var executable = Path.Combine(Path.GetTempPath(), "fixture.exe");
        Action missingList = () => WindowsJobProcess.PrepareCommandLine(executable, null!);
        missingList.Should().Throw<ArgumentNullException>();
        foreach (var arguments in new[]
                 {
                     new string[] { null! }, new[] { "secret\0argument" }, new[] { new string('s', 32767) },
                     Enumerable.Repeat("secret", WindowsSupervisorProtocol.MaximumArguments + 1).ToArray()
                 })
        {
            var prepare = () => WindowsJobProcess.PrepareCommandLine(executable, arguments);
            prepare.Should().Throw<InvalidDataException>().Which.Message.Should().NotContain("secret");
        }
    }

    [UnsupportedWindowsFact]
    public void Factory_RejectsUnsupportedPlatformsBeforeNativeInterop()
    {
        var create = () => WindowsJobProcess.CreateSuspended("secret.exe", ["secret-input"]);
        create.Should().Throw<PlatformNotSupportedException>().Which.Message.Should().NotContain("secret");
    }

    [Fact]
    public void Dispose_BeforeNativeAllocationIsIdempotentAndRejectsFurtherOperations()
    {
        var owner = (IWindowsJobProcess)Activator.CreateInstance(typeof(WindowsJobProcess), nonPublic: true)!;
        owner.Dispose();
        owner.Dispose();
        Action[] operations =
        [
            owner.Resume, () => owner.Terminate(1), () => owner.TryGetExitCode(out _),
            () => _ = owner.ActiveProcessCount, () => _ = owner.StandardOutput, () => _ = owner.StandardError
        ];
        foreach (var operation in operations) operation.Should().Throw<ObjectDisposedException>();
    }

    private sealed class UnsupportedWindowsFactAttribute : FactAttribute
    {
        public UnsupportedWindowsFactAttribute()
        {
            if (OperatingSystem.IsWindowsVersionAtLeast(10))
                Skip = "Unsupported-platform rejection applies only outside Windows 10 and later.";
        }
    }

    private static Type NestedType(string name) => typeof(WindowsJobProcess).GetNestedType(name, BindingFlags.NonPublic)!;

    private static T Constant<T>(string name) =>
        (T)typeof(WindowsJobProcess).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
}