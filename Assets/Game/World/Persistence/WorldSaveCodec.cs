using System;
using System.Collections.Generic;
using System.IO;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Entities;
using MiniCivilization.World.Generation.Patterns;
using MiniCivilization.World.Runtime;
using Vector3 = UnityEngine.Vector3;

namespace MiniCivilization.World.Persistence
{
    internal sealed class WorldSaveRuntimeState
    {
        public WorldSaveRuntimeState(
            ulong nextEntityId,
            IReadOnlyList<CellCoordinate> waterFrontier,
            IReadOnlyList<EntityPersistentState> entities)
        {
            NextEntityId = nextEntityId;
            WaterFrontier = waterFrontier ?? Array.Empty<CellCoordinate>();
            Entities = entities ?? Array.Empty<EntityPersistentState>();
        }

        public ulong NextEntityId { get; }
        public IReadOnlyList<CellCoordinate> WaterFrontier { get; }
        public IReadOnlyList<EntityPersistentState> Entities { get; }
    }

    internal sealed class WorldChunkSnapshot
    {
        public WorldChunkSnapshot(
            ChunkCoordinate coordinate,
            IReadOnlyList<WorldChunkSectionSnapshot> sections)
        {
            Coordinate = coordinate;
            Sections = sections ?? Array.Empty<WorldChunkSectionSnapshot>();
        }

        public ChunkCoordinate Coordinate { get; }
        public IReadOnlyList<WorldChunkSectionSnapshot> Sections { get; }
    }

    internal sealed class WorldChunkSectionSnapshot
    {
        public WorldChunkSectionSnapshot(
            int sectionY,
            IReadOnlyList<WorldChunkCellSnapshot> cells)
        {
            SectionY = sectionY;
            Cells = cells ?? Array.Empty<WorldChunkCellSnapshot>();
        }

        public int SectionY { get; }
        public IReadOnlyList<WorldChunkCellSnapshot> Cells { get; }
    }

    internal readonly struct WorldChunkCellSnapshot
    {
        public WorldChunkCellSnapshot(int localIndex, CellData cell)
        {
            LocalIndex = localIndex;
            Cell = cell;
        }

        public int LocalIndex { get; }
        public CellData Cell { get; }
    }

    internal static class WorldSaveCodec
    {
        private const uint SaveDataMagic = 0x57534433;
        private const uint ChunkMagic = 0x57434438;
        private const int MaximumCollectionCount = 16_777_216;
        private const int MaximumEntityProgressPayloadLength = 65_536;
        private const int MaximumStringByteLength = 4_096;

        public static WorldChunkSnapshot CaptureChunk(
            WorldData world,
            ChunkCoordinate coordinate)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (!world.TryGetChunk(coordinate, out var chunk))
            {
                throw new InvalidOperationException(
                    $"Chunk {coordinate} is not loaded.");
            }

            var sections = new List<WorldChunkSectionSnapshot>();
            for (var sectionY = 0;
                 sectionY < world.ChunkSectionCountY;
                 sectionY++)
            {
                if (!chunk.TryGetSection(sectionY, out var section))
                {
                    continue;
                }

                var cells = new List<WorldChunkCellSnapshot>();
                var source = section.AsSpan();
                for (var localIndex = 0; localIndex < source.Length; localIndex++)
                {
                    if (!source[localIndex].Equals(default))
                    {
                        cells.Add(new WorldChunkCellSnapshot(
                            localIndex,
                            source[localIndex]));
                    }
                }

                if (cells.Count != 0)
                {
                    sections.Add(new WorldChunkSectionSnapshot(sectionY, cells));
                }
            }

            return new WorldChunkSnapshot(coordinate, sections);
        }

        public static void ApplyChunk(
            WorldData world,
            WorldChunkSnapshot snapshot)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (world.IsChunkLoaded(snapshot.Coordinate))
            {
                throw new InvalidOperationException(
                    $"Chunk {snapshot.Coordinate} is already loaded.");
            }

            world.EnsureChunkLoaded(snapshot.Coordinate);
            var sectionCellCount = checked(
                world.ChunkSizeX * world.ChunkSectionSizeY * world.ChunkSizeZ);
            var seenSections = new HashSet<int>();
            for (var sectionIndex = 0;
                 sectionIndex < snapshot.Sections.Count;
                 sectionIndex++)
            {
                var section = snapshot.Sections[sectionIndex];
                if ((uint)section.SectionY >= world.ChunkSectionCountY
                    || !seenSections.Add(section.SectionY))
                {
                    throw new InvalidDataException(
                        "Saved Chunk has an invalid or duplicated Section.");
                }

                var seenCells = new HashSet<int>();
                for (var cellIndex = 0;
                     cellIndex < section.Cells.Count;
                     cellIndex++)
                {
                    var savedCell = section.Cells[cellIndex];
                    if ((uint)savedCell.LocalIndex >= sectionCellCount
                        || !seenCells.Add(savedCell.LocalIndex)
                        || savedCell.Cell.Equals(default))
                    {
                        throw new InvalidDataException(
                            "Saved Chunk has an invalid or duplicated Cell.");
                    }

                    var localY = savedCell.LocalIndex
                        / (world.ChunkSizeX * world.ChunkSizeZ);
                    var remaining = savedCell.LocalIndex
                        % (world.ChunkSizeX * world.ChunkSizeZ);
                    var localZ = remaining / world.ChunkSizeX;
                    var localX = remaining % world.ChunkSizeX;
                    world.SetCellRaw(
                        checked(snapshot.Coordinate.X * world.ChunkSizeX + localX),
                        checked(section.SectionY * world.ChunkSectionSizeY + localY),
                        checked(snapshot.Coordinate.Z * world.ChunkSizeZ + localZ),
                        savedCell.Cell);
                }
            }
        }

        public static void WriteSaveData(
            Stream stream,
            WorldSaveData saveData)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            if (saveData == null)
            {
                throw new ArgumentNullException(nameof(saveData));
            }

            using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
            writer.Write(SaveDataMagic);
            writer.Write(saveData.WorldId.ToByteArray());
            WriteString(writer, saveData.SaveName);
            WriteGenerationConfiguration(
                writer,
                saveData.GenerationConfiguration);
            WriteRuntimeState(writer, saveData.RuntimeState);
        }

        public static WorldSaveData ReadSaveData(Stream stream)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
            VerifyHeader(reader, SaveDataMagic);
            var worldId = new Guid(ReadExactBytes(reader, 16));
            var saveName = ReadString(reader);
            var generationConfiguration = ReadGenerationConfiguration(reader);
            var runtimeState = ReadRuntimeState(reader);
            return new WorldSaveData(
                worldId,
                saveName,
                generationConfiguration,
                runtimeState);
        }

        public static void ReadSaveIdentity(
            Stream stream,
            out Guid worldId,
            out string saveName)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
            VerifyHeader(reader, SaveDataMagic);
            worldId = new Guid(ReadExactBytes(reader, 16));
            saveName = ReadString(reader);
            if (worldId == Guid.Empty || string.IsNullOrWhiteSpace(saveName))
            {
                throw new InvalidDataException(
                    "World save has an invalid identity.");
            }
        }

        public static void WriteChunk(
            Stream stream,
            WorldChunkSnapshot snapshot)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
            writer.Write(ChunkMagic);
            WriteChunkCoordinate(writer, snapshot.Coordinate);
            WriteCollectionCount(writer, snapshot.Sections.Count);
            for (var sectionIndex = 0;
                 sectionIndex < snapshot.Sections.Count;
                 sectionIndex++)
            {
                var section = snapshot.Sections[sectionIndex];
                writer.Write(section.SectionY);
                WriteCollectionCount(writer, section.Cells.Count);
                for (var cellIndex = 0;
                     cellIndex < section.Cells.Count;
                     cellIndex++)
                {
                    var cell = section.Cells[cellIndex];
                    writer.Write(cell.LocalIndex);
                    WriteCellData(writer, cell.Cell);
                }
            }
        }

        public static WorldChunkSnapshot ReadChunk(Stream stream)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
            VerifyHeader(reader, ChunkMagic);
            var coordinate = ReadChunkCoordinate(reader);
            var sections = new WorldChunkSectionSnapshot[ReadCollectionCount(reader)];
            for (var sectionIndex = 0;
                 sectionIndex < sections.Length;
                 sectionIndex++)
            {
                var sectionY = reader.ReadInt32();
                var cells = new WorldChunkCellSnapshot[ReadCollectionCount(reader)];
                for (var cellIndex = 0; cellIndex < cells.Length; cellIndex++)
                {
                    cells[cellIndex] = new WorldChunkCellSnapshot(
                        reader.ReadInt32(),
                        ReadCellData(reader));
                }

                sections[sectionIndex] = new WorldChunkSectionSnapshot(
                    sectionY,
                    cells);
            }

            return new WorldChunkSnapshot(coordinate, sections);
        }

        private static void WriteRuntimeState(
            BinaryWriter writer,
            WorldSaveRuntimeState runtimeState)
        {
            writer.Write(runtimeState.NextEntityId);
            WriteCollectionCount(writer, runtimeState.WaterFrontier.Count);
            for (var index = 0;
                 index < runtimeState.WaterFrontier.Count;
                 index++)
            {
                WriteCellCoordinate(writer, runtimeState.WaterFrontier[index]);
            }

            WriteCollectionCount(writer, runtimeState.Entities.Count);
            for (var index = 0; index < runtimeState.Entities.Count; index++)
            {
                WriteEntity(writer, runtimeState.Entities[index]);
            }
        }

        private static WorldSaveRuntimeState ReadRuntimeState(BinaryReader reader)
        {
            var nextEntityId = reader.ReadUInt64();
            var frontier = new CellCoordinate[ReadCollectionCount(reader)];
            for (var index = 0; index < frontier.Length; index++)
            {
                frontier[index] = ReadCellCoordinate(reader);
            }

            var entities = new EntityPersistentState[ReadCollectionCount(reader)];
            var ids = new HashSet<EntityId>();
            for (var index = 0; index < entities.Length; index++)
            {
                entities[index] = ReadEntity(reader);
                if (!ids.Add(entities[index].Id))
                {
                    throw new InvalidDataException(
                        "Saved WorldSaveData contains duplicated Entity IDs.");
                }
            }

            return new WorldSaveRuntimeState(nextEntityId, frontier, entities);
        }

        private static void WriteGenerationConfiguration(
            BinaryWriter writer,
            WorldGenerationConfiguration configuration)
        {
            WriteWorldSettings(writer, configuration.World);
            WriteTerrainSettings(writer, configuration.Terrain);
            WriteHydrologySettings(writer, configuration.Hydrology);
            writer.Write(configuration.UpdateRangeChunks);
            writer.Write(configuration.RenderRangeChunks);
            writer.Write(configuration.PrepareRangeChunks);
            writer.Write(configuration.ChunkMaterializationsPerFrame);
            writer.Write(configuration.MaximumConcurrentTileBuilds);
        }

        private static WorldGenerationConfiguration ReadGenerationConfiguration(
            BinaryReader reader)
        {
            var world = ReadWorldSettings(reader);
            var terrain = ReadTerrainSettings(reader);
            var hydrology = ReadHydrologySettings(reader, world);
            var patternTiles = new PatternTileGridSettingsData(
                world,
                terrain.PatternTileChunkSpan);
            return new WorldGenerationConfiguration(
                world,
                terrain,
                hydrology,
                patternTiles,
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32());
        }

        private static void WriteWorldSettings(
            BinaryWriter writer,
            WorldSettingsData world)
        {
            writer.Write(world.Seed);
            writer.Write((byte)world.WorldType);
            writer.Write(world.CellSize);
            writer.Write(world.ChunkCellCountXZ);
            writer.Write(world.ChunkSectionCellCountY);
            writer.Write(world.InitialChunkCountXZ);
            writer.Write(world.ChunkSectionCountY);
            writer.Write(world.RenderChunksPerPatch);
            writer.Write(world.RoadMaxHeightSteps);
            writer.Write(world.PondMaximumArea);
            writer.Write(world.WaterFlowRules.SpreadAmountLoss);
            writer.Write(world.WaterFlowRules.MinimumSpreadAmount);
            writer.Write(world.WaterFlowRules.DissipationAmountLoss);
        }

        private static WorldSettingsData ReadWorldSettings(BinaryReader reader)
        {
            var seed = reader.ReadInt32();
            var worldType = (WorldType)reader.ReadByte();
            var cellSize = ReadFiniteSingle(reader);
            var chunkCellCountXZ = reader.ReadInt32();
            var chunkSectionCellCountY = reader.ReadInt32();
            var initialChunkCountXZ = reader.ReadInt32();
            var chunkSectionCountY = reader.ReadInt32();
            var renderChunksPerPatch = reader.ReadInt32();
            var roadMaxHeightSteps = reader.ReadInt32();
            var pondMaximumArea = reader.ReadInt32();
            var spreadAmountLoss = reader.ReadByte();
            var minimumSpreadAmount = reader.ReadByte();
            var dissipationAmountLoss = reader.ReadByte();
            if (spreadAmountLoss == 0
                || minimumSpreadAmount == 0
                || dissipationAmountLoss == 0)
            {
                throw new InvalidDataException(
                    "World save contains invalid Water Flow rules.");
            }

            return new WorldSettingsData(
                seed,
                worldType,
                cellSize,
                chunkCellCountXZ,
                chunkSectionCellCountY,
                initialChunkCountXZ,
                chunkSectionCountY,
                renderChunksPerPatch,
                roadMaxHeightSteps,
                pondMaximumArea,
                new WaterFlowRules(
                    spreadAmountLoss,
                    minimumSpreadAmount,
                    dissipationAmountLoss));
        }

        private static void WriteTerrainSettings(
            BinaryWriter writer,
            TerrainPatternSettingsData terrain)
        {
            writer.Write(terrain.WorldSeed);
            writer.Write(terrain.PatternTileChunkSpan);
            writer.Write(terrain.TerrainBaseHeight);
            WriteNoiseRouter(writer, terrain.NoiseRouter);
            WriteRegion(writer, terrain.Region);
            WriteBaseSurface(writer, terrain.BaseSurface);
            WriteSurfaceForm(writer, terrain.Smooth);
            WriteSurfaceForm(writer, terrain.Rugged);
            WriteMountainForm(writer, terrain.Mountain);
            WriteCanyonForm(writer, terrain.Canyon);
        }

        private static TerrainPatternSettingsData ReadTerrainSettings(
            BinaryReader reader) => new(
            reader.ReadInt32(),
            reader.ReadInt32(),
            reader.ReadInt32(),
            ReadNoiseRouter(reader),
            ReadRegion(reader),
            ReadBaseSurface(reader),
            ReadSurfaceForm(reader),
            ReadSurfaceForm(reader),
            ReadMountainForm(reader),
            ReadCanyonForm(reader));

        private static void WriteHydrologySettings(
            BinaryWriter writer,
            HydrologyFeatureSettingsData hydrology)
        {
            WriteSea(writer, hydrology.Sea);
            WriteBasin(writer, hydrology.Basins);
            WriteRiver(writer, hydrology.River);
            WriteNaturalEndpoint(writer, hydrology.NaturalEndpoint);
        }

        private static HydrologyFeatureSettingsData ReadHydrologySettings(
            BinaryReader reader,
            WorldSettingsData world) => new(
            world,
            ReadSea(reader),
            ReadBasin(reader),
            ReadRiver(reader),
            ReadNaturalEndpoint(reader));

        private static void WriteNoise(
            BinaryWriter writer,
            TerrainNoiseFieldData value)
        {
            writer.Write((byte)value.Mode);
            writer.Write(value.Scale);
            writer.Write(value.Layers);
            writer.Write(value.FrequencySpacing);
            writer.Write(value.Persistence);
            writer.Write(value.OctaveSeedStride);
        }

        private static TerrainNoiseFieldData ReadNoise(BinaryReader reader) => new(
            (PatternNoiseMode)reader.ReadByte(),
            ReadFiniteSingle(reader),
            reader.ReadInt32(),
            ReadFiniteSingle(reader),
            ReadFiniteSingle(reader),
            reader.ReadInt32());

        private static void WriteCurve(
            BinaryWriter writer,
            TerrainCurveData value)
        {
            writer.Write(value.AtZero);
            writer.Write(value.AtQuarter);
            writer.Write(value.AtHalf);
            writer.Write(value.AtThreeQuarters);
            writer.Write(value.AtOne);
        }

        private static TerrainCurveData ReadCurve(BinaryReader reader) => new(
            ReadFiniteSingle(reader),
            ReadFiniteSingle(reader),
            ReadFiniteSingle(reader),
            ReadFiniteSingle(reader),
            ReadFiniteSingle(reader));

        private static void WriteRange(
            BinaryWriter writer,
            TerrainRangeData value)
        {
            writer.Write(value.Minimum);
            writer.Write(value.Maximum);
        }

        private static TerrainRangeData ReadRange(BinaryReader reader) => new(
            ReadFiniteSingle(reader),
            ReadFiniteSingle(reader));

        private static void WriteDomainWarp(
            BinaryWriter writer,
            TerrainDomainWarpData value)
        {
            WriteNoise(writer, value.Field);
            writer.Write(value.StrengthCells);
        }

        private static TerrainDomainWarpData ReadDomainWarp(BinaryReader reader) =>
            new(ReadNoise(reader), ReadFiniteSingle(reader));

        private static void WriteNoiseRouter(
            BinaryWriter writer,
            TerrainNoiseRouterData value)
        {
            WriteNoise(writer, value.Continentalness);
            WriteNoise(writer, value.Erosion);
        }

        private static TerrainNoiseRouterData ReadNoiseRouter(BinaryReader reader) =>
            new(ReadNoise(reader), ReadNoise(reader));

        private static void WriteRegion(
            BinaryWriter writer,
            TerrainRegionData value)
        {
            writer.Write(value.SizeCells);
            writer.Write(value.CenterJitter);
            WriteNoise(writer, value.WarpField);
            writer.Write(value.WarpStrengthCells);
            writer.Write(value.BoundaryBlendCells);
            writer.Write(value.InteriorReachRatio);
            writer.Write(value.SmoothShare);
            writer.Write(value.RuggedShare);
            writer.Write(value.MountainShare);
            writer.Write(value.CanyonShare);
            writer.Write(value.SeaShare);
        }

        private static TerrainRegionData ReadRegion(BinaryReader reader) => new(
            reader.ReadInt32(),
            ReadFiniteSingle(reader),
            ReadNoise(reader),
            ReadFiniteSingle(reader),
            ReadFiniteSingle(reader),
            ReadFiniteSingle(reader),
            ReadFiniteSingle(reader),
            ReadFiniteSingle(reader),
            ReadFiniteSingle(reader),
            ReadFiniteSingle(reader),
            ReadFiniteSingle(reader));

        private static void WriteBaseSurface(
            BinaryWriter writer,
            TerrainBaseSurfaceData value)
        {
            WriteCurve(writer, value.SurfaceByContinentalness);
            WriteCurve(writer, value.SurfaceByErosion);
        }

        private static TerrainBaseSurfaceData ReadBaseSurface(BinaryReader reader) =>
            new(ReadCurve(reader), ReadCurve(reader));

        private static void WriteSurfaceForm(
            BinaryWriter writer,
            TerrainSurfaceFormData value)
        {
            WriteDomainWarp(writer, value.DomainWarp);
            WriteNoise(writer, value.ShapeField);
            WriteCurve(writer, value.ShapeResponse);
            WriteRange(writer, value.ShapeAmplitude);
            WriteNoise(writer, value.DetailField);
            WriteRange(writer, value.DetailAmplitude);
        }

        private static TerrainSurfaceFormData ReadSurfaceForm(BinaryReader reader) =>
            new(
                ReadDomainWarp(reader),
                ReadNoise(reader),
                ReadCurve(reader),
                ReadRange(reader),
                ReadNoise(reader),
                ReadRange(reader));

        private static void WriteMountainForm(
            BinaryWriter writer,
            TerrainMountainFormData value)
        {
            WriteDomainWarp(writer, value.DomainWarp);
            WriteNoise(writer, value.MassField);
            WriteCurve(writer, value.MassResponse);
            WriteRange(writer, value.Height);
            WriteNoise(writer, value.RidgeField);
            WriteCurve(writer, value.RidgeResponse);
            WriteRange(writer, value.RidgeStrength);
            WriteNoise(writer, value.DetailField);
            WriteRange(writer, value.DetailAmplitude);
        }

        private static TerrainMountainFormData ReadMountainForm(BinaryReader reader) =>
            new(
                ReadDomainWarp(reader),
                ReadNoise(reader),
                ReadCurve(reader),
                ReadRange(reader),
                ReadNoise(reader),
                ReadCurve(reader),
                ReadRange(reader),
                ReadNoise(reader),
                ReadRange(reader));

        private static void WriteCanyonForm(
            BinaryWriter writer,
            TerrainCanyonFormData value)
        {
            WriteDomainWarp(writer, value.DomainWarp);
            WriteNoise(writer, value.BasinField);
            WriteCurve(writer, value.BasinResponse);
            WriteRange(writer, value.BasinDepthRatio);
            WriteNoise(writer, value.ValleyField);
            WriteCurve(writer, value.ValleyResponse);
            WriteRange(writer, value.ValleyDepthRatio);
            WriteRange(writer, value.Depth);
            WriteNoise(writer, value.DetailField);
            WriteRange(writer, value.DetailAmplitude);
        }

        private static TerrainCanyonFormData ReadCanyonForm(BinaryReader reader) =>
            new(
                ReadDomainWarp(reader),
                ReadNoise(reader),
                ReadCurve(reader),
                ReadRange(reader),
                ReadNoise(reader),
                ReadCurve(reader),
                ReadRange(reader),
                ReadRange(reader),
                ReadNoise(reader),
                ReadRange(reader));

        private static void WriteBasin(
            BinaryWriter writer,
            BasinFeatureSettingsData value)
        {
            writer.Write(value.CandidateLatticeSpacingCells);
            writer.Write(value.Occurrence);
            WriteRange(writer, value.Area);
            writer.Write(value.PondMaximumAreaCells);
            WriteRange(writer, value.MaximumDepth);
            WriteNoise(writer, value.PotentialField);
            WriteCurve(writer, value.PotentialResponse);
            writer.Write(value.ShoreTransitionCells);
            WriteCurve(writer, value.ShoreTransition);
            WriteCurve(writer, value.DepthByInterior);
            WriteNoise(writer, value.BedField);
            WriteRange(writer, value.BedAmplitude);
            writer.Write(value.MaximumReachCells);
            writer.Write(value.PotentialCost);
            writer.Write(value.TerrainDeformationCost);
            writer.Write(value.SlopeCost);
            writer.Write(value.CutCost);
            writer.Write(value.FillCost);
            writer.Write(value.RimCost);
        }

        private static BasinFeatureSettingsData ReadBasin(BinaryReader reader) =>
            new(
                reader.ReadInt32(),
                ReadFiniteSingle(reader),
                ReadRange(reader),
                reader.ReadInt32(),
                ReadRange(reader),
                ReadNoise(reader),
                ReadCurve(reader),
                reader.ReadInt32(),
                ReadCurve(reader),
                ReadCurve(reader),
                ReadNoise(reader),
                ReadRange(reader),
                reader.ReadInt32(),
                ReadFiniteSingle(reader),
                ReadFiniteSingle(reader),
                ReadFiniteSingle(reader),
                ReadFiniteSingle(reader),
                ReadFiniteSingle(reader),
                ReadFiniteSingle(reader));

        private static void WriteSea(
            BinaryWriter writer,
            SeaFeatureSettingsData value)
        {
            WriteDomainWarp(writer, value.DomainWarp);
            WriteNoise(writer, value.BasinField);
            writer.Write(value.BasinVariation);
            WriteCurve(writer, value.DepthByInterior);
            WriteRange(writer, value.MaximumDepth);
            WriteNoise(writer, value.SeabedField);
            WriteRange(writer, value.SeabedAmplitude);
            writer.Write(value.SurfaceHeight);
        }

        private static SeaFeatureSettingsData ReadSea(BinaryReader reader) => new(
            ReadDomainWarp(reader),
            ReadNoise(reader),
            ReadFiniteSingle(reader),
            ReadCurve(reader),
            ReadRange(reader),
            ReadNoise(reader),
            ReadRange(reader),
            reader.ReadInt32());

        private static void WriteRiver(
            BinaryWriter writer,
            RiverFeatureSettingsData value)
        {
            writer.Write(value.CandidateLatticeSpacingCells);
            writer.Write(value.AnchorJitterCells);
            writer.Write(value.Occurrence);
            WriteRange(writer, value.Length);
            writer.Write(value.StrokeSampleSpacingCells);
            WriteRange(writer, value.NodeTurnDegrees);
            writer.Write(value.TerrainCorrectionRadiusCells);
            writer.Write(value.TerrainCorrectionSmoothingPasses);
            writer.Write(value.TerrainSlopeCost);
            writer.Write(value.BaseStrokeDeviationCost);
            writer.Write(value.ElevationChangeCost);
            writer.Write(value.CorridorDeformationCost);
            writer.Write(value.CurvatureCost);
            WriteNoise(writer, value.WidthField);
            WriteRange(writer, value.Width);
            WriteCurve(writer, value.CrossSection);
            WriteRange(writer, value.Depth);
            WriteRange(writer, value.WaterInset);
            writer.Write(value.BankMarginCells);
            writer.Write(value.DropTransitionCells);
            WriteCurve(writer, value.DropTransition);
            WriteNoise(writer, value.RiverbedField);
            WriteRange(writer, value.RiverbedAmplitude);
        }

        private static RiverFeatureSettingsData ReadRiver(BinaryReader reader) =>
            new(
                reader.ReadInt32(),
                reader.ReadInt32(),
                ReadFiniteSingle(reader),
                ReadRange(reader),
                reader.ReadInt32(),
                ReadRange(reader),
                reader.ReadInt32(),
                reader.ReadInt32(),
                ReadFiniteSingle(reader),
                ReadFiniteSingle(reader),
                ReadFiniteSingle(reader),
                ReadFiniteSingle(reader),
                ReadFiniteSingle(reader),
                ReadNoise(reader),
                ReadRange(reader),
                ReadCurve(reader),
                ReadRange(reader),
                ReadRange(reader),
                ReadFiniteSingle(reader),
                reader.ReadInt32(),
                ReadCurve(reader),
                ReadNoise(reader),
                ReadRange(reader));

        private static void WriteNaturalEndpoint(
            BinaryWriter writer,
            NaturalEndpointSettingsData value)
        {
            writer.Write(value.EndpointTransitionCells);
            WriteCurve(writer, value.EndpointTransitionRate);
        }

        private static NaturalEndpointSettingsData ReadNaturalEndpoint(
            BinaryReader reader) => new(
            reader.ReadInt32(),
            ReadCurve(reader));

        private static void WriteEntity(
            BinaryWriter writer,
            EntityPersistentState state)
        {
            writer.Write(state.Id.Value);
            writer.Write((byte)state.TypeKey.Category);
            writer.Write(state.TypeKey.Value);
            WriteCellCoordinate(writer, state.AnchorCell);
            writer.Write((byte)state.Direction);
            WriteEntityProgressPayload(writer, state.ProgressPayload);
            var flags = (byte)0;
            if (state.HasBuildingWayLocation) flags |= 1;
            if (state.ActiveWayMove != null) flags |= 2;
            writer.Write(flags);

            if (state.HasBuildingWayLocation)
            {
                WriteBuildingWayLocation(writer, state.BuildingWayLocation);
            }

            if (state.ActiveWayMove != null)
            {
                var plan = state.ActiveWayMove;
                WriteCollectionCount(writer, plan.GraphPositions.Length);
                for (var index = 0; index < plan.GraphPositions.Length; index++)
                {
                    writer.Write(plan.GraphPositions[index].x);
                    writer.Write(plan.GraphPositions[index].y);
                    writer.Write(plan.GraphPositions[index].z);
                }

                writer.Write(plan.StartsAtCellCenter);
                writer.Write(plan.EndsAtCellCenter);
                writer.Write(plan.EndsInsideBuilding);
                WriteBuildingWayLocation(writer, plan.EndLocation);
            }
        }

        private static EntityPersistentState ReadEntity(BinaryReader reader)
        {
            var id = new EntityId(reader.ReadUInt64());
            var typeKey = new EntityTypeKey(
                (EntityCategory)reader.ReadByte(),
                reader.ReadUInt16());
            var anchor = ReadCellCoordinate(reader);
            var direction = (EntityDirection)reader.ReadByte();
            if (!id.IsValid || !typeKey.IsValid
                || !Enum.IsDefined(typeof(EntityDirection), direction))
            {
                throw new InvalidDataException(
                    "Saved Entity has an invalid identity.");
            }

            var progressPayload = ReadEntityProgressPayload(reader);
            var flags = reader.ReadByte();
            if ((flags & ~3) != 0)
            {
                throw new InvalidDataException(
                    "Saved Entity has unknown state flags.");
            }

            var hasBuildingWayLocation = (flags & 1) != 0;
            var buildingWayLocation = hasBuildingWayLocation
                ? ReadBuildingWayLocation(reader)
                : default;
            WayMovementPlanPersistentState activeWayMove = null;
            if ((flags & 2) != 0)
            {
                var positions = new Vector3[ReadCollectionCount(reader)];
                for (var index = 0; index < positions.Length; index++)
                {
                    positions[index] = new Vector3(
                        ReadFiniteSingle(reader),
                        ReadFiniteSingle(reader),
                        ReadFiniteSingle(reader));
                }

                activeWayMove = new WayMovementPlanPersistentState(
                    positions,
                    reader.ReadBoolean(),
                    reader.ReadBoolean(),
                    reader.ReadBoolean(),
                    ReadBuildingWayLocation(reader));
            }

            return new EntityPersistentState(
                id,
                typeKey,
                anchor,
                direction,
                progressPayload,
                hasBuildingWayLocation,
                buildingWayLocation,
                activeWayMove);
        }

        private static void WriteEntityProgressPayload(
            BinaryWriter writer,
            byte[] payload)
        {
            payload ??= Array.Empty<byte>();
            if (payload.Length > MaximumEntityProgressPayloadLength)
            {
                throw new InvalidOperationException(
                    "Entity progress data exceeds the format limit.");
            }

            writer.Write(payload.Length);
            writer.Write(payload);
        }

        private static byte[] ReadEntityProgressPayload(BinaryReader reader)
        {
            var length = reader.ReadInt32();
            if (length < 0 || length > MaximumEntityProgressPayloadLength)
            {
                throw new InvalidDataException(
                    "Entity progress data exceeds the format limit.");
            }

            var payload = reader.ReadBytes(length);
            if (payload.Length != length)
            {
                throw new InvalidDataException(
                    "Entity progress data is truncated.");
            }

            return payload;
        }

        private static void WriteCellData(BinaryWriter writer, CellData cell)
        {
            writer.Write(cell.Biome.Value);
            writer.Write((ushort)cell.Terrain.Material);
            writer.Write((ushort)cell.Terrain.Surface);
            writer.Write((ushort)cell.Terrain.Geology);
            writer.Write(cell.Terrain.ResourceId);
            writer.Write(cell.Terrain.SolidHeight);
            writer.Write(cell.Water.Amount);
            writer.Write((byte)cell.Water.Role);
            writer.Write((byte)cell.Water.Type);
            writer.Write((byte)cell.Water.Flow);
            writer.Write((ushort)cell.Road.Type);
            writer.Write(cell.Road.CrossesCenter);
        }

        private static CellData ReadCellData(BinaryReader reader)
        {
            var cell = new CellData
            {
                Biome = CellBiome.FromValue(reader.ReadUInt16()),
                Terrain = new TerrainData
                {
                    Material = (MaterialType)reader.ReadUInt16(),
                    Surface = (SurfaceType)reader.ReadUInt16(),
                    Geology = (MaterialType)reader.ReadUInt16(),
                    ResourceId = reader.ReadUInt16(),
                    SolidHeight = reader.ReadByte()
                },
                Water = new WaterData
                {
                    Amount = reader.ReadByte(),
                    Role = (WaterRole)reader.ReadByte(),
                    Type = (WaterType)reader.ReadByte(),
                    Flow = (FlowDirection)reader.ReadByte()
                },
                Road = new RoadData
                {
                    Type = (RoadType)reader.ReadUInt16(),
                    CrossesCenter = reader.ReadBoolean()
                }
            };
            if (!Enum.IsDefined(typeof(MaterialType), cell.Terrain.Material)
                || !Enum.IsDefined(typeof(SurfaceType), cell.Terrain.Surface)
                || !Enum.IsDefined(typeof(MaterialType), cell.Terrain.Geology)
                || !Enum.IsDefined(typeof(WaterRole), cell.Water.Role)
                || !Enum.IsDefined(typeof(WaterType), cell.Water.Type)
                || !Enum.IsDefined(typeof(RoadType), cell.Road.Type))
            {
                throw new InvalidDataException("Saved Cell contains an invalid enum value.");
            }

            var normalized = cell;
            normalized.Normalize();
            if (!normalized.Equals(cell))
            {
                throw new InvalidDataException("Saved Cell is not normalized.");
            }

            return cell;
        }

        private static void WriteChunkCoordinate(
            BinaryWriter writer,
            ChunkCoordinate coordinate)
        {
            writer.Write(coordinate.X);
            writer.Write(coordinate.Z);
        }

        private static ChunkCoordinate ReadChunkCoordinate(BinaryReader reader) =>
            new(reader.ReadInt32(), reader.ReadInt32());

        private static void WriteCellCoordinate(
            BinaryWriter writer,
            CellCoordinate coordinate)
        {
            writer.Write(coordinate.X);
            writer.Write(coordinate.Y);
            writer.Write(coordinate.Z);
        }

        private static CellCoordinate ReadCellCoordinate(BinaryReader reader) =>
            new(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());

        private static void WriteBuildingWayLocation(
            BinaryWriter writer,
            BuildingWayLocation location)
        {
            writer.Write(location.BuildingId.Value);
            writer.Write(location.LocalPointIndex);
        }

        private static BuildingWayLocation ReadBuildingWayLocation(
            BinaryReader reader) => new(
            new EntityId(reader.ReadUInt64()),
            reader.ReadInt32());

        private static void WriteString(BinaryWriter writer, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    "World save strings cannot be empty.");
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(value);
            if (bytes.Length > MaximumStringByteLength)
            {
                throw new InvalidOperationException(
                    "World save string exceeds the format limit.");
            }

            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        private static string ReadString(BinaryReader reader)
        {
            var length = reader.ReadInt32();
            if (length <= 0 || length > MaximumStringByteLength)
            {
                throw new InvalidDataException(
                    "World save contains an invalid string length.");
            }

            var value = System.Text.Encoding.UTF8.GetString(
                ReadExactBytes(reader, length));
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidDataException(
                    "World save contains an empty string.");
            }

            return value;
        }

        private static byte[] ReadExactBytes(BinaryReader reader, int length)
        {
            var bytes = reader.ReadBytes(length);
            if (bytes.Length != length)
            {
                throw new InvalidDataException("World save is truncated.");
            }

            return bytes;
        }

        private static void VerifyHeader(BinaryReader reader, uint magic)
        {
            if (reader.ReadUInt32() != magic)
            {
                throw new InvalidDataException(
                    "World save has an invalid file header.");
            }
        }

        private static void WriteCollectionCount(BinaryWriter writer, int count)
        {
            if (count < 0 || count > MaximumCollectionCount)
            {
                throw new InvalidOperationException(
                    "World save collection exceeds the format limit.");
            }

            writer.Write(count);
        }

        private static int ReadCollectionCount(BinaryReader reader)
        {
            var count = reader.ReadInt32();
            if (count < 0 || count > MaximumCollectionCount)
            {
                throw new InvalidDataException(
                    "World save collection exceeds the format limit.");
            }

            return count;
        }

        private static float ReadFiniteSingle(BinaryReader reader)
        {
            var value = reader.ReadSingle();
            if (!float.IsFinite(value))
            {
                throw new InvalidDataException(
                    "World save contains a non-finite value.");
            }

            return value;
        }
    }
}
