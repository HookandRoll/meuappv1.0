using System.Collections.Generic;
using System.Linq;
using Godot;
using Mapmaker.Core;

namespace Mapmaker.GodotApp;

internal partial class RegionLabels : Node2D
{
    internal WorldData? World;
    internal float Zoom=1;
    public override void _Draw()
    {
        if(World is null || World.Geography is null) return;
        var occupied=new List<Rect2>();
        foreach(var region in World.Geography.Regions.OrderByDescending(r=>r.Cells))
        {
            if(region.Cells*Zoom*Zoom<400)continue;
            var point=HexGrid.Center(HexGrid.FromOffset(region.Anchor%World.Width,region.Anchor/World.Width));
            var pos=new Vector2(point.X,point.Y);
            Vector2 text=ThemeDB.FallbackFont.GetStringSize(region.Name,fontSize:15);
            Vector2 screen=GetGlobalTransformWithCanvas()*pos;
            var box=new Rect2(screen-text*0.5f-new Vector2(6,4),text+new Vector2(12,8));
            if(occupied.Any(other=>other.Intersects(box)))continue;
            occupied.Add(box);
            DrawSetTransform(pos,0,Vector2.One/Zoom);
            Vector2 origin=new(-text.X*0.5f,5);
            DrawStringOutline(ThemeDB.FallbackFont,origin,region.Name,fontSize:15,size:4,modulate:new Color("#101b20"));
            DrawString(ThemeDB.FallbackFont,origin,region.Name,fontSize:15,modulate:new Color("#f4edda"));
        }
        DrawSetTransform(Vector2.Zero);
    }
}
