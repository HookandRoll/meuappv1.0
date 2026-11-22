using System;
using System.Collections.Generic;
using System.Linq;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    private sealed partial class ContinentalSculpture
    {
        // Retained only to reproduce v51 saves and regional regeneration.
        private void ShapeSmallIslandsLegacy51(WaterType[] water, float[] uplift)
        {
            var owner=new int[water.Length];var sizes=new List<int>{0};var peaks=new List<float>{0};
            var queue=new Queue<int>();
            for(int start=0;start<water.Length;start++)
            {
                if(water[start]!=WaterType.None || owner[start]!=0)continue;
                int id=sizes.Count,size=0;float peak=0;owner[start]=id;queue.Enqueue(start);
                while(queue.TryDequeue(out int cell))
                {
                    size++;peak=Math.Max(peak,uplift[cell]);
                    for(int d=0;d<6;d++)
                    {
                        int n=Neighbor(HexAt(cell),d);
                        if(n<0 || water[n]!=WaterType.None || owner[n]!=0)continue;
                        owner[n]=id;queue.Enqueue(n);
                    }
                }
                sizes.Add(size);peaks.Add(peak);
            }
            var mainlands=Enumerable.Range(1,sizes.Count-1).OrderByDescending(id=>sizes[id]).Take(_settings.Continents).ToHashSet();
            int continentMin=Math.Max(32,(int)(sizes.Sum()*0.05));
            var selected=new bool[sizes.Count];
            for(int id=1;id<sizes.Count;id++)
                selected[id]=!mainlands.Contains(id) && sizes[id]<continentMin && sizes[id]>9 &&
                    !(peaks[id]>=0.65f && sizes[id]<=Math.Max(32,_height/4));
            if(!selected.Any(v=>v))return;
            uint Rank(int cell)
            {
                uint h=unchecked((uint)cell*0x9E3779B9u ^ _seed ^ 0x79C315A9u);
                h=(h^(h>>16))*0x7FEB352Du;return h^(h>>15);
            }
            var original=(WaterType[])water.Clone();
            var families=new Dictionary<int,List<int>>();int removed=0;
            for(int cell=0;cell<water.Length;cell++)
                if(selected[owner[cell]])
                {
                    if(!families.TryGetValue(owner[cell],out var cells))families[owner[cell]]=cells=new();
                    cells.Add(cell);water[cell]=WaterType.Lake;removed++;
                }
            foreach(var family in families)
            {
                int kept=0,target=Math.Max(3,(int)(family.Value.Count*0.45f));
                foreach(int center in family.Value.OrderByDescending(i=>_scores[i]+(Rank(i)&255)*0.0007f).ThenBy(Rank))
                {
                    if(kept>=target || water[center]==WaterType.None)continue;
                    bool clear=true;
                    for(int d=0;d<6;d++){int n=Neighbor(HexAt(center),d);if(n>=0&&water[n]==WaterType.None){clear=false;break;}}
                    if(!clear)continue;
                    bool companion=false;
                    for(int dy=-4;dy<=4 && !companion;dy++)for(int dx=-4;dx<=4;dx++)
                    {
                        int y=center/_width+dy,x=center%_width+dx;
                        if(_wrap)x=(x+_width)%_width;
                        if((uint)y>=_height || (uint)x>=_width)continue;
                        int n=y*_width+x;
                        if(owner[n]==family.Key && water[n]==WaterType.None && HexGrid.Distance(HexAt(center),HexAt(n),_width,_wrap)<=4){companion=true;break;}
                    }
                    bool islet=kept>=7 && companion && Rank(center)%5==0;
                    int goal=islet?1:Math.Min(target-kept,3+(int)(Rank(center)%7));
                    if(goal<3 && !islet)continue;
                    var patch=new HashSet<int>();var offered=new HashSet<int>();
                    var front=new PriorityQueue<int,(float Rank,int Cell)>();front.Enqueue(center,(0,center));offered.Add(center);
                    while(patch.Count<goal && front.TryDequeue(out int cell,out _))
                    {
                        bool joins=false;
                        for(int d=0;d<6;d++)
                        {int n=Neighbor(HexAt(cell),d);if(n>=0 && water[n]==WaterType.None && !patch.Contains(n)){joins=true;break;}}
                        if(joins)continue;
                        patch.Add(cell);water[cell]=WaterType.None;
                        for(int d=0;d<6;d++)
                        {
                            int n=Neighbor(HexAt(cell),d);
                            if(n<0 || owner[n]!=family.Key || water[n]==WaterType.None || !offered.Add(n))continue;
                            float cost=(Rank(n)&65535)/65535f+Math.Abs(_scores[n]-_scores[center])*0.3f;
                            front.Enqueue(n,(cost,n));
                        }
                    }
                    if(patch.Count<3 && !islet){foreach(int cell in patch)water[cell]=WaterType.Lake;continue;}
                    kept+=patch.Count;removed-=patch.Count;
                }
            }
            // Compensate on existing mainlands, not on the newly separated
            // islands. Simple-point additions preserve channels and land groups.
            var coast=new PriorityQueue<int,(float Score,int Cell)>();var queued=new bool[water.Length];
            int CoastOwner(int cell)
            {
                int found=0;
                for(int d=0;d<6;d++)
                {
                    int n=Neighbor(HexAt(cell),d);
                    if(n<0 || water[n]!=WaterType.None || selected[owner[n]])continue;
                    if(mainlands.Contains(owner[n]) || sizes[owner[n]]>=continentMin)found=owner[n];
                }
                return found;
            }
            void Offer(int cell)
            {
                if(cell<0 || queued[cell] || water[cell]==WaterType.None || selected[owner[cell]] || !AtlasLandEligible(cell) || CoastOwner(cell)==0)return;
                queued[cell]=true;coast.Enqueue(cell,(-_scores[cell],cell));
            }
            for(int cell=0;cell<water.Length;cell++)Offer(cell);
            while(removed>0 && coast.TryDequeue(out int cell,out _))
            {
                int id=CoastOwner(cell);
                if(id==0 || !SimpleWaterMargin(water,cell))continue;
                water[cell]=WaterType.None;owner[cell]=id;removed--;
                for(int d=0;d<6;d++)Offer(Neighbor(HexAt(cell),d));
            }
            if(removed!=0)Array.Copy(original,water,water.Length);
        }
    }
}
