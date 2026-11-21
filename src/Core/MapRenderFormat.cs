using System;
using System.Buffers.Binary;

namespace Mapmaker.Core;

// Byte-exact GPU planes. IDs never share bits with render metadata.
public static class MapRenderFormat
{
    public const int Version = 2;
    public const int BytesPerPlane = 4;
    public const int PreviewPlanes = 5;
    public const byte Snow = 32, ReliefSymbol = 64, VegetationSymbol = 128;
    public const byte ContourSymbol = 16; // Low three bits: downhill direction + 1.
    public const byte MassifSymbol = 8; // Sparse interior / uphill spur, centered artwork.

    public static void WriteHex(WorldData world, int cell, Span<byte> terrain, Span<byte> surface,
        Span<byte> geography, Span<byte> featureLow, Span<byte> featureHigh)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(terrain, (ushort)(world.Elevation[cell] - short.MinValue));
        BinaryPrimitives.WriteUInt16LittleEndian(terrain[2..], (ushort)world.Biomes[cell]);
        surface[0] = (byte)world.Water[cell];
        BinaryPrimitives.WriteUInt16LittleEndian(surface[1..], (ushort)world.Relief[cell]);
        surface[3] = world.Manifest.Generation.Stage == GenerationStage.Terrain && world.Relief[cell] == ReliefType.Peak &&
            (world.Temperature[cell] <= 0 || world.Biomes[cell] == BiomeType.Alpine) ? Snow : (byte)0;
        BinaryPrimitives.WriteUInt16LittleEndian(geography, (ushort)world.Landforms[cell]);
        geography[2] = 0; // Coast crowding is derived after the independent IDs.
        var locks = (HexLocks)world.LockMask[cell];
        geography[3] = (byte)((locks & HexLocks.All) == HexLocks.All ? 2 : locks == 0 ? 0 : 1);
        ulong features = (ulong)world.Features[cell];
        BinaryPrimitives.WriteUInt32LittleEndian(featureLow, (uint)features);
        BinaryPrimitives.WriteUInt32LittleEndian(featureHigh, (uint)(features >> 32));
    }
}
