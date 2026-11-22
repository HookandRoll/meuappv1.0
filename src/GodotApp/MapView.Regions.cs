using System;
using System.Collections.Generic;
using Godot;
using Mapmaker.Core;

namespace Mapmaker.GodotApp;

public partial class MapView
{
    private ImageTexture? _regionTexture;
    private RegionLabels _regionLabels=null!;
    private AcceptDialog _regionRename=null!;
    private LineEdit _regionName=null!;
    private int _renamingRegion;

    private void RefreshRegions()
    {
        if(_world is null || _world.Manifest.Generation.Stage==GenerationStage.Blank)return;
        var geography=GeographicRegions.Get(_world);
        var bytes=new byte[_world.Count*4];
        var colors=new System.Collections.Generic.Dictionary<int,Color>();
        int oceanIndex=0;
        foreach(var region in geography.Regions)
        {
            uint hash=unchecked((uint)region.Id*0x9E3779B9u); hash^=hash>>16;
            float variation=(hash&65535)/65535f;
            float hue=region.Kind switch { RegionKind.Ocean=>0.53f+variation*0.12f,RegionKind.Sea=>0.43f+variation*0.08f,
                RegionKind.Lake=>0.68f+variation*0.10f,RegionKind.Continent=>0.08f+variation*0.22f,
                RegionKind.Archipelago=>0.86f+variation*0.09f,_=>0.02f+variation*0.06f };
            colors[region.Id]=Color.FromHsv(hue,0.42f+variation*0.2f,0.55f+variation*0.25f);
            if(region.Kind==RegionKind.Ocean)
            {
                colors[region.Id]=Color.FromHsv(0.49f+(oceanIndex%3)*0.065f,0.55f,0.60f+(oceanIndex%3)*0.10f);
                oceanIndex++;
            }
        }
        for(int i=0;i<_world.Count;i++)
        {
            Color color=colors.GetValueOrDefault(geography.CellIds[i],Colors.Black);
            bytes[i*4]=(byte)(color.R*255);bytes[i*4+1]=(byte)(color.G*255);bytes[i*4+2]=(byte)(color.B*255);bytes[i*4+3]=255;
        }
        using var image=Image.CreateFromData(_world.Width,_world.Height,false,Image.Format.Rgba8,bytes);
        var old=_regionTexture; _regionTexture=ImageTexture.CreateFromImage(image);
        _material.SetShaderParameter("region_data",_regionTexture); old?.Dispose();
        _regionLabels.World=_world;_regionLabels.Zoom=_map.Scale.X;_regionLabels.QueueRedraw();
    }

    private void ShowRegionRename()
    {
        if(_world is null || _selected==NoHex)return;
        if(_editorPanel.Visible) { _message.DialogText="Finalize a edição do terreno antes de renomear uma região.";_message.PopupCentered();return; }
        var region=GeographicRegions.Get(_world).At(_selected.Y*_world.Width+_selected.X);
        if(region is null)return;
        _renamingRegion=region.Id;_regionRename.Title=$"Renomear {GeographicRegions.Category(region.Kind).ToLowerInvariant()}";
        _regionName.Text=region.Name;_regionRename.PopupCentered(new Vector2I(420,120));_regionName.GrabFocus();_regionName.SelectAll();
    }
}
