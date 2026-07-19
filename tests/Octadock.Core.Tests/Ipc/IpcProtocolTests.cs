using System.Text.Json;
using FluentAssertions;
using Octadock.Core.Ipc;
using Xunit;

namespace Octadock.Core.Tests.Ipc;

public sealed class IpcProtocolTests
{
    [Fact]
    public void Capture_id_round_trips_as_an_additive_v1_response_field()
    {
        Guid captureId = Guid.NewGuid();

        string json = IpcProtocol.SerializeResponse(IpcResponse.Ok("Capture completed.", captureId));
        IpcResponse? response = IpcProtocol.DeserializeResponse(json);

        response.Should().NotBeNull();
        response!.Success.Should().BeTrue();
        response.CaptureId.Should().Be(captureId);
        using JsonDocument document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("captureId").GetGuid().Should().Be(captureId);
    }

    [Fact]
    public void Legacy_success_response_without_capture_id_remains_compatible()
    {
        IpcResponse? response = IpcProtocol.DeserializeResponse(
            "{\"success\":true,\"message\":\"ok\",\"exitCode\":0}");

        response.Should().NotBeNull();
        response!.Success.Should().BeTrue();
        response.CaptureId.Should().BeNull();
        IpcProtocol.SerializeResponse(IpcResponse.Ok()).Should().NotContain("captureId");
    }
}
