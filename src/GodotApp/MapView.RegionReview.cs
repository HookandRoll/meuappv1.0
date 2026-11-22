using System;
using System.Linq;
using System.IO;
using Godot;
using Mapmaker.Core;

namespace Mapmaker.GodotApp;

public partial class MapView
{
    private async void RunRegionReview()
    {
        try
        {
            var size=MapDomain.ExpandedSize(384,192);
            foreach(int seed in new[]{1234,1,-77})
            {
                var m=TestManifest(size.Width,size.Height,TerrainAlgorithm.ContinentalSculpt) with {Seed=seed,GeneratorVersion=WorldGenerator.Version};
                m=m with{Generation=m.Generation with{Extent=MapExtent.World,ReferenceWidth=384,ReferenceHeight=192}};
                var world=await System.Threading.Tasks.Task.Run(()=>
                { var w=WorldGenerator.Create(m);GeographicRegions.Get(w);return w; });
                InstallWorld(world);RefreshInterface();SetLayer(7);
                await NextDraw();await NextDraw();Screenshot($"regions-v{WorldGenerator.Version}-{seed}.png");
                Require(_regionLabels.Visible && _regionTexture is not null,"Regioes sem camada visual.");
                var region=world.Geography!.Regions.First(r=>r.Kind==RegionKind.Continent);
                SelectHex(new Vector2I(region.Anchor%world.Width,region.Anchor/world.Width));
                Require(_inspection.Text.Contains(region.Name),"Inspecao sem nome da regiao.");
                ShowRegionRename();_regionName.Text="Continente de Verificação";_regionRename.EmitSignal(AcceptDialog.SignalName.Confirmed);_regionRename.Hide();
                Require(world.Geography.At(region.Anchor)!.Name=="Continente de Verificação" && _dirty,"Renomear nao atualizou regiao.");
                string path=Path.Combine(Path.GetTempPath(),$"region-review-{Guid.NewGuid():N}.world");
                try{WorldFile.Save(path,world,null);var loaded=WorldFile.Load(path);Require(loaded.Geography!.At(region.Anchor)!.Name=="Continente de Verificação","Nome perdido no arquivo.");}
                finally{File.Delete(path);}
                SetLayer(0);_layerSelect.Select(0);FitMap();
                await NextDraw();await NextDraw();Screenshot($"islands-v{WorldGenerator.Version}-{seed}.png");
                var group=world.Geography.Regions.Where(r=>r.Kind==RegionKind.Archipelago).OrderByDescending(r=>r.Cells).FirstOrDefault();
                if(group is not null)
                {
                    int focus=group.Anchor;
                    var types=world.Geography.Regions.ToDictionary(r=>r.Id,r=>r.Kind);
                    for(int cell=0;cell<world.Count;cell++)
                    {
                        if(world.Water[cell]!=WaterType.None || world.Biomes[cell]==BiomeType.Glacier || types[world.Geography.CellIds[cell]]!=RegionKind.Archipelago)continue;
                        bool single=true;
                        for(int d=0;d<6;d++)
                        {
                            int n=HexGrid.NeighborIndex(HexGrid.FromOffset(cell%world.Width,cell/world.Width),d,world.Width,world.Height,world.Manifest.HorizontalWrap);
                            if(n>=0 && world.Water[n]==WaterType.None){single=false;break;}
                        }
                        if(single){focus=cell;break;}
                    }
                    var p=HexGrid.Center(HexGrid.FromOffset(focus%world.Width,focus/world.Width));
                    _map.Scale=Vector2.One*12;_map.Position=MapCanvasRect().GetCenter()-new Vector2(p.X,p.Y)*12;UpdateArtworkZoom();
                    await NextDraw();await NextDraw();Screenshot($"archipelago-v{WorldGenerator.Version}-{seed}.png");
                }
                SetLayer(7);FitMap();
                SetEditorPanelOpen(true);EditSingleHex(HexLocks.Elevation,world.Elevation[region.Anchor]+10);
                EditHistory(false);EditHistory(true);CancelEditorDraft();
                Require(_world==world && world.Geography.At(region.Anchor)!.Name=="Continente de Verificação","Cancelar edicao perdeu nome.");
                GD.Print($"REGIONS seed={seed}: {string.Join(", ",world.Geography.Regions.GroupBy(r=>r.Kind).Select(g=>$"{g.Key}={g.Count()}"))}");
            }
            GD.Print("REGIONS REVIEW PASSED: atlas layer, labels, inspection, rename, save/load, edit/undo/redo/cancel.");GetTree().Quit();
        }
        catch(Exception error){GD.PushError(error.ToString());GetTree().Quit(1);}
    }
}
