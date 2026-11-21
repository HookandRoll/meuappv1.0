using System;
using System.Numerics;

namespace Mapmaker.Core;

// The same seeded boundary is used for plate ownership, contacts and reserved sea.
public sealed class PolarPlateBands
{
    private readonly WorldManifest _manifest;
    private readonly float[] _north;
    private readonly float[] _south;
    public float Height => _manifest.Height * 1.5f;
    public static bool Enabled(WorldManifest m) => m.GeneratorVersion >= 38 && m.Generation.Extent == MapExtent.World;

    public PolarPlateBands(WorldManifest m)
    {
        _manifest = m;
        _north = new float[m.Width + 1]; _south = new float[m.Width + 1];
        for (int x = 0; x <= m.Width; x++)
        {
            _north[x] = Depth(m, x * HexGrid.Root3, PlateKind.Arctic);
            _south[x] = Depth(m, x * HexGrid.Root3, PlateKind.Antarctic);
        }
    }

    public static float Depth(WorldManifest m, float worldX, PlateKind kind)
    {
        uint salt = kind == PlateKind.Arctic ? 0x93ACF175u : 0xC4517E2Bu;
        uint seed = unchecked((uint)m.Seed) ^ salt;
        float aspect = m.Width / (float)m.Height;
        float x = (worldX / HexGrid.Root3 + 0.5f) / m.Height;
        float phase = kind == PlateKind.Arctic ? 1.7f : 5.3f;
        float broad = WorldGenerator.TerrainNoise(x, phase, 3.4f, aspect, m.HorizontalWrap, seed) - 0.5f;
        float detail = WorldGenerator.TerrainNoise(x, phase + 7, 11f, aspect, m.HorizontalWrap, seed ^ 0x17BA5D49u, angular: true) - 0.5f;
        float fine = WorldGenerator.TerrainNoise(x, phase + 13, 29f, aspect, m.HorizontalWrap, seed ^ 0x87E31B65u, angular: true) - 0.5f;
        return Math.Max(3f, m.Height * 1.5f * (0.085f + broad * 0.065f + detail * 0.040f + fine * 0.015f));
    }

    private float Sample(float[] values, float worldX)
    {
        float x = Math.Clamp(worldX / HexGrid.Root3, 0, _manifest.Width);
        int i = Math.Min((int)x, _manifest.Width - 1);
        return float.Lerp(values[i], values[i + 1], x - i);
    }
    public float North(float x) => Sample(_north, x);
    public float South(float x) => Sample(_south, x);
    public PlateKind KindAt(Vector2 point) => point.Y <= North(point.X) ? PlateKind.Arctic
        : point.Y >= Height - South(point.X) ? PlateKind.Antarctic : PlateKind.Normal;
    public float SignedDistance(Vector2 point, PlateKind kind) => kind == PlateKind.Arctic
        ? point.Y - North(point.X) : Height - point.Y - South(point.X);
    public float DistanceToOcean(Vector2 point) => Math.Max(0, Math.Min(point.Y - North(point.X), Height - point.Y - South(point.X)));

    public static float ContactExtension(float compression) => -Math.Clamp(Math.Abs(compression), 0.65f, 1.5f);
}
