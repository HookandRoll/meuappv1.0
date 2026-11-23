using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Mapmaker.Core;

public enum RegionKind { Continent, Island, Archipelago, Ocean, Sea, Lake }
public sealed record GeographicRegion(int Id, RegionKind Kind, string Name, int Cells, int Anchor,
    double AreaKm2, double MeanElevation, int Plate);

public sealed class GeographicRegions
{
    public const int AlgorithmVersion = 2;
    public int Version { get; init; } = AlgorithmVersion;
    public int[] CellIds { get; init; } = Array.Empty<int>();
    private GeographicRegion[] _regions = Array.Empty<GeographicRegion>();
    private Dictionary<int, GeographicRegion>? _byId;
    public GeographicRegion[] Regions
    {
        get => _regions;
        set { _regions = value; _byId = null; }
    }
    [System.Text.Json.Serialization.JsonIgnore]
    public ulong SourceHash { get; init; }
    internal static ulong SurfaceHash(WorldData world)
    {
        ulong hash=14695981039346656037UL;
        for(int i=0;i<world.Count;i++)hash=unchecked((hash^((ulong)(ushort)world.Elevation[i]<<8 | (byte)world.Water[i]))*1099511628211UL);
        return hash;
    }
    public static string Category(RegionKind kind) => kind switch
    {
        RegionKind.Continent => "Continente", RegionKind.Archipelago => "Arquipélago",
        RegionKind.Ocean => "Oceano", RegionKind.Sea => "Mar", RegionKind.Lake => "Lago", _ => "Ilha"
    };
    public GeographicRegion? At(int cell)
    {
        if ((uint)cell >= CellIds.Length) return null;
        _byId ??= Regions.ToDictionary(r => r.Id);
        return _byId.GetValueOrDefault(CellIds[cell]);
    }
    public void Rename(int id, string name)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 80 || name.Any(char.IsControl)) throw new ArgumentException("Use um nome de 1 a 80 caracteres.");
        int index = Array.FindIndex(Regions, r => r.Id == id);
        if (index < 0) throw new ArgumentException("Região inexistente.");
        Regions[index] = Regions[index] with { Name = name };
        if (_byId is not null) _byId[id] = Regions[index];
    }
    public GeographicRegions Copy() => new() { Version = Version, CellIds = CellIds, Regions = (GeographicRegion[])Regions.Clone(), SourceHash=SourceHash };
    public static GeographicRegions Get(WorldData world)
    {
        if (world.Geography is null || world.GeographyDirty || world.Geography.Version != AlgorithmVersion)
        {
            world.Geography = Build(world, world.Geography);
            world.GeographyDirty = false;
        }
        return world.Geography;
    }

    private static (int[] Labels, List<RegionKind> Kinds, List<int> Plates, int West, int East) Classify(WorldData w,
        WaterType[] water, short[] heights, bool groupIslands)
    {
        int count = w.Count, width = w.Width, height = w.Height;
        var labels = new int[count]; var sizes = new List<int> { 0 };
        var kinds = new List<RegionKind> { RegionKind.Island }; var anchors = new List<int> { 0 };
        var plates = new List<int> { -1 }; var queue = new Queue<int>();
        int Neighbor(int cell, int d) => HexGrid.NeighborIndex(HexGrid.FromOffset(cell % width, cell / width), d, width, height, w.Manifest.HorizontalWrap);
        int Medium(int cell) => water[cell] == WaterType.None ? 0 : water[cell] == WaterType.Ocean ? 1 : 2;
        int land = 0, marine = 0;
        for (int start = 0; start < count; start++)
        {
            if (!w.Domain.Contains(start) || labels[start] != 0) continue;
            int medium = Medium(start), id = sizes.Count, size = 0;
            labels[start] = id; queue.Enqueue(start);
            while (queue.TryDequeue(out int cell))
            {
                size++;
                for (int d = 0; d < 6; d++)
                {
                    int n = Neighbor(cell, d);
                    if (!w.Domain.Contains(n) || labels[n] != 0 || Medium(n) != medium) continue;
                    labels[n] = id; queue.Enqueue(n);
                }
            }
            sizes.Add(size); anchors.Add(start); plates.Add(-1);
            kinds.Add(medium == 0 ? RegionKind.Island : medium == 1 ? RegionKind.Ocean : RegionKind.Lake);
            if (medium == 0) land += size; if (medium == 1) marine += size;
        }
        int continentMin = Math.Max(32, (int)Math.Ceiling(land * 0.05));
        int oceanMin = Math.Max(64, (int)Math.Ceiling(marine * 0.08));
        int seaMin = Math.Max(12, (int)Math.Ceiling(marine * 0.005));
        for (int id = 1; id < sizes.Count; id++) if (kinds[id] == RegionKind.Island && sizes[id] >= continentMin) kinds[id] = RegionKind.Continent;

        // Each seed starts in real marine water. Weighted expansion cannot jump
        // over land; shelves and narrow straits are slower than deep basins.
        var seeds = new Dictionary<int, int>();
        if (w.Tectonics is { } tectonics)
            for (int p = 0; p < tectonics.Plates.Length; p++)
            {
                var plate = tectonics.Plates[p];
                if (plate.Kind != PlateKind.Normal || plate.Continental) continue;
                int x = Math.Clamp((int)(plate.X / HexGrid.Root3), 0, width - 1), y = Math.Clamp((int)(plate.Y / 1.5f), 0, height - 1);
                int best = -1; float nearest = float.PositiveInfinity;
                int radius = Math.Max(4, height / 8);
                for (int row = Math.Max(0, y - radius); row <= Math.Min(height - 1, y + radius); row += Math.Max(1, height / 256))
                for (int dx = -radius; dx <= radius; dx += Math.Max(1, height / 256))
                {
                    int col = x + dx;
                    if (w.Manifest.HorizontalWrap) col = (col + width) % width;
                    if (!w.Domain.Contains(col, row)) continue;
                    int cell = row * width + col;
                    if (water[cell] != WaterType.Ocean) continue;
                    float distance = dx * dx + (row - y) * (row - y);
                    if (distance < nearest) { best = cell; nearest = distance; }
                }
                if (best >= 0) seeds.TryAdd(best, p);
            }
        int west = -1, east = -1; float westRank=float.PositiveInfinity, eastRank=float.PositiveInfinity;
        for(int cell=0;cell<count;cell++)
        {
            if(!w.Domain.Contains(cell) || water[cell]!=WaterType.Ocean)continue;
            float polar=Math.Abs(cell/width-height*0.5f)*0.10f;
            float a=cell%width+polar,b=width-1-cell%width+polar;
            if(a<westRank){west=cell;westRank=a;}
            if(b<eastRank){east=cell;eastRank=b;}
        }
        if(east==west && marine>1)
            east=Enumerable.Range(0,count).Last(i=>w.Domain.Contains(i)&&water[i]==WaterType.Ocean&&i!=west);
        if(west>=0)seeds[west]=-1;
        if(east>=0)seeds[east]=-1;
        var seededComponents = new HashSet<int>(seeds.Keys.Select(cell => labels[cell]));
        for (int id = 1; id < sizes.Count; id++)
            if (kinds[id] == RegionKind.Ocean && !seededComponents.Contains(id)) seeds.TryAdd(anchors[id], -1);
        var costs = new float[count]; Array.Fill(costs, float.PositiveInfinity);
        var front = new PriorityQueue<int, (float Cost, int Cell)>();
        var primary=new HashSet<int>();
        foreach (var seed in seeds.OrderBy(s => s.Key))
        {
            int id = sizes.Count; sizes.Add(0); anchors.Add(seed.Key); plates.Add(seed.Value); kinds.Add(RegionKind.Ocean);
            bool side=seed.Key==west || seed.Key==east;
            if(side)primary.Add(id);
            float initial=side ? 0 : Math.Max(2,height*0.04f);
            labels[seed.Key] = id; costs[seed.Key] = initial; front.Enqueue(seed.Key, (initial, seed.Key));
        }
        var fixedSeeds=new HashSet<int>(seeds.Keys);
        while (front.TryDequeue(out int cell, out var priority))
        {
            if (priority.Cost != costs[cell]) continue;
            for (int d = 0; d < 6; d++)
            {
                int n = Neighbor(cell, d);
                if (!w.Domain.Contains(n) || water[n] != WaterType.Ocean || fixedSeeds.Contains(n)) continue;
                float shelf = Math.Clamp((heights[n] + 1500f) / 1500f, 0, 1);
                float cost = priority.Cost + 1 + shelf * 4;
                if (cost >= costs[n]) continue;
                costs[n] = cost; labels[n] = labels[cell]; front.Enqueue(n, (cost, n));
            }
        }
        var parent = Enumerable.Range(0, sizes.Count).ToArray();
        int Root(int id) { while (parent[id] != id) { parent[id] = parent[parent[id]]; id = parent[id]; } return id; }
        System.Runtime.InteropServices.CollectionsMarshal.AsSpan(sizes).Clear();
        var coastal = new int[sizes.Count]; var elevation = new long[sizes.Count];
        var continentalBorder=new int[sizes.Count];var marineBorder=new int[sizes.Count];
        var sumX=new long[sizes.Count];var sumY=new long[sizes.Count];
        var borders = new Dictionary<long, int>();
        var openings=new Dictionary<int,HashSet<int>>();
        for (int cell = 0; cell < count; cell++)
        {
            int id = labels[cell]; if (id == 0) continue;
            sizes[id]++; elevation[id] += heights[cell];sumX[id]+=cell%width;sumY[id]+=cell/width;
            if (water[cell] != WaterType.Ocean) continue;
            bool coast = false;
            for (int d = 0; d < 6; d++)
            {
                int n = Neighbor(cell, d); if (!w.Domain.Contains(n)) continue;
                bool mainland=water[n]==WaterType.None && kinds[labels[n]]==RegionKind.Continent;
                coast |= mainland;if(mainland)continentalBorder[id]++;
                int other = labels[n]; if (water[n] != WaterType.Ocean || other == id) continue;
                marineBorder[id]++;
                if(!openings.TryGetValue(id,out var mouth))openings[id]=mouth=new();mouth.Add(cell);
                long key = ((long)Math.Min(id, other) << 32) | (uint)Math.Max(id, other);
                borders[key] = borders.GetValueOrDefault(key) + 1;
            }
            if (coast) coastal[id]++;
        }
        bool Enclosed(int id)
        {
            int probe=(int)(sumY[id]/Math.Max(1,sizes[id]))*width+(int)(sumX[id]/Math.Max(1,sizes[id]));
            if((uint)probe>=count || labels[probe]!=id)probe=anchors[id];
            int blocked=0,reach=Math.Max(8,height/2);
            var hex=HexGrid.FromOffset(probe%width,probe/width);
            for(int d=0;d<6;d++)for(int step=1;step<=reach;step++)
            {
                int n=HexGrid.NeighborIndex(hex,d,width,height,w.Manifest.HorizontalWrap,step);
                if(!w.Domain.Contains(n))break;
                // Islands do not act as continental walls around a sea.
                if(water[n]==WaterType.None && kinds[labels[n]]==RegionKind.Continent){blocked++;break;}
            }
            if(blocked<5 || continentalBorder[id]<Math.Max(4,marineBorder[id]*2))return false;
            if(!openings.TryGetValue(id,out var boundary))return true;
            var pending=new HashSet<int>(boundary);int mouths=0;var flood=new Queue<int>();
            while(pending.Count>0)
            {
                if(++mouths>1)return false;
                int first=pending.First();pending.Remove(first);flood.Enqueue(first);
                while(flood.TryDequeue(out int cell))for(int d=0;d<6;d++)
                {int n=Neighbor(cell,d);if(pending.Remove(n))flood.Enqueue(n);}
            }
            return true;
        }
        var sea = new bool[sizes.Count];
        for (int id = 1; id < sizes.Count; id++)
            sea[id] = kinds[id] == RegionKind.Ocean && !primary.Contains(id) && sizes[id] >= seaMin && sizes[id] < oceanMin && Enclosed(id);
        // Side oceans are main basins, not tiny slivers left behind the
        // closest tectonic seed. Absorb a connected ocean neighbor if needed.
        foreach(int side in primary.OrderBy(id=>id))
            while(sizes[side]<oceanMin)
            {
                int best=-1,connection=0;
                foreach(var border in borders)
                {
                    int a=Root((int)(border.Key>>32)),b=Root((int)border.Key);
                    int other=a==side?b:b==side?a:-1;
                    if(other<=0 || other==side || primary.Contains(other) || sea[other] || border.Value<=connection)continue;
                    best=other;connection=border.Value;
                }
                if(best<0)break;
                parent[best]=side;sizes[side]+=sizes[best];sizes[best]=0;
            }
        foreach (int id in Enumerable.Range(1, sizes.Count - 1).OrderBy(id => sizes[id]))
        {
            if (kinds[id] != RegionKind.Ocean || primary.Contains(id) || sizes[id] == 0 || sizes[id] >= oceanMin || sea[id]) continue;
            int best = -1, connection = 0;
            foreach (var border in borders)
            {
                int a = Root((int)(border.Key >> 32)), b = Root((int)border.Key);
                int other = a == Root(id) ? b : b == Root(id) ? a : -1;
                if (other <= 0 || other == Root(id) || sea[other] || border.Value <= connection) continue;
                best = other; connection = border.Value;
            }
            if (best > 0) { int root = Root(id); parent[root] = best; sizes[best] += sizes[root]; sizes[root] = 0; }
        }
        for (int id = 1; id < sea.Length; id++) if (sea[id] && Root(id) == id) kinds[id] = RegionKind.Sea;

        if (!groupIslands)
        {
            for (int cell = 0; cell < count; cell++) if (labels[cell] != 0) labels[cell] = Root(labels[cell]);
            return (labels, kinds, plates, west, east);
        }

        // Nearby islands share an archipelago ID, but intervening water keeps
        // its marine ID. Land bridges are never created by classification.
        var islandOwner = new int[count]; var reach = new byte[count];
        int islandReach = Math.Clamp(height / 64, 2, 24);
        for (int cell = 0; cell < count; cell++)
            if (labels[cell] != 0 && kinds[labels[cell]] == RegionKind.Island)
            { islandOwner[cell] = labels[cell]; reach[cell] = 1; queue.Enqueue(cell); }
        while (queue.TryDequeue(out int cell))
        {
            if (reach[cell] > islandReach) continue;
            for (int d = 0; d < 6; d++)
            {
                int n = Neighbor(cell, d);
                if (!w.Domain.Contains(n) || (water[n] != WaterType.Ocean && kinds[labels[n]] != RegionKind.Island)) continue;
                if (islandOwner[n] != 0)
                { int a = Root(islandOwner[cell]), b = Root(islandOwner[n]); if (a != b) parent[Math.Max(a,b)] = Math.Min(a,b); continue; }
                islandOwner[n] = islandOwner[cell]; reach[n] = (byte)(reach[cell] + 1); queue.Enqueue(n);
            }
        }
        var members = new int[sizes.Count]; var islandArea = new int[sizes.Count];
        for (int id = 1; id < sizes.Count; id++) if (kinds[id] == RegionKind.Island && sizes[id] > 0)
        { members[Root(id)]++; islandArea[Root(id)]+=sizes[id]; }
        int archipelagoMin=8;
        var roots=Enumerable.Range(0,sizes.Count).Select(Root).ToArray();
        for (int id = 1; id < sizes.Count; id++)
            if(kinds[id]==RegionKind.Island && sizes[id]>0 && islandArea[roots[id]]<archipelagoMin) parent[id]=id;
        for (int id = 1; id < sizes.Count; id++) if (members[id] > 1 && islandArea[id]>=archipelagoMin) kinds[id] = RegionKind.Archipelago;
        for (int cell = 0; cell < count; cell++) if (labels[cell] != 0) labels[cell] = Root(labels[cell]);

        return (labels, kinds, plates, west, east);
    }

    internal static RegionKind[] ClassifyClimateSurface(WorldData world, WaterType[] water, short[] heights, bool cacheGeography = false)
    {
        if (cacheGeography && ReferenceEquals(water, world.Water) && ReferenceEquals(heights, world.Elevation))
        {
            var geography = Get(world);
            var byId = geography.Regions.ToDictionary(region => region.Id, region => region.Kind);
            var cachedSurface = new RegionKind[world.Count];
            for (int cell = 0; cell < cachedSurface.Length; cell++)
            {
                var kind = byId.GetValueOrDefault(geography.CellIds[cell], RegionKind.Island);
                cachedSurface[cell] = kind == RegionKind.Archipelago ? RegionKind.Island : kind;
            }
            return cachedSurface;
        }
        var (labels, kinds, _, _, _) = Classify(world, water, heights, groupIslands: false);
        var surface = new RegionKind[world.Count];
        for (int cell = 0; cell < surface.Length; cell++) surface[cell] = kinds[labels[cell]];
        return surface;
    }

    public static GeographicRegions Build(WorldData w, GeographicRegions? previous = null)
    {
        int count = w.Count, width = w.Width;
        var (labels, kinds, plates, west, east) = Classify(w, w.Water, w.Elevation, groupIslands: true);

        var overlap = new Dictionary<long,int>();
        var old = previous?.Regions.ToDictionary(r => r.Id);
        if (previous is not null && previous.CellIds.Length == count)
            for (int cell = 0; cell < count; cell++)
            {
                int id = labels[cell], before = previous.CellIds[cell];
                if (id == 0 || before == 0 || old is null || !old.TryGetValue(before, out var oldRegion) || oldRegion.Kind != kinds[id]) continue;
                long key = ((long)id << 32) | (uint)before; overlap[key] = overlap.GetValueOrDefault(key) + 1;
            }
        var identities = new Dictionary<int,int>(); var used = new HashSet<int>();
        foreach (var pair in overlap.OrderByDescending(p=>p.Value).ThenBy(p=>p.Key))
        {
            int id = (int)(pair.Key >> 32), before = (int)pair.Key;
            if (!identities.ContainsKey(id) && used.Add(before)) identities[id] = before;
        }
        int next = previous?.Regions.Select(r=>r.Id).DefaultIfEmpty(0).Max() ?? 0;
        var totals = new Dictionary<int,(int Cells,long Elevation,long X,long Y,int Anchor)>();
        for (int cell = 0; cell < count; cell++)
        {
            int id = labels[cell]; if (id == 0) continue;
            var sum = totals.GetValueOrDefault(id);
            totals[id] = (sum.Cells+1, sum.Elevation+w.Elevation[cell], sum.X+cell%width, sum.Y+cell/width, sum.Cells==0?cell:sum.Anchor);
        }
        var records = new List<GeographicRegion>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        double hexArea = Math.Sqrt(3)*0.5*w.Manifest.HexDistance*w.Manifest.HexDistance;
        foreach (var entry in totals.OrderBy(p=>p.Key))
        {
            int id = entry.Key; var sum = entry.Value;
            if (!identities.ContainsKey(id)) identities[id] = ++next;
            int stable = identities[id];
            string name = old is not null && old.TryGetValue(stable,out var existing) ? existing.Name : Name(w.Manifest.Seed, stable, kinds[id]);
            if(old is null || !old.ContainsKey(stable))
            {
                if(west>=0 && id==labels[west])name+=" Ocidental";
                else if(east>=0 && id==labels[east])name+=" Oriental";
            }
            if(!names.Add(name)) { name+=$" {stable}";names.Add(name); }
            var kind=kinds[id];
            if(kind!=kinds[id] && (old is null || !old.ContainsKey(stable)))name=Name(w.Manifest.Seed,stable,kind);
            records.Add(new(stable,kind,name,sum.Cells,sum.Anchor,sum.Cells*hexArea,sum.Elevation/(double)sum.Cells,plates[id]));
        }
        // Pick a real member near the centroid, rather than placing a label in
        // the sea at the centroid of a crescent-shaped continent.
        var bestDistance = new Dictionary<int,long>(); var bestCell = new Dictionary<int,int>();
        for (int cell=0;cell<count;cell++)
        {
            int id=labels[cell]; if(id==0)continue; var sum=totals[id];
            long dx=cell%width-sum.X/sum.Cells, dy=cell/width-sum.Y/sum.Cells, distance=dx*dx+dy*dy;
            if (!bestDistance.TryGetValue(id,out long best) || distance<best) { bestDistance[id]=distance; bestCell[id]=cell; }
            labels[cell]=identities[id];
        }
        var originals=identities.ToDictionary(p=>p.Value,p=>p.Key);
        for(int i=0;i<records.Count;i++)
            if (originals.TryGetValue(records[i].Id, out int original) && bestCell.TryGetValue(original, out int anchor))
                records[i]=records[i] with { Anchor=anchor };
        return new() { CellIds=labels, Regions=records.ToArray(), SourceHash=SurfaceHash(w) };
    }

    private static string Name(int seed, int id, RegionKind kind)
    {
        string[] first = { "Ara", "Bel", "Cal", "Dor", "Eri", "Fal", "Gal", "Iri", "Lor", "Mor", "Nar", "Or", "Sar", "Tal", "Val", "Zar" };
        string[] last = { "vel", "dan", "mar", "nor", "lia", "dor", "ven", "ria", "thar", "mir", "las", "ron", "sia", "len", "var", "dal" };
        uint hash=unchecked((uint)seed ^ (uint)id*0x9E3779B9u); hash=(hash^(hash>>16))*0x7FEB352Du; hash^=hash>>15;
        return $"{Category(kind)} {first[hash%16]}{last[(hash>>8)%16]}";
    }
}
