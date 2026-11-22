using System;

namespace Mapmaker.Core;

// Ice-covered land is an exception inside the reserved polar plates, not a
// replacement for their oceanic crust or the nonpolar land quota.
public sealed class PolarIceField
{
    private readonly WorldManifest _m;
    private readonly MapDomain _domain;
    private readonly PolarPlateBands _bands;
    private readonly OceanicMarginBands _margins;
    public static bool Enabled(WorldManifest m) => m.GeneratorVersion >= 46 && PolarPlateBands.Enabled(m);
    public PolarIceField(WorldManifest m) { _m = m; _domain = new MapDomain(m); _bands = new(m); _margins = new(m); }
    public bool IsPolar(int cell) => _domain.Contains(cell) &&
        _bands.KindAt(HexGrid.Center(HexGrid.FromOffset(cell % _m.Width, cell / _m.Width))) != PlateKind.Normal;
    private float Noise(int cell, float frequency, uint salt) => WorldGenerator.TerrainNoise(
        (cell % _m.Width + (cell / _m.Width & 1) * 0.5f) / _m.Height, cell / _m.Width / (float)_m.Height,
        frequency, _m.Width / (float)_m.Height, _m.HorizontalWrap, unchecked((uint)_m.Seed) ^ salt, _m.GeneratorVersion < 48);
    public bool IsLand(int cell)
    {
        if (!IsPolar(cell)) return false;
        var p = HexGrid.Center(HexGrid.FromOffset(cell % _m.Width, cell / _m.Width));
        if (_margins.SideAt(p) != 0) return false;
        bool north = p.Y < _m.Height * 0.75f;
        float depth = north ? p.Y / _bands.North(p.X) : (_bands.Height - p.Y) / _bands.South(p.X);
        uint hash = unchecked((uint)_m.Seed) * 0x9E3779B9u;
        bool continent = north ? (hash & 1) == 0 : (hash & 1) != 0 || (hash & 4) != 0;
        float broad = Noise(cell, 9, north ? 0x1723AC51u : 0x7521B39Du);
        float detail = Noise(cell, 37, 0x4AC91735u);
        float coast = 0.65f * broad + 0.35f * detail;
        if (_m.GeneratorVersion >= 48)
        {
            float edge = north ? p.Y : (_m.Height - 1) * 1.5f - p.Y;
            float band = north ? _bands.North(p.X) : _bands.South(p.X);
            float fine = Noise(cell, 113, 0x5381CA79u);
            float side = _margins.DistanceToOcean(p);
            float retreat = _m.Height * HexGrid.Root3 * (0.006f + 0.012f * detail);
            float taper = Math.Clamp((side - retreat) / (_m.Height * HexGrid.Root3 * 0.10f), 0, 1);
            taper = taper * taper * (3 - 2 * taper);
            float shore = (continent ? 0.24f : 0.16f) + broad * 0.38f
                + (detail - 0.5f) * 0.28f + (fine - 0.5f) * 0.12f;
            float thickness = band * shore * taper;
            // Thin to zero before the reserved side belt. Neither that belt
            // nor the polar boundary may slice an otherwise solid ice field.
            if (thickness >= 1.5f && edge <= thickness) return true;
            float offshore = coast + (fine - 0.5f) * 0.20f
                - MathF.Pow(Math.Max(0, depth) / 0.75f, 4) * 0.18f - (1 - taper) * 0.45f;
            return side > retreat && offshore > 0.68f;
        }
        if (_m.GeneratorVersion >= 47)
        {
            // Both ice caps grow from the actual storage edge. Offshore islands
            // are additional ice, never a substitute for the attached cap.
            float edge = north ? p.Y : (_m.Height - 1) * 1.5f - p.Y;
            float band = north ? _bands.North(p.X) : _bands.South(p.X);
            float fine = Noise(cell, 113, 0x5381CA79u);
            float shore = (continent ? 0.26f : 0.14f) + broad * 0.40f
                + (detail - 0.5f) * 0.24f + (fine - 0.5f) * 0.10f;
            if (edge <= Math.Max(1.5f, band * shore)) return true;
            return depth < 0.94f && coast + (fine - 0.5f) * 0.20f > 0.66f;
        }
        if (continent && depth < 0.32f + broad * 0.36f && detail > 0.13f) return true;
        return depth > 0.18f && depth < 0.94f && coast > (continent ? 0.62f : 0.54f);
    }
    public short Elevation(int cell) => (short)(80 + 1250 * MathF.Pow(Noise(cell, 7, 0xF3517A93u), 1.7f));
}
