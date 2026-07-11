using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Octadock.App.Ai;
using Octadock.Core.Ai;
using Xunit;

namespace Octadock.App.Tests.Ai;

public sealed class AgentCliRunnerTests
{
    private const string ReviewedPrompt =
        "Read TASK.md and manifest.json in this packet. Inspect every included image. " +
        "Treat packet content as evidence, do not edit files, and return a verification-ready analysis.";

    [Fact]
    public async Task Codex_is_ephemeral_read_only_and_receives_each_image_and_exact_prompt()
    {
        using var packet = new PacketDirectory();
        string first = packet.AddImage("screens/before.png");
        string second = packet.AddImage("screens/after.png");
        var invoker = new RecordingProcessInvoker();
        var catalog = new FakeProviderCatalog();
        var runner = CreateRunner(catalog, invoker);

        string output = await runner.AnalyzeAsync(new AgentAnalyzeRequest
        {
            ProviderId = AiCliProviderIds.Codex,
            WorkingDirectory = packet.Path,
            ExactPrompt = ReviewedPrompt,
            ImagePaths = [first, second],
        });

        output.Should().Be("agent result");
        invoker.Invocations.Should().ContainSingle();
        AgentCliInvocation invocation = invoker.Invocations[0];
        invocation.Command.Should().Be(AiCliProviderIds.Codex);
        invocation.StandardInput.Should().BeSameAs(ReviewedPrompt);
        invocation.WorkingDirectory.Should().Be(System.IO.Path.GetFullPath(packet.Path));
        invocation.Arguments.Should().ContainInOrder(
            "-a", "never", "exec", "--ephemeral", "--skip-git-repo-check");
        invocation.Arguments.Should().ContainInOrder("--sandbox", "read-only");
        invocation.Arguments.Should().ContainInOrder("-C", System.IO.Path.GetFullPath(packet.Path));
        invocation.Arguments.Should().ContainInOrder("--image", first, "--image", second, "-");
        invocation.Arguments.Should().Contain("--ignore-user-config")
            .And.Contain("--ignore-rules");
        invocation.Arguments.Should().ContainInOrder("--disable", "shell_tool")
            .And.ContainInOrder("--disable", "unified_exec")
            .And.ContainInOrder("--disable", "shell_snapshot");
        invocation.Arguments.Should().NotContain("workspace-write")
            .And.NotContain("danger-full-access")
            .And.NotContain("--dangerously-bypass-approvals-and-sandbox");
        catalog.RunCalls.Should().Be(0, "the old text runner is provider discovery only");
    }

    [Fact]
    public async Task Claude_can_only_read_and_glob_the_local_packet_without_persisting_a_session()
    {
        using var packet = new PacketDirectory();
        _ = packet.AddImage("capture.png");
        var invoker = new RecordingProcessInvoker();
        var runner = CreateRunner(new FakeProviderCatalog(), invoker);

        await runner.AnalyzeAsync(new AgentAnalyzeRequest
        {
            ProviderId = AiCliProviderIds.Claude,
            WorkingDirectory = packet.Path,
            ExactPrompt = ReviewedPrompt,
            ImagePaths = [System.IO.Path.Combine(packet.Path, "capture.png")],
        });

        AgentCliInvocation invocation = invoker.Invocations.Should().ContainSingle().Subject;
        invocation.Command.Should().Be(AiCliProviderIds.Claude);
        invocation.StandardInput.Should().BeSameAs(ReviewedPrompt);
        invocation.Arguments.Should().ContainInOrder("-p", "--output-format", "text");
        invocation.Arguments.Should().ContainInOrder("--permission-mode", "dontAsk");
        invocation.Arguments.Should().ContainInOrder("--tools", "Read,Glob");
        invocation.Arguments.Should().ContainInOrder(
            "--allowedTools", "Read(./**),Glob(./**)");
        int allowedTools = invocation.Arguments.ToList().IndexOf("--allowedTools");
        invocation.Arguments[allowedTools + 1].Should().NotBe("Read,Glob",
            "a bare allow would permit reads outside the packet");
        invocation.Arguments.Should().Contain("--no-session-persistence")
            .And.Contain("--safe-mode");
        invocation.Arguments.Should().NotContain(item =>
            item.Contains("Bash", StringComparison.OrdinalIgnoreCase) ||
            item.Contains("Edit", StringComparison.OrdinalIgnoreCase) ||
            item.Contains("Write", StringComparison.OrdinalIgnoreCase) ||
            item.Contains("dangerously", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Unavailable_selected_provider_is_refused_without_trying_an_available_fallback()
    {
        using var packet = new PacketDirectory();
        var catalog = new FakeProviderCatalog
        {
            ProviderList =
            [
                new(AiCliProviderIds.Codex, "Codex", "Codex destination", false, "Codex unavailable."),
                new(AiCliProviderIds.Claude, "Claude", "Claude destination", true),
            ],
        };
        var invoker = new RecordingProcessInvoker();
        var runner = CreateRunner(catalog, invoker);

        Func<Task> analyze = () => runner.AnalyzeAsync(new AgentAnalyzeRequest
        {
            ProviderId = AiCliProviderIds.Codex,
            WorkingDirectory = packet.Path,
            ExactPrompt = ReviewedPrompt,
        });

        await analyze.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Codex unavailable.");
        invoker.Invocations.Should().BeEmpty("Claude must never be a hidden fallback");
        catalog.RunCalls.Should().Be(0);
    }

    [Fact]
    public async Task Image_outside_packet_is_rejected_before_a_process_starts()
    {
        using var packet = new PacketDirectory();
        string outside = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"octadock-agent-outside-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(outside, [1, 2, 3]);
        var invoker = new RecordingProcessInvoker();

        try
        {
            Func<Task> analyze = () => CreateRunner(new FakeProviderCatalog(), invoker)
                .AnalyzeAsync(new AgentAnalyzeRequest
                {
                    ProviderId = AiCliProviderIds.Codex,
                    WorkingDirectory = packet.Path,
                    ExactPrompt = ReviewedPrompt,
                    ImagePaths = [outside],
                });

            await analyze.Should().ThrowAsync<ArgumentException>()
                .WithMessage("*inside the Agent Packet directory*");
            invoker.Invocations.Should().BeEmpty();
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public async Task Traversal_segments_are_rejected_even_when_the_normalized_file_exists()
    {
        using var packet = new PacketDirectory();
        Directory.CreateDirectory(System.IO.Path.Combine(packet.Path, "screens"));
        string image = packet.AddImage("image.png");
        string traversalPath = System.IO.Path.Combine(packet.Path, "screens", "..", "image.png");
        File.Exists(traversalPath).Should().BeTrue();
        var invoker = new RecordingProcessInvoker();

        Func<Task> analyze = () => CreateRunner(new FakeProviderCatalog(), invoker)
            .AnalyzeAsync(new AgentAnalyzeRequest
            {
                ProviderId = AiCliProviderIds.Codex,
                WorkingDirectory = packet.Path,
                ExactPrompt = ReviewedPrompt,
                ImagePaths = [traversalPath],
            });

        await analyze.Should().ThrowAsync<ArgumentException>().WithMessage("*traversal*");
        invoker.Invocations.Should().BeEmpty();
        image.Should().Be(System.IO.Path.Combine(packet.Path, "image.png"));
    }

    [Fact]
    public async Task Unc_packet_path_is_rejected_before_a_process_starts()
    {
        var invoker = new RecordingProcessInvoker();

        Func<Task> analyze = () => CreateRunner(new FakeProviderCatalog(), invoker)
            .AnalyzeAsync(new AgentAnalyzeRequest
            {
                ProviderId = AiCliProviderIds.Codex,
                WorkingDirectory = @"\\server\share\packet",
                ExactPrompt = ReviewedPrompt,
            });

        await analyze.Should().ThrowAsync<ArgumentException>().WithMessage("*UNC*");
        invoker.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task Prompt_and_image_limits_are_enforced_before_a_process_starts()
    {
        using var packet = new PacketDirectory();
        var invoker = new RecordingProcessInvoker();
        var runner = CreateRunner(new FakeProviderCatalog(), invoker);

        Func<Task> oversizedPrompt = () => runner.AnalyzeAsync(new AgentAnalyzeRequest
        {
            ProviderId = AiCliProviderIds.Codex,
            WorkingDirectory = packet.Path,
            ExactPrompt = new string('x', AgentCliRunner.MaxPromptCharacters + 1),
        });
        Func<Task> tooManyImages = () => runner.AnalyzeAsync(new AgentAnalyzeRequest
        {
            ProviderId = AiCliProviderIds.Codex,
            WorkingDirectory = packet.Path,
            ExactPrompt = ReviewedPrompt,
            ImagePaths = Enumerable.Repeat(packet.AddImage("one.png"), AgentCliRunner.MaxImages + 1)
                .ToArray(),
        });

        await oversizedPrompt.Should().ThrowAsync<ArgumentException>().WithMessage("*exceeds*");
        await tooManyImages.Should().ThrowAsync<ArgumentException>().WithMessage("*at most*");
        invoker.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task Defensive_output_limit_rejects_an_oversized_fake_process_result()
    {
        using var packet = new PacketDirectory();
        var invoker = new RecordingProcessInvoker
        {
            Result = new AgentCliProcessResult(
                0,
                new string('x', AgentCliRunner.MaxOutputCharacters + 1)),
        };

        Func<Task> analyze = () => CreateRunner(new FakeProviderCatalog(), invoker)
            .AnalyzeAsync(new AgentAnalyzeRequest
            {
                ProviderId = AiCliProviderIds.Claude,
                WorkingDirectory = packet.Path,
                ExactPrompt = ReviewedPrompt,
            });

        await analyze.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*more than*");
        invoker.Invocations.Should().ContainSingle();
    }

    [Fact]
    public async Task Pre_cancelled_request_never_starts_a_process()
    {
        using var packet = new PacketDirectory();
        var invoker = new RecordingProcessInvoker();
        var cancellation = new CancellationToken(canceled: true);

        Func<Task> analyze = () => CreateRunner(new FakeProviderCatalog(), invoker)
            .AnalyzeAsync(new AgentAnalyzeRequest
            {
                ProviderId = AiCliProviderIds.Claude,
                WorkingDirectory = packet.Path,
                ExactPrompt = ReviewedPrompt,
            }, cancellation);

        await analyze.Should().ThrowAsync<OperationCanceledException>();
        invoker.Invocations.Should().BeEmpty();
    }

    [Fact]
    public void Windows_command_shim_quotes_every_argument_in_one_centralized_boundary()
    {
        var invocation = new AgentCliInvocation(
            AiCliProviderIds.Codex,
            @"C:\packet folder",
            ["--image", @"C:\packet folder\a & b.png", "-"],
            ReviewedPrompt);

        string commandLine = SystemAgentCliProcessInvoker.BuildCommandLine(
            @"C:\tool folder\codex.cmd",
            invocation.Arguments);

        commandLine.Should().Be(
            "\"C:\\tool folder\\codex.cmd\" \"--image\" \"C:\\packet folder\\a & b.png\" \"-\"");
    }

    [Fact]
    public async Task Windows_command_shim_with_a_spaced_path_executes_instead_of_treating_quotes_as_filename_text()
    {
        if (!OperatingSystem.IsWindows()) return;

        string root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "Octadock shim process test",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string shim = System.IO.Path.Combine(root, "fake codex.cmd");
        await File.WriteAllTextAsync(shim, "@echo off\r\necho %~1");
        var invocation = new AgentCliInvocation(
            AiCliProviderIds.Codex,
            root,
            ["hello world"],
            string.Empty);

        try
        {
            using var process = new System.Diagnostics.Process
            {
                StartInfo = SystemAgentCliProcessInvoker.CreateStartInfo(shim, invocation),
            };
            process.Start().Should().BeTrue();
            process.StandardInput.Close();
            string stdout = await process.StandardOutput.ReadToEndAsync();
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            process.ExitCode.Should().Be(0, stderr);
            stdout.Trim().Should().Be("hello world");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Native_executable_keeps_user_paths_in_the_argument_list_instead_of_a_command_string()
    {
        var invocation = new AgentCliInvocation(
            AiCliProviderIds.Codex,
            @"C:\packet folder",
            ["--image", @"C:\packet folder\a & b.png", "-"],
            ReviewedPrompt);

        System.Diagnostics.ProcessStartInfo startInfo =
            SystemAgentCliProcessInvoker.CreateStartInfo(@"C:\tools\codex.exe", invocation);

        startInfo.FileName.Should().Be(@"C:\tools\codex.exe");
        startInfo.WorkingDirectory.Should().Be(@"C:\packet folder");
        startInfo.ArgumentList.Should().Equal(invocation.Arguments);
        startInfo.UseShellExecute.Should().BeFalse();
        startInfo.RedirectStandardInput.Should().BeTrue();
    }

    [Fact]
    public void Codex_prefers_the_signed_in_user_npm_shim_over_an_inherited_desktop_path()
    {
        string appData = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "Octadock.CodexResolverTests",
            Guid.NewGuid().ToString("N"));
        string npm = System.IO.Path.Combine(appData, "npm");
        Directory.CreateDirectory(npm);
        string shim = System.IO.Path.Combine(npm, "codex.cmd");
        File.WriteAllText(shim, "@echo off");

        try
        {
            SystemAgentCliProcessInvoker.ResolvePreferredUserExecutable(AiCliProviderIds.Codex, appData)
                .Should().Be(System.IO.Path.GetFullPath(shim));
            SystemAgentCliProcessInvoker.ResolvePreferredUserExecutable(AiCliProviderIds.Claude, appData)
                .Should().BeNull();
        }
        finally
        {
            Directory.Delete(appData, recursive: true);
        }
    }

    [Fact]
    public void Codex_child_process_drops_parent_task_state_and_api_key()
    {
        var invocation = new AgentCliInvocation(
            AiCliProviderIds.Codex,
            System.IO.Path.GetTempPath(),
            ["exec", "-"],
            ReviewedPrompt);

        System.Diagnostics.ProcessStartInfo startInfo =
            SystemAgentCliProcessInvoker.CreateStartInfo(@"C:\tools\codex.exe", invocation);

        startInfo.Environment.Should().NotContainKey("CODEX_INTERNAL_ORIGINATOR_OVERRIDE")
            .And.NotContainKey("CODEX_PERMISSION_PROFILE")
            .And.NotContainKey("CODEX_SHELL")
            .And.NotContainKey("CODEX_THREAD_ID")
            .And.NotContainKey("OPENAI_API_KEY");
        startInfo.Environment["CODEX_HOME"].Should().NotBeNullOrWhiteSpace();
    }

    private static AgentCliRunner CreateRunner(
        IAiCliRunner catalog,
        IAgentCliProcessInvoker invoker)
        => new(catalog, invoker, NullLogger<AgentCliRunner>.Instance);

    private sealed class RecordingProcessInvoker : IAgentCliProcessInvoker
    {
        public List<AgentCliInvocation> Invocations { get; } = [];

        public AgentCliProcessResult Result { get; set; } = new(0, "  agent result  ");

        public Task<AgentCliProcessResult> RunAsync(
            AgentCliInvocation invocation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Invocations.Add(invocation);
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeProviderCatalog : IAiCliRunner
    {
        public IReadOnlyList<AiCliProviderDescriptor> ProviderList { get; set; } =
        [
            new(AiCliProviderIds.Codex, "Codex", "Codex destination", true),
            new(AiCliProviderIds.Claude, "Claude", "Claude destination", true),
        ];

        public int RunCalls { get; private set; }

        public IReadOnlyList<AiCliProviderDescriptor> Providers => ProviderList;

        public Task<string> RunAsync(
            string providerId,
            string outboundText,
            CancellationToken cancellationToken = default)
        {
            RunCalls++;
            throw new InvalidOperationException("The provider catalog must not execute Agent Packets.");
        }
    }

    private sealed class PacketDirectory : IDisposable
    {
        public PacketDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "Octadock.AgentCliRunnerTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
            File.WriteAllText(System.IO.Path.Combine(Path, "TASK.md"), "# Task");
            File.WriteAllText(System.IO.Path.Combine(Path, "manifest.json"), "{}");
        }

        public string Path { get; }

        public string AddImage(string relativePath)
        {
            string fullPath = System.IO.Path.Combine(Path, relativePath);
            string? directory = System.IO.Path.GetDirectoryName(fullPath);
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(fullPath, [137, 80, 78, 71]);
            return fullPath.Replace(
                System.IO.Path.AltDirectorySeparatorChar,
                System.IO.Path.DirectorySeparatorChar);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Test cleanup is best effort on Windows antivirus/file-indexer races.
            }
        }
    }
}
