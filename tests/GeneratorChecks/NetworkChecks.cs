using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    internal static void CheckNetworkEditing()
    {
        foreach (bool wrap in new[] { false, true })
        {
            var world = new WorldData(CheckManifest(128, 64, wrap) with { GeneratorVersion = Version });
            var history = new UndoManager(world);
            int A(int x) => 31 * world.Width + x;
            int a = A(31), b = A(32), c = A(33), d = A(34), e = A(35), f = A(36);
            Require(NetworkEditor.Road(world, a, b, 3, null, history.Capture), "Road crossing chunks");
            Require(HexGrid.EdgeValue(world.RoadEdges[a], 0) == 3 && HexGrid.EdgeValue(world.RoadEdges[b], 3) == 3, "Shared road");
            Require(history.Commit() && history.Undo() && world.RoadEdges.All(v => v == 0), "Road undo both chunks");
            Require(history.Redo() && HexGrid.EdgeValue(world.RoadEdges[b], 3) == 3, "Road redo");
            world.LockMask[b] = (ushort)HexLocks.Roads;
            Require(!NetworkEditor.Road(world, a, b, 0, null, history.Capture), "Road neighbor lock");
            world.LockMask[b] = 0;
            Require(!NetworkEditor.Road(world, a, b, 0, new WorldRegion(31, 31, 1, 1), history.Capture), "Road selection border");
            Require(NetworkEditor.Road(world, a, b, 1, null, history.Capture), "Road replace tier");
            history.Commit();
            Require(NetworkEditor.Road(world, a, b, 0, null, history.Capture), "Road erase");
            history.Commit();

            int[] path = { a, b, c, d, e, f };
            world.Elevation[a] = 0; world.Elevation[f] = 8000;
            Require(NetworkEditor.River(world, path, 5, null, history.Capture), "Manual uphill river");
            Require(history.Commit(), "River command");
            string initial = world.SparseEntities.GetRawText();
            var initialEdges = world.RiverEdges.ToArray();
            Require(ReadRivers(world).Single().Tiers.All(t => t == 5), "Exact five-level width");
            Require(history.Undo() && ReadRivers(world).Length == 0 && world.RiverEdges.All(v => v == 0), "River undo caches");
            Require(history.Redo() && world.RiverEdges.SequenceEqual(initialEdges), "River redo caches");
            world.LockMask[d] = (ushort)HexLocks.Rivers;
            Require(!NetworkEditor.River(world, new[] { c, d }, 0, null, history.Capture) &&
                world.SparseEntities.GetRawText() == initial, "River neighbor lock");
            world.LockMask[d] = 0;
            Require(!NetworkEditor.River(world, new[] { c, d }, 0, new WorldRegion(33, 31, 1, 1), history.Capture), "River selection border");
            Require(NetworkEditor.River(world, new[] { c, d }, 0, null, history.Capture), "Cut middle edge");
            Require(history.Commit() && ReadRivers(world).Length == 2, "Split surviving segments");
            foreach (int i in Enumerable.Range(0, world.Count).Except(new[] { c, d }))
                Require(world.RiverEdges[i] == initialEdges[i], "Cut changed unrelated edges");
            Require(ReadRivers(world).All(r => r.Tiers.All(t => t == 5)), "Split preserved widths");
            Require(history.Undo() && world.SparseEntities.GetRawText() == initial, "Split undo entities");
            Require(history.Redo(), "Split redo");
            Require(NetworkEditor.River(world, new[] { c, d }, 2, null, history.Capture), "Replace missing link");
            history.Commit();
            Require(HexGrid.EdgeValue(world.RiverEdges[c], 0) == 1, "Compact width cache");

            int upstream = HexGrid.NeighborIndex(HexGrid.FromOffset(e % world.Width, e / world.Width), 5, world.Width, world.Height, wrap);
            Require(NetworkEditor.River(world, new[] { upstream, e }, 1, null, history.Capture), "Draw tributary");
            history.Commit();
            Require(ReadRivers(world).Single(r => r.Path[0] == upstream).ParentId.HasValue, "Tributary linked");
            Require(NetworkEditor.River(world, new[] { d, e, f }, 0, null, history.Capture), "Erase parent");
            history.Commit();
            Require(ReadRivers(world).Single(r => r.Path[0] == upstream).ParentId is null, "Detach missing parent");
            string sparse = world.SparseEntities.GetRawText();
            bool rejectedCycle = false;
            try { NetworkEditor.River(world, new[] { a, b, a }, 1, null, history.Capture); }
            catch (ArgumentException) { rejectedCycle = true; }
            Require(rejectedCycle && world.SparseEntities.GetRawText() == sparse, "Reject manual cycle atomically");
            if (wrap)
            {
                int west = A(0), east = A(127);
                Require(NetworkEditor.Road(world, west, east, 2, null, history.Capture), "Wrap road");
                Require(HexGrid.EdgeValue(world.RoadEdges[west], 3) == 2 && HexGrid.EdgeValue(world.RoadEdges[east], 0) == 2, "Shared wrap road");
                Require(NetworkEditor.River(world, new[] { west, east }, 3, null, history.Capture), "Wrap river");
                history.Commit();
            }
            string file = Path.Combine(Path.GetTempPath(), $"m5-network-{Guid.NewGuid():N}.world");
            try
            {
                WorldFile.Save(file, world, null);
                var loaded = WorldFile.Load(file);
                Require(world.RoadEdges.SequenceEqual(loaded.RoadEdges) && world.RiverEdges.SequenceEqual(loaded.RiverEdges) &&
                    world.SparseEntities.GetRawText() == loaded.SparseEntities.GetRawText(), "Manual networks round-trip");
            }
            finally { File.Delete(file); }
        }
        var generated = Create(CheckManifest(128, 64, false) with { GeneratorVersion = Version });
        RiverRecord source = ReadRivers(generated).First(r => r.Path.Length >= 3);
        int edgeIndex = source.Tiers.Length / 2;
        int from = source.Path[edgeIndex], to = source.Path[edgeIndex + 1];
        var generatedHistory = new UndoManager(generated);
        var beforeEdges = generated.RiverEdges.ToArray();
        string beforeEntities = generated.SparseEntities.GetRawText();
        Require(NetworkEditor.River(generated, new[] { from, to }, 1, null, generatedHistory.Capture), "Edit generated river");
        generatedHistory.Commit();
        for (int i = 0; i < generated.Count; i++)
            if (i != from && i != to) Require(generated.RiverEdges[i] == beforeEdges[i], "Generated network changed exterior");
        Require(generatedHistory.Undo() && generated.SparseEntities.GetRawText() == beforeEntities &&
            generated.RiverEdges.SequenceEqual(beforeEdges), "Restore generated network");
        Require(generatedHistory.Redo(), "Redo generated network");
        Console.WriteLine("M5 NETWORK CHECKS PASSED: shared edges, chunks, tiers, locks, selection, split, tributaries, uphill, wrap, undo/redo, save/load.");
    }
}
