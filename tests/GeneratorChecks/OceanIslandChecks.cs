using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.IO;
using System.Text.Json;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    internal static void CheckOceansAndSmallIslands(bool review = false)
    {
        foreach(bool wrap in new[]{false,true})
        {
            var w=new WorldData(CheckManifest(96,64,wrap));Array.Fill(w.Water,WaterType.Ocean);Array.Fill(w.Elevation,(short)-200);
            for(int y=8;y<56;y+=6)for(int x=20;x<80;x+=7)
            {w.Water[y*96+x]=WaterType.None;w.Elevation[y*96+x]=100;}
            var g=GeographicRegions.Get(w);
            if(g.Regions.Any(r=>r.Kind==RegionKind.Sea)||g.Regions.Count(r=>r.Kind==RegionKind.Ocean)<2)
                throw new Exception("Small islands/shallow water created false seas or lost the two oceans.");
            if(g.CellIds[32*96]==g.CellIds[32*96+95])throw new Exception("West and east ocean cores merged.");
            int oceanMin = (int)Math.Ceiling(w.Water.Count(v => v == WaterType.Ocean) * 0.08);
            if (g.At(32*96)!.Cells < oceanMin || g.At(32*96+95)!.Cells < oceanMin)
                throw new Exception("A main ocean became a tiny edge strip.");
        }
        var bay=new WorldData(CheckManifest(96,64,false));Array.Fill(bay.Water,WaterType.Ocean);Array.Fill(bay.Elevation,(short)-1800);
        for(int y=15;y<44;y++)for(int x=30;x<56;x++){bay.Water[y*96+x]=WaterType.None;bay.Elevation[y*96+x]=200;}
        for(int y=20;y<38;y++)for(int x=35;x<50;x++){bay.Water[y*96+x]=WaterType.Ocean;bay.Elevation[y*96+x]=-300;}
        for(int x=30;x<35;x++){bay.Water[28*96+x]=WaterType.Ocean;bay.Elevation[28*96+x]=-300;}
        bay.Tectonics=new(ImmutableArray.Create(new PlateReference(42*HexGrid.Root3,28*1.5f,0,0,false)),ImmutableArray<PlateContact>.Empty);
        var regions=GeographicRegions.Get(bay);
        if(regions.At(28*96+42)?.Kind!=RegionKind.Sea)throw new Exception("A continental enclosed bay did not become a sea.");
        for (int x=50;x<56;x++) { bay.Water[28*96+x]=WaterType.Ocean; bay.Elevation[28*96+x]=-300; }
        bay.InvalidateGeography();
        if (GeographicRegions.Get(bay).At(28*96+42)?.Kind != RegionKind.Ocean)
            throw new Exception("An open channel with two outlets became a sea.");
        Console.WriteLine("PASS marine rules: two protected side oceans, islands do not enclose seas, shallow open water stays ocean, continental bay becomes sea");
        (int Small,int Large,int Singles,int Land) Stats(WorldData w)
        {
            var seen=new bool[w.Count];var queue=new Queue<int>();var sizes=new List<int>();int land=0;
            var polar=new PolarIceField(w.Manifest);
            for(int start=0;start<w.Count;start++)
            {
                if(seen[start]||!w.Domain.Contains(start)||w.Water[start]!=WaterType.None||polar.IsPolar(start))continue;
                seen[start]=true;queue.Enqueue(start);int size=0;
                while(queue.TryDequeue(out int cell))
                {
                    size++;
                    for(int d=0;d<6;d++)
                    {
                        int n=HexGrid.NeighborIndex(HexGrid.FromOffset(cell%w.Width,cell/w.Width),d,w.Width,w.Height,w.Manifest.HorizontalWrap);
                        if(!w.Domain.Contains(n)||seen[n]||w.Water[n]!=WaterType.None||polar.IsPolar(n))continue;
                        seen[n]=true;queue.Enqueue(n);
                    }
                }
                land+=size;sizes.Add(size);
            }
            int min=Math.Max(32,(int)(land*0.05));
            return(sizes.Count(n=>n is >=3 and <=9),sizes.Count(n=>n>9&&n<min),sizes.Count(n=>n==1),land);
        }
        (int[] Labels, List<int> Sizes) Components(WorldData w)
        {
            var labels = new int[w.Count]; var sizes = new List<int> { 0 }; var queue = new Queue<int>();
            for (int start = 0; start < w.Count; start++)
            {
                if (!w.Domain.Contains(start) || labels[start] != 0 || w.Water[start] != WaterType.None) continue;
                int id = sizes.Count, size = 0; labels[start] = id; queue.Enqueue(start);
                while (queue.TryDequeue(out int cell))
                {
                    size++;
                    for (int d = 0; d < 6; d++)
                    {
                        int n = HexGrid.NeighborIndex(HexGrid.FromOffset(cell % w.Width, cell / w.Width), d, w.Width, w.Height, w.Manifest.HorizontalWrap);
                        if (!w.Domain.Contains(n) || labels[n] != 0 || w.Water[n] != WaterType.None) continue;
                        labels[n] = id; queue.Enqueue(n);
                    }
                }
                sizes.Add(size);
            }
            return (labels, sizes);
        }
        void Preserve(WorldData original, WorldData current)
        {
            var a = Components(original); var b = Components(current);
            int min = Math.Max(32, (int)Math.Ceiling(a.Sizes.Sum() * 0.05));
            var mapped = new int[a.Sizes.Count]; var removed = new int[a.Sizes.Count];
            var inherited = new HashSet<int>(); int added = 0;
            for (int i = 0; i < original.Count; i++)
            {
                int id = a.Labels[i], next = b.Labels[i];
                if (id == 0) { if (next != 0) added++; continue; }
                if (next == 0)
                {
                    if (a.Sizes[id] < min) throw new Exception("An original island lost land.");
                    removed[id]++; continue;
                }
                if (mapped[id] != 0 && mapped[id] != next) throw new Exception("An original landmass was fragmented.");
                mapped[id] = next; inherited.Add(next);
            }
            if (mapped.Skip(1).Any(id => id == 0) || mapped.Skip(1).Distinct().Count() != a.Sizes.Count - 1)
                throw new Exception("An original landmass disappeared or merged.");
            for (int id = 1; id < a.Sizes.Count; id++)
            {
                if (removed[id] > a.Sizes[id] / 100) throw new Exception("Continental coast compensation exceeded one percent.");
                if (a.Sizes[id] < min && a.Sizes[id] != b.Sizes[mapped[id]]) throw new Exception("An original island changed size.");
            }
            for (int id = 1; id < b.Sizes.Count; id++)
                if (!inherited.Contains(id) && b.Sizes[id] != 1 && b.Sizes[id] is not (>= 3 and <= 9))
                    throw new Exception("An added island has an unexpected size.");
            if (added != removed.Sum() || added > Math.Min(192, (int)(a.Sizes.Sum() * 0.003)))
                throw new Exception("Additions changed the land quota or exceeded the moderate budget.");
            for (int i = 0; i < original.Count; i++)
                if (original.Water[i] == WaterType.None && current.Water[i] != WaterType.None && current.Water[i] != WaterType.Ocean)
                    throw new Exception("Coastal compensation drilled a lake into a landmass.");
        }
        bool rejectedLegacy = false; int addedSmall = 0, addedSingles = 0;
        foreach(int height in new[]{64,256})foreach(int seed in new[]{1234,1,-77})foreach(bool wrap in new[]{false,true})
        {
            var m=CheckManifest(height*2,height,wrap) with{Seed=seed};m=m with{HorizontalWrap=false,Generation=m.Generation with{Stage=GenerationStage.Land,Extent=MapExtent.World}};
            var old=Create(m with{GeneratorVersion=50});var current=Create(m with{GeneratorVersion=52});
            var a=Stats(old);var b=Stats(current);
            Preserve(old, current);
            if(a.Land!=b.Land)throw new Exception("Island additions changed the nonpolar land quota.");
            var repeat=Create(m with{GeneratorVersion=52});
            if(!current.Water.SequenceEqual(repeat.Water)||!current.Elevation.SequenceEqual(repeat.Elevation))throw new Exception("Island additions are not deterministic.");
            addedSmall += b.Small-a.Small; addedSingles += b.Singles-a.Singles;
            Console.WriteLine($"ISLANDS {m.Width}x{height} seed={seed} wrap={wrap}: 3..9 {a.Small}->{b.Small}; >9 {a.Large}->{b.Large}; single {a.Singles}->{b.Singles}; land={b.Land}; preserved");
            if (seed != 1 && !wrap)
            {
                var broken = Create(m with { GeneratorVersion = 51 });
                try { Preserve(old, broken); } catch (Exception) { rejectedLegacy = true; }
                if (review)
                {
                    Directory.CreateDirectory(".godot/island-review52");
                    foreach (var sample in new[]{old,broken,current})
                    {
                        var g = GeographicRegions.Get(sample);
                        File.WriteAllText($".godot/island-review52/{seed}-{height}-v{sample.Manifest.GeneratorVersion}.json", JsonSerializer.Serialize(new
                        { sample.Width, sample.Height, Water = sample.Water.Select(w => (int)w).ToArray(), Regions = g.CellIds,
                          Kinds = g.Regions.ToDictionary(r => r.Id, r => (int)r.Kind) }));
                    }
                }
            }
        }
        if (!rejectedLegacy) throw new Exception("Preservation checks failed to detect the destructive v51 recipe.");
        if (addedSmall == 0 || addedSingles == 0) throw new Exception("The sample has no added small islands or companion islets.");
        foreach (int percent in new[]{0,5,35,100})
        {
            var m=CheckManifest(128,64,false);m=m with{Generation=m.Generation with{Stage=GenerationStage.Land,LandPercent=percent,Extent=MapExtent.World}};
            Preserve(Create(m with{GeneratorVersion=50}),Create(m with{GeneratorVersion=52}));
        }
        Console.WriteLine("PASS island preservation, size diversity, exact quota, bounded coast edits, determinism, slider extremes; v51 rejected");
    }
}
