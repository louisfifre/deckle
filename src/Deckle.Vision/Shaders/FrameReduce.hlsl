// FrameReduce.hlsl — averages the desktop texture straight into the sampling
// grid, one thread group per grid cell, so the full-size frame is read once
// and never copied.
//
// The mean is plain arithmetic on the stored values, in whatever space the
// texture holds them: gamma-encoded bytes for BGRA8, linear scRGB for FP16.
// That is what the mip chain it replaces did (GenerateMips on a UNORM,
// non-sRGB format averages 2x2 blocks of raw values), so the grid keeps its
// meaning and AmbientZoneSampler needs no change.
//
// Cell (cx, cy) covers source columns [cx*W/cols, (cx+1)*W/cols) and rows
// [cy*H/rows, (cy+1)*H/rows): the grid is stretched over the whole frame, as
// the mip chain's normalised sampling stretched it.
//
// Output is a raw byte buffer laid out like the staging texture the readers
// expect, row-major: 4 bytes per cell (B, G, R, A) in SDR, 8 bytes (four
// halves R, G, B, A) in HDR.
//
// Compiled bytecode is committed beside this file as FrameReduce.cso and
// embedded in Deckle.Vision. After any edit, rebuild it from this folder:
//
//   fxc /nologo /T cs_5_0 /E main /O3 /Fo FrameReduce.cso FrameReduce.hlsl

cbuffer Params : register(b0)
{
    uint SrcWidth;
    uint SrcHeight;
    uint Cols;
    uint Rows;
    uint IsHdr;
    uint3 Padding;
};

Texture2D<float4> Src : register(t0);
RWByteAddressBuffer Dst : register(u0);

static const uint GroupSide = 16;
static const uint GroupSize = GroupSide * GroupSide;

groupshared float3 Partial[GroupSize];

[numthreads(GroupSide, GroupSide, 1)]
void main(uint3 cell : SV_GroupID, uint3 lane : SV_GroupThreadID, uint index : SV_GroupIndex)
{
    uint x0 = (cell.x * SrcWidth) / Cols;
    uint x1 = ((cell.x + 1) * SrcWidth) / Cols;
    uint y0 = (cell.y * SrcHeight) / Rows;
    uint y1 = ((cell.y + 1) * SrcHeight) / Rows;

    float3 sum = 0;
    for (uint y = y0 + lane.y; y < y1; y += GroupSide)
    {
        for (uint x = x0 + lane.x; x < x1; x += GroupSide)
        {
            sum += Src.Load(int3(x, y, 0)).rgb;
        }
    }

    Partial[index] = sum;
    GroupMemoryBarrierWithGroupSync();

    for (uint stride = GroupSize / 2; stride > 0; stride >>= 1)
    {
        if (index < stride)
        {
            Partial[index] += Partial[index + stride];
        }
        GroupMemoryBarrierWithGroupSync();
    }

    if (index != 0) return;

    uint count = max((x1 - x0) * (y1 - y0), 1u);
    float3 mean = Partial[0] / count;
    uint cellIndex = cell.y * Cols + cell.x;

    if (IsHdr != 0)
    {
        // Halves keep the sign and the range above 1.0 that scRGB carries.
        uint r = f32tof16(mean.r);
        uint g = f32tof16(mean.g);
        uint b = f32tof16(mean.b);
        uint a = f32tof16(1.0);
        Dst.Store2(cellIndex * 8, uint2(r | (g << 16), b | (a << 16)));
    }
    else
    {
        uint3 q = (uint3)round(saturate(mean) * 255.0);
        Dst.Store(cellIndex * 4, q.b | (q.g << 8) | (q.r << 16) | (0xFFu << 24));
    }
}
