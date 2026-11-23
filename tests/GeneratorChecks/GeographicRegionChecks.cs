using System;
using System.IO;
using System.Linq;
using System.Collections.Immutable;
using System.IO.Compression;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    internal static void CheckGeographicRegions()
    {
        foreach(bool wrap in new[]{false,true})
        {
            var w=new WorldData(CheckManifest(96,64,wrap));
            Array.Fill(w.Water,WaterType.Ocean);Array.Fill(w.Elevation,(short)-2500);
            void Land(int x,int y,int width,int height)
            { for(int row=y;row<y+height;row++)for(int col=x;col<x+width;col++){int i=row*96+col;w.Water[i]=WaterType.None;w.Elevation[i]=300;} }
            Land(2,5,28,53);Land(48,15,4,4);Land(55,15,4,4);Land(62,15,4,4);Land(80,48,2,2);
            for(int y=25;y<29;y++)for(int x=12;x<17;x++){w.Water[y*96+x]=WaterType.Lake;w.Elevation[y*96+x]=-30;}
            var water=(WaterType[])w.Water.Clone();var heights=(short[])w.Elevation.Clone();
            w.Tectonics=new(ImmutableArray.Create(new PlateReference(60,20,0,0,false),new PlateReference(130,65,0,0,false),
                new PlateReference(110,25,0,0,false,0.3f,PlateKind.OceanicMargin)),ImmutableArray<PlateContact>.Empty);
            var regions=GeographicRegions.Get(w);
            foreach(var kind in new[]{RegionKind.Continent,RegionKind.Archipelago,RegionKind.Island,RegionKind.Lake,RegionKind.Ocean})
                if(!regions.Regions.Any(r=>r.Kind==kind))throw new Exception($"Region kind missing: {kind} wrap={wrap}");
            if(regions.At(16*96+49)?.Id!=regions.At(16*96+56)?.Id || regions.At(16*96+53)?.Kind==RegionKind.Archipelago)
                throw new Exception("Archipelago merged water or failed to group neighboring islands.");
            if(!water.SequenceEqual(w.Water)||!heights.SequenceEqual(w.Elevation))throw new Exception("Region classification changed terrain.");
            var again=GeographicRegions.Build(w);
            if(!regions.CellIds.SequenceEqual(again.CellIds)||!regions.Regions.SequenceEqual(again.Regions))throw new Exception("Geographic IDs/names are not deterministic.");
            var continent=regions.At(10*96+10)!;regions.Rename(continent.Id,"Terras de Teste");
            w.Elevation[10*96+10]=450;w.GeographyDirty=true;
            if(GeographicRegions.Get(w).At(10*96+10)?.Name!="Terras de Teste")throw new Exception("Terrain update lost named region identity.");
            var copy=w.CreateEditCopy();copy.Geography!.Rename(continent.Id,"Rascunho");
            if(w.Geography!.At(10*96+10)!.Name!="Terras de Teste")throw new Exception("Edit copy changed source region name.");
            string path=Path.Combine(Path.GetTempPath(),$"regions-{Guid.NewGuid():N}.world");
            try
            {
                w.Water[10*96+10]=WaterType.Lake;
                w.InvalidateGeography();
                WorldFile.Save(path,w,null);var loaded=WorldFile.Load(path);
                if(!w.Geography.CellIds.SequenceEqual(loaded.Geography!.CellIds)||!w.Geography.Regions.SequenceEqual(loaded.Geography.Regions))throw new Exception("Region save/load lost names or IDs.");
                using(var archive=ZipFile.Open(path,ZipArchiveMode.Update))
                { archive.GetEntry("layers/geographic_regions.bin")!.Delete();archive.GetEntry("geography.json")!.Delete(); }
                var legacy=WorldFile.Load(path);
                if(legacy.Geography is not null || !legacy.Water.SequenceEqual(w.Water))throw new Exception("Optional region layer broke old saves.");
                GeographicRegions.Get(legacy);
                WorldFile.Save(path,w,null);
                using(var archive=ZipFile.Open(path,ZipArchiveMode.Update))archive.GetEntry("layers/geographic_regions.bin")!.Delete();
                bool rejected=false;try{WorldFile.Load(path);}catch(InvalidDataException){rejected=true;}
                if(!rejected)throw new Exception("Incomplete geographic layer accepted.");
            }
            finally{File.Delete(path);}
            Console.WriteLine($"PASS geographic fixture wrap={wrap}: continent, islands, archipelago, ocean, lake, stable names, copy, save/load, corruption");
        }
        foreach(int seed in new[]{1234,1,-77})
        {
            var size=MapDomain.ExpandedSize(256,128);var m=CheckManifest(size.Width,size.Height,false) with{Seed=seed};
            m=m with{Generation=m.Generation with{Extent=MapExtent.World,ReferenceWidth=256,ReferenceHeight=128}};
            var w=Create(m);var g=GeographicRegions.Get(w);
            if(g.Regions.Sum(r=>r.Cells)!=w.ActiveCount)throw new Exception("Regions did not partition atlas exactly.");
            foreach(var r in g.Regions)
            {
                if(g.CellIds[r.Anchor]!=r.Id||string.IsNullOrWhiteSpace(r.Name)||r.AreaKm2<=0)throw new Exception("Invalid region anchor/name/area.");
                int actual=g.CellIds.Count(id=>id==r.Id);if(actual!=r.Cells)throw new Exception("Invalid region cell count.");
            }
            for(int i=0;i<w.Count;i++)if((g.CellIds[i]!=0)!=w.Domain.Contains(i))throw new Exception("Regions crossed curved atlas boundary.");
            if(g.Regions.Any(r=>r.Plate>=0 && w.Tectonics!.Plates[r.Plate].Kind!=PlateKind.Normal))throw new Exception("Polar/edge microplates created named ocean seeds.");
            Console.WriteLine($"PASS named atlas seed={seed}: {string.Join(", ",g.Regions.GroupBy(r=>r.Kind).Select(c=>$"{c.Key}={c.Count()}"))}");
        }
    }
}
