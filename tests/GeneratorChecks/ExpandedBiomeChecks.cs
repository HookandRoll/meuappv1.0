using System;
using System.Linq;
using System.IO;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    internal static void CheckLandSliderRange()
    {
        foreach (bool atlas in new[] { false, true })
        foreach (bool wrap in new[] { false, true })
        {
            int previous = -1;
            foreach (int percent in new[] { 0, 5, 35, 65, 70, 95, 100 })
            {
                var m = CheckManifest(192,96,wrap);
                m = m with { Generation = m.Generation with { Extent = atlas ? MapExtent.World : MapExtent.Region, LandPercent = percent } };
                var world = Create(m);
                int land = Enumerable.Range(0,world.Count).Count(i=>world.Domain.CountsTowardLandQuota(i) && world.Water[i]==WaterType.None);
                int target = world.Domain.PercentageAreaCount * percent / 100;
                if (land > target || land < previous || ((!atlas || percent<=65) && land!=target))
                    throw new Exception($"Land slider quota failed: world={atlas} wrap={wrap} {percent}%: {land}/{target}.");
                if (atlas)
                {
                    var margins = new OceanicMarginBands(m);
                    for(int i=0;i<world.Count;i++)
                        if(world.Domain.Contains(i) && margins.SideAt(HexGrid.Center(HexGrid.FromOffset(i%world.Width,i/world.Width)))!=0 && world.Water[i]==WaterType.None)
                            throw new Exception("High land slider overwrote the reserved ocean.");
                }
                previous=land;
                Console.WriteLine($"PASS land slider world={atlas} wrap={wrap} target={percent}% land={land}/{world.Domain.PercentageAreaCount}");
            }
        }
    }

    internal static void CheckExpandedBiomes()
    {
        CheckNaturalBiomeShapes();
        foreach (int h in new[] { 2300, 3799 })
            if (ChooseExpandedBiome((short)h, 0, 1000, 32, 32, 128, 64, false, 1234) == BiomeType.Alpine)
                throw new Exception("Alpine starts below 3800 m.");
        if (ChooseExpandedBiome(4000, 0, 1000, 32, 32, 128, 64, false, 1234) != BiomeType.Alpine)
            throw new Exception("4000 m cold ground did not become alpine.");
        int transition = Enumerable.Range(0,128).Count(x => ChooseExpandedBiome(3900, 0, 1000, x, 32, 128, 64, false, 1234) == BiomeType.Alpine);
        if (transition is 0 or 128) throw new Exception("Alpine transition is not irregular.");
        if (ChooseExpandedBiome(100, 220, 1200, 32, 32, 128, 64, false, 1234) != BiomeType.SeasonalForest ||
            ChooseExpandedBiome(100, 120, 2200, 32, 32, 128, 64, false, 1234) != BiomeType.TemperateRainforest)
            throw new Exception("Expanded forest climate rules are missing.");
        WorldData Habitat(short temperature, ushort rain, BiomeType biome)
        {
            var v = new WorldData(CheckManifest(64,64,false)); Array.Fill(v.Elevation,(short)100);
            Array.Fill(v.Temperature,temperature); Array.Fill(v.Rainfall,rain); Array.Fill(v.Biomes,biome); return v;
        }
        foreach (var (t,p,initial,expected,ocean) in new[] {
            ((short)30,(ushort)1200,BiomeType.Taiga,BiomeType.Bog,false),
            ((short)260,(ushort)150,BiomeType.Desert,BiomeType.Oasis,false),
            ((short)230,(ushort)1300,BiomeType.Grassland,BiomeType.Mangrove,true) })
        {
            var habitat = Habitat(t,p,initial);
            if (ocean) Array.Fill(habitat.Elevation,(short)15);
            for (int row=8;row<56;row++) { int i=row*64+32; habitat.Water[i]=ocean?WaterType.Ocean:WaterType.Lake; habitat.Elevation[i]=-50; habitat.Biomes[i]=BiomeType.None; }
            ApplyContextBiomes(habitat,new(0,0,64,64),true);
            if (!habitat.Biomes.Contains(expected)) throw new Exception($"Context biome {expected} did not form.");
        }
        var salt = Habitat(180,100,BiomeType.Desert); Array.Fill(salt.Elevation,(short)400);
        var rocks = Habitat(180,200,BiomeType.Desert);
        for(int row=5;row<60;row+=5) for(int col=5;col<60;col+=5)
        { salt.Elevation[row*64+col]=100; rocks.Elevation[row*64+col]=240; rocks.Relief[row*64+col]=ReliefType.Rolling; }
        if (Version >= 47)
            for (int row=0; row<64; row++) for (int col=0; col<64; col++) rocks.Elevation[row*64+col]=(short)(200+row*40);
        ApplyContextBiomes(salt,new(0,0,64,64),true); ApplyContextBiomes(rocks,new(0,0,64,64),true);
        if (!salt.Biomes.Contains(BiomeType.SaltFlat) || !rocks.Biomes.Contains(BiomeType.Badlands) ||
            !rocks.Features.Any(f=>(f&GeographicFeatures.BadlandRocks)!=0)) throw new Exception("Closed dry basins / eroded badlands did not form.");
        Console.WriteLine("PASS expanded habitats: irregular 3800..4000 m alpine, seasonal/wet forests, freshwater oasis/bog, sheltered mangrove, closed salt basins and badland rocks");
        foreach (bool wrap in new[] { false, true })
        {
            var m = CheckManifest(128, 64, wrap);
            var w = new WorldData(m); Array.Fill(w.Water, WaterType.Ocean); Array.Fill(w.Elevation, (short)-1500);
            for (int y = 5; y < 55; y++) for (int x = 5; x < 50; x++) { w.Water[y * 128 + x] = WaterType.None; w.Elevation[y * 128 + x] = 5000; }
            for (int y = 22; y < 34; y++) for (int x = 80; x < 102; x++)
            {
                int i = y * 128 + x; w.Water[i] = WaterType.None;
                double a = Math.Exp(-((x - 86) * (x - 86) + (y - 27) * (y - 27)) / 8.0);
                double b = Math.Exp(-((x - 96) * (x - 96) + (y - 28) * (y - 28)) / 8.0);
                w.Elevation[i] = (short)(300 + 2700 * Math.Max(a, b));
            }
            var field = BuildVolcanoes(m, w.Elevation, w.Water, w.Domain);
            int centers = field.Count(v => v == 2);
            if (centers != 2 || field.Count(v => v == 1) < 8) throw new Exception($"Expected two island volcanoes and aprons: {centers}.");
            var originalWater = (WaterType[])w.Water.Clone(); var originalHeights = (short[])w.Elevation.Clone();
            RegenerateRegion(w, new(0,0,w.Width,w.Height), RegenerationMode.Everything, HexLocks.Features);
            if (w.Features.Count(f=>(f&GeographicFeatures.Volcano)!=0) != centers || !originalWater.SequenceEqual(w.Water) || !originalHeights.SequenceEqual(w.Elevation))
                throw new Exception("Feature-only regeneration ignored the edited island geometry or changed water/height.");
            int lockedCenter=Array.FindIndex(field,v=>v==2); w.Features[lockedCenter]=GeographicFeatures.None; w.LockMask[lockedCenter]=(ushort)HexLocks.Features;
            RegenerateRegion(w,new(0,0,w.Width,w.Height),RegenerationMode.Everything,HexLocks.Features);
            if(w.Features[lockedCenter]!=GeographicFeatures.None) throw new Exception("Volcano generation ignored feature lock.");
            for (int i = 0; i < w.Count; i++) if (field[i] != 0 && (w.Water[i] != WaterType.None || i % 128 < 80))
                throw new Exception("Volcano spread crossed water or reached a continent.");
            foreach (int seed in new[] { 1234, 1, -77 })
            {
                var size = MapDomain.ExpandedSize(128, 64);
                var manifest = CheckManifest(size.Width, size.Height, wrap) with { Seed = seed };
                manifest = manifest with { Generation = manifest.Generation with { Extent = MapExtent.World, ReferenceWidth = 128, ReferenceHeight = 64 } };
                var world = Create(manifest); var ice = new PolarIceField(manifest);
                int north = 0, south = 0, nonpolarLand = 0;
                for (int i = 0; i < world.Count; i++)
                {
                    if (world.Water[i] == WaterType.None && ice.IsPolar(i))
                    {
                        if (world.Biomes[i] != BiomeType.Glacier) throw new Exception("Non-glacier biome on a polar island.");
                        if (i / world.Width < world.Height / 2) north++; else south++;
                    }
                    else if (world.Biomes[i] == BiomeType.Glacier) throw new Exception("Glacier escaped polar plates.");
                    if (world.Water[i] == WaterType.None && !ice.IsPolar(i)) nonpolarLand++;
                }
                if (north == 0 || south == 0 || nonpolarLand != world.Domain.PercentageAreaCount * 38 / 100)
                    throw new Exception($"Polar ice or quota failed: N={north}, S={south}, nonpolar={nonpolarLand}.");
                var copy = Create(manifest);
                RegenerateRegion(copy, new(world.Width / 4, 0, world.Width / 2, world.Height / 4), RegenerationMode.Everything, HexLocks.Biome);
                RegenerateRegion(copy, new(world.Width / 4, 0, world.Width / 2, world.Height / 4), RegenerationMode.Everything, HexLocks.Features);
                if (!world.Water.SequenceEqual(copy.Water) || !world.Elevation.SequenceEqual(copy.Elevation) || !world.Biomes.SequenceEqual(copy.Biomes) || !world.Features.SequenceEqual(copy.Features))
                    throw new Exception($"Polar/feature regional regeneration differs: water={world.Water.Where((v,i)=>v!=copy.Water[i]).Count()}, height={world.Elevation.Where((v,i)=>v!=copy.Elevation[i]).Count()}, biomes={world.Biomes.Where((v,i)=>v!=copy.Biomes[i]).Count()}, features={world.Features.Where((v,i)=>v!=copy.Features[i]).Count()}; firstBiome={Array.FindIndex(Enumerable.Range(0,world.Count).ToArray(), i=>world.Biomes[i]!=copy.Biomes[i])}");
                string path = Path.Combine(Path.GetTempPath(), $"expanded-{Guid.NewGuid():N}.world");
                try { WorldFile.Save(path, world, null); var loaded = WorldFile.Load(path);
                    if (!world.Features.SequenceEqual(loaded.Features) || !world.Biomes.SequenceEqual(loaded.Biomes)) throw new Exception("Expanded save/load lost biomes/features."); }
                finally { File.Delete(path); }
                Console.WriteLine($"PASS expanded seed={seed} wrap={wrap}: polar glaciers N={north} S={south}, exact nonpolar quota, regeneration and save/load; island volcanoes={centers}");
            }
        }
    }
    private static void CheckNaturalBiomeShapes()
    {
        foreach (int seed in new[] { 1234, 1, -77 })
        foreach (int height in new[] { 96, 512 })
        {
            var m = CheckManifest(height * 2, height, false) with { Seed = seed };
            m = m with { Generation = m.Generation with { Extent = MapExtent.World } };
            var ice = new PolarIceField(m);
            foreach (int row in new[] { 0, height - 1 })
            {
                var coasts = new System.Collections.Generic.HashSet<int>();
                for (int x = m.Width / 3; x < m.Width * 2 / 3; x++)
                {
                    if (!ice.IsLand(row * m.Width + x)) throw new Exception("Polar cap detached from straight edge.");
                    int depth = 0, step = row == 0 ? 1 : -1;
                    while (depth < height / 4 && ice.IsLand((row + depth * step) * m.Width + x)) depth++;
                    coasts.Add(depth);
                }
                if (coasts.Count < 3) throw new Exception("Polar cap has a flat inner coast.");
            }
        }
        var island = new WorldData(CheckManifest(1024,512,false));
        Array.Fill(island.Water,WaterType.Ocean); Array.Fill(island.Elevation,(short)-1000);
        int size = 0;
        for(int y=190;y<290;y++) for(int x=650;x<750;x++)
        {
            int i=y*1024+x; island.Water[i]=WaterType.None; size++;
            island.Elevation[i]=(short)(400+3000*Math.Exp(-((x-700.0)*(x-700)+(y-240.0)*(y-240))/300));
        }
        // A separate mainland makes the smaller landmass an island candidate.
        for(int y=40;y<470;y++) for(int x=20;x<500;x++) island.Water[y*1024+x]=WaterType.None;
        var mask=BuildVolcanoes(island.Manifest,island.Elevation,island.Water,island.Domain);
        if (Version >= 48)
        {
            var previous = BuildVolcanoes(island.Manifest with { GeneratorVersion = 47 }, island.Elevation, island.Water, island.Domain);
            if (mask.Count(v=>v==1) != previous.Count(v=>v==1)/2 ||
                !mask.Select((v,i)=>(v==2)==(previous[i]==2)).All(v=>v))
                throw new Exception("Volcanic contraction changed centers or did not halve apron area.");
            var labels = new int[33*33]; var footprint = new byte[labels.Length]; var terrain = new short[labels.Length];
            for(int y=9;y<=23;y++) for(int x=9;x<=23;x++)
            {
                int i=y*33+x; labels[i]=1; footprint[i]=1; terrain[i]=(short)(x>16?4500:2000);
            }
            footprint[16*33+16]=2; terrain[16*33+16]=3000;
            var flow = ContractVolcanicAprons(CheckManifest(33,33,false),terrain,labels,footprint,2);
            if(flow.Where((v,i)=>i%33>16 && v==1).Any() || flow.Count(v=>v==1)!=footprint.Count(v=>v==1)/2)
                throw new Exception("Volcanic propagation climbed a high ridge instead of following lower terrain.");
        }
        if(mask.Count(v=>v==2)!=1 || mask.Count(v=>v==1)>size/8 || mask.Count(v=>v==1)<12)
            throw new Exception($"Large volcanic apron exceeded island area budget or lost its center: centers={mask.Count(v=>v==2)}, apron={mask.Count(v=>v==1)}.");
        var reached=new bool[mask.Length]; var queue=new System.Collections.Generic.Queue<int>();
        int center=Array.FindIndex(mask,v=>v==2); reached[center]=true; queue.Enqueue(center); int connected=0;
        while(queue.TryDequeue(out int i))
        {
            connected++;
            for(int d=0;d<6;d++)
            {
                int n=HexGrid.NeighborIndex(HexGrid.FromOffset(i%1024,i/1024),d,1024,512,false);
                if(n>=0 && mask[n]!=0 && !reached[n]) { reached[n]=true; queue.Enqueue(n); }
            }
        }
        if(connected!=mask.Count(v=>v!=0)) throw new Exception("Volcanic apron detached from volcano.");
        var stripes = new WorldData(CheckManifest(96,96,false));
        Array.Fill(stripes.Biomes,BiomeType.Desert); Array.Fill(stripes.Temperature,(short)180); Array.Fill(stripes.Rainfall,(ushort)200);
        for(int y=0;y<96;y++) for(int x=0;x<96;x++) stripes.Elevation[y*96+x]=(short)(300+y*80);
        ApplyContextBiomes(stripes,new(0,0,96,96),true);
        int bad=0, thin=0;
        for(int y=4;y<92;y++) for(int x=4;x<92;x++)
        {
            int i=y*96+x; if(stripes.Biomes[i]!=BiomeType.Badlands) continue; bad++; int own=0;
            for(int d=0;d<6;d++) if(stripes.Biomes[HexGrid.NeighborIndex(HexGrid.FromOffset(x,y),d,96,96,false)]==BiomeType.Badlands) own++;
            if(own<3) thin++;
        }
        if(bad<20 || thin>bad/20) throw new Exception($"Badlands still form thin stripes: {thin}/{bad}.");
        var before = (BiomeType[])stripes.Biomes.Clone(); var beforeFeatures = (GeographicFeatures[])stripes.Features.Clone();
        ApplyContextBiomes(stripes,new(20,20,40,40),true);
        if (!before.SequenceEqual(stripes.Biomes) || !beforeFeatures.SequenceEqual(stripes.Features))
            throw new Exception("Context biome regional halo differs from whole-map generation.");
        Console.WriteLine($"PASS natural shapes: both polar caps attached at 96/512 rows, irregular coasts, connected volcanic apron {connected}/{size} cells, thin badlands={thin}/{bad}");
    }
}
