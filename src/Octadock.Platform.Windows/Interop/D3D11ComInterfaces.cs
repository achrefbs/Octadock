using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Octadock.Platform.Windows.Interop;

// These COM interface declarations expose only the handful of D3D11 methods the
// capture readback path calls (CreateTexture2D, GetImmediateContext, CopyResource,
// Map/Unmap). COM requires every preceding vtable slot to be present in exact
// order, so unused earlier slots are declared as opaque placeholders with correct
// arity but signatures we never invoke. This keeps the interfaces small while
// preserving vtable layout. Runtime validation happens through the WGC fallback path.

/// <summary>Minimal <c>ID3D11Texture2D</c> (used only as an opaque resource handle).</summary>
[ComImport]
[Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[SupportedOSPlatform("windows")]
internal interface ID3D11Texture2D
{
    // ID3D11DeviceChild (4 slots after IUnknown).
    void GetDevice(out nint ppDevice);

    [PreserveSig]
    int GetPrivateData(ref Guid guid, ref uint pDataSize, nint pData);

    [PreserveSig]
    int SetPrivateData(ref Guid guid, uint dataSize, nint pData);

    [PreserveSig]
    int SetPrivateDataInterface(ref Guid guid, nint pData);

    // ID3D11Resource.
    void GetType(out uint pResourceDimension);

    void SetEvictionPriority(uint evictionPriority);

    [PreserveSig]
    uint GetEvictionPriority();

    // ID3D11Texture2D.
    [PreserveSig]
    void GetDesc(out D3D11_TEXTURE2D_DESC pDesc);
}

/// <summary>
/// Minimal <c>ID3D11Device</c> exposing <c>CreateTexture2D</c> and
/// <c>GetImmediateContext</c>. Earlier vtable slots are declared with the correct
/// order; parameter shapes of unused slots are irrelevant since they are never
/// called through this projection.
/// </summary>
[ComImport]
[Guid("db6f6ddb-ac77-4e88-8253-819df9bbf140")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[SupportedOSPlatform("windows")]
internal interface ID3D11Device
{
    // Slot 3: CreateBuffer
    [PreserveSig]
    int CreateBuffer(nint pDesc, nint pInitialData, out nint ppBuffer);

    // Slot 4: CreateTexture1D
    [PreserveSig]
    int CreateTexture1D(nint pDesc, nint pInitialData, out nint ppTexture1D);

    // Slot 5: CreateTexture2D
    [PreserveSig]
    int CreateTexture2D(ref D3D11_TEXTURE2D_DESC pDesc, nint pInitialData, out nint ppTexture2D);

    // Slot 6: CreateTexture3D
    [PreserveSig]
    int CreateTexture3D(nint pDesc, nint pInitialData, out nint ppTexture3D);

    // Slot 7: CreateShaderResourceView
    [PreserveSig]
    int CreateShaderResourceView(nint pResource, nint pDesc, out nint ppView);

    // Slot 8: CreateUnorderedAccessView
    [PreserveSig]
    int CreateUnorderedAccessView(nint pResource, nint pDesc, out nint ppView);

    // Slot 9: CreateRenderTargetView
    [PreserveSig]
    int CreateRenderTargetView(nint pResource, nint pDesc, out nint ppView);

    // Slot 10: CreateDepthStencilView
    [PreserveSig]
    int CreateDepthStencilView(nint pResource, nint pDesc, out nint ppView);

    // Slot 11: CreateInputLayout
    [PreserveSig]
    int CreateInputLayout(nint a, uint b, nint c, nint d, out nint e);

    // Slot 12: CreateVertexShader
    [PreserveSig]
    int CreateVertexShader(nint a, nint b, nint c, out nint d);

    // Slot 13: CreateGeometryShader
    [PreserveSig]
    int CreateGeometryShader(nint a, nint b, nint c, out nint d);

    // Slot 14: CreateGeometryShaderWithStreamOutput
    [PreserveSig]
    int CreateGeometryShaderWithStreamOutput(nint a, nint b, uint c, nint d, uint e, nint f, uint g, uint h, nint i, out nint j);

    // Slot 15: CreatePixelShader
    [PreserveSig]
    int CreatePixelShader(nint a, nint b, nint c, out nint d);

    // Slot 16: CreateHullShader
    [PreserveSig]
    int CreateHullShader(nint a, nint b, nint c, out nint d);

    // Slot 17: CreateDomainShader
    [PreserveSig]
    int CreateDomainShader(nint a, nint b, nint c, out nint d);

    // Slot 18: CreateComputeShader
    [PreserveSig]
    int CreateComputeShader(nint a, nint b, nint c, out nint d);

    // Slot 19: CreateClassLinkage
    [PreserveSig]
    int CreateClassLinkage(out nint ppLinkage);

    // Slot 20: CreateBlendState
    [PreserveSig]
    int CreateBlendState(nint a, out nint b);

    // Slot 21: CreateDepthStencilState
    [PreserveSig]
    int CreateDepthStencilState(nint a, out nint b);

    // Slot 22: CreateRasterizerState
    [PreserveSig]
    int CreateRasterizerState(nint a, out nint b);

    // Slot 23: CreateSamplerState
    [PreserveSig]
    int CreateSamplerState(nint a, out nint b);

    // Slot 24: CreateQuery
    [PreserveSig]
    int CreateQuery(nint a, out nint b);

    // Slot 25: CreatePredicate
    [PreserveSig]
    int CreatePredicate(nint a, out nint b);

    // Slot 26: CreateCounter
    [PreserveSig]
    int CreateCounter(nint a, out nint b);

    // Slot 27: CreateDeferredContext
    [PreserveSig]
    int CreateDeferredContext(uint flags, out nint ppDeferredContext);

    // Slot 28: OpenSharedResource
    [PreserveSig]
    int OpenSharedResource(nint hResource, ref Guid returnedInterface, out nint ppResource);

    // Slot 29: CheckFormatSupport
    [PreserveSig]
    int CheckFormatSupport(uint format, out uint pFormatSupport);

    // Slot 30: CheckMultisampleQualityLevels
    [PreserveSig]
    int CheckMultisampleQualityLevels(uint format, uint sampleCount, out uint pNumQualityLevels);

    // Slot 31: CheckCounterInfo
    void CheckCounterInfo(nint pCounterInfo);

    // Slot 32: CheckCounter
    [PreserveSig]
    int CheckCounter(nint a, out uint b, nint c, out uint d, nint e, out uint f, nint g, out uint h);

    // Slot 33: CheckFeatureSupport
    [PreserveSig]
    int CheckFeatureSupport(uint feature, nint pFeatureSupportData, uint featureSupportDataSize);

    // Slot 34: GetPrivateData
    [PreserveSig]
    int GetPrivateData(ref Guid guid, ref uint pDataSize, nint pData);

    // Slot 35: SetPrivateData
    [PreserveSig]
    int SetPrivateData(ref Guid guid, uint dataSize, nint pData);

    // Slot 36: SetPrivateDataInterface
    [PreserveSig]
    int SetPrivateDataInterface(ref Guid guid, nint pData);

    // Slot 37: GetFeatureLevel
    [PreserveSig]
    uint GetFeatureLevel();

    // Slot 38: GetCreationFlags
    [PreserveSig]
    uint GetCreationFlags();

    // Slot 39: GetDeviceRemovedReason
    [PreserveSig]
    int GetDeviceRemovedReason();

    // Slot 40: GetImmediateContext
    void GetImmediateContext(out nint ppImmediateContext);

    // Slot 41: SetExceptionMode
    [PreserveSig]
    int SetExceptionMode(uint raiseFlags);

    // Slot 42: GetExceptionMode
    [PreserveSig]
    uint GetExceptionMode();
}

/// <summary>
/// Minimal <c>ID3D11DeviceContext</c> exposing <c>Map</c>, <c>Unmap</c> and
/// <c>CopyResource</c>. Preceding slots are declared as opaque placeholders in
/// exact order.
/// </summary>
[ComImport]
[Guid("c0bfa96c-e089-44fb-8eaf-26f8796190da")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[SupportedOSPlatform("windows")]
internal interface ID3D11DeviceContext
{
    // ID3D11DeviceChild (slots 3-6).
    void GetDevice(out nint ppDevice);

    [PreserveSig]
    int GetPrivateData(ref Guid guid, ref uint pDataSize, nint pData);

    [PreserveSig]
    int SetPrivateData(ref Guid guid, uint dataSize, nint pData);

    [PreserveSig]
    int SetPrivateDataInterface(ref Guid guid, nint pData);

    // Slot 7: VSSetConstantBuffers
    void VSSetConstantBuffers(uint startSlot, uint numBuffers, nint ppConstantBuffers);

    // Slot 8: PSSetShaderResources
    void PSSetShaderResources(uint startSlot, uint numViews, nint ppShaderResourceViews);

    // Slot 9: PSSetShader
    void PSSetShader(nint pPixelShader, nint ppClassInstances, uint numClassInstances);

    // Slot 10: PSSetSamplers
    void PSSetSamplers(uint startSlot, uint numSamplers, nint ppSamplers);

    // Slot 11: VSSetShader
    void VSSetShader(nint pVertexShader, nint ppClassInstances, uint numClassInstances);

    // Slot 12: DrawIndexed
    void DrawIndexed(uint indexCount, uint startIndexLocation, int baseVertexLocation);

    // Slot 13: Draw
    void Draw(uint vertexCount, uint startVertexLocation);

    // Slot 14: Map
    [PreserveSig]
    int Map(nint pResource, uint subresource, D3D11_MAP mapType, uint mapFlags, out D3D11_MAPPED_SUBRESOURCE pMappedResource);

    // Slot 15: Unmap. Native signature is 'void' (no HRESULT); PreserveSig stops
    // the CLR from misinterpreting the (absent) return value.
    [PreserveSig]
    void Unmap(nint pResource, uint subresource);

    // Slot 16: PSSetConstantBuffers
    void PSSetConstantBuffers(uint startSlot, uint numBuffers, nint ppConstantBuffers);

    // Slot 17: IASetInputLayout
    void IASetInputLayout(nint pInputLayout);

    // Slot 18: IASetVertexBuffers
    void IASetVertexBuffers(uint startSlot, uint numBuffers, nint ppVertexBuffers, nint pStrides, nint pOffsets);

    // Slot 19: IASetIndexBuffer
    void IASetIndexBuffer(nint pIndexBuffer, uint format, uint offset);

    // Slot 20: DrawIndexedInstanced
    void DrawIndexedInstanced(uint a, uint b, uint c, int d, uint e);

    // Slot 21: DrawInstanced
    void DrawInstanced(uint a, uint b, uint c, uint d);

    // Slot 22: GSSetConstantBuffers
    void GSSetConstantBuffers(uint startSlot, uint numBuffers, nint ppConstantBuffers);

    // Slot 23: GSSetShader
    void GSSetShader(nint pShader, nint ppClassInstances, uint numClassInstances);

    // Slot 24: IASetPrimitiveTopology
    void IASetPrimitiveTopology(uint topology);

    // Slot 25: VSSetShaderResources
    void VSSetShaderResources(uint startSlot, uint numViews, nint ppShaderResourceViews);

    // Slot 26: VSSetSamplers
    void VSSetSamplers(uint startSlot, uint numSamplers, nint ppSamplers);

    // Slot 27: Begin
    void Begin(nint pAsync);

    // Slot 28: End
    void End(nint pAsync);

    // Slot 29: GetData
    [PreserveSig]
    int GetData(nint pAsync, nint pData, uint dataSize, uint getDataFlags);

    // Slot 30: SetPredication
    void SetPredication(nint pPredicate, [MarshalAs(UnmanagedType.Bool)] bool predicateValue);

    // Slot 31: GSSetShaderResources
    void GSSetShaderResources(uint startSlot, uint numViews, nint ppShaderResourceViews);

    // Slot 32: GSSetSamplers
    void GSSetSamplers(uint startSlot, uint numSamplers, nint ppSamplers);

    // Slot 33: OMSetRenderTargets
    void OMSetRenderTargets(uint numViews, nint ppRenderTargetViews, nint pDepthStencilView);

    // Slot 34: OMSetRenderTargetsAndUnorderedAccessViews
    void OMSetRenderTargetsAndUnorderedAccessViews(uint a, nint b, nint c, uint d, uint e, nint f, nint g);

    // Slot 35: OMSetBlendState
    void OMSetBlendState(nint pBlendState, nint blendFactor, uint sampleMask);

    // Slot 36: OMSetDepthStencilState
    void OMSetDepthStencilState(nint pDepthStencilState, uint stencilRef);

    // Slot 37: SOSetTargets
    void SOSetTargets(uint numBuffers, nint ppSOTargets, nint pOffsets);

    // Slot 38: DrawAuto
    void DrawAuto();

    // Slot 39: DrawIndexedInstancedIndirect
    void DrawIndexedInstancedIndirect(nint pBufferForArgs, uint alignedByteOffsetForArgs);

    // Slot 40: DrawInstancedIndirect
    void DrawInstancedIndirect(nint pBufferForArgs, uint alignedByteOffsetForArgs);

    // Slot 41: Dispatch
    void Dispatch(uint x, uint y, uint z);

    // Slot 42: DispatchIndirect
    void DispatchIndirect(nint pBufferForArgs, uint alignedByteOffsetForArgs);

    // Slot 43: RSSetState
    void RSSetState(nint pRasterizerState);

    // Slot 44: RSSetViewports
    void RSSetViewports(uint numViewports, nint pViewports);

    // Slot 45: RSSetScissorRects
    void RSSetScissorRects(uint numRects, nint pRects);

    // Slot 46: CopySubresourceRegion
    void CopySubresourceRegion(nint pDstResource, uint dstSubresource, uint dstX, uint dstY, uint dstZ, nint pSrcResource, uint srcSubresource, nint pSrcBox);

    // Slot 47: CopyResource. Native signature is 'void' (no HRESULT).
    [PreserveSig]
    void CopyResource(nint pDstResource, nint pSrcResource);
}
