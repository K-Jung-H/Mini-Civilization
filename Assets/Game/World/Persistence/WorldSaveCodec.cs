using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Persistence
{
    public static class WorldSaveCodec
    {
        private const uint Magic = 0x3257434D; // "MCW2"
        private const uint Footer = 0x444E454D; // "MEND"
        private const uint WaterFlowScheduleMarker = 0x31534657; // "WFS1"
        private const uint EntitiesMarker = 0x31544E45; // "ENT1"
        private const ushort CurrentVersion = 9;
        private const int CellByteSize = 13;
        private const int EnvironmentByteSize = 5;
        private const int MaximumSectionBytes = 256 * 1024 * 1024;

        private enum ValueEncoding : byte
        {
            Raw = 0,
            RunLength = 1
        }

        private enum PayloadCompression : byte
        {
            None = 0,
            Deflate = 1
        }

        public static byte[] ToBytes(WorldData world)
        {
            using var stream = new MemoryStream();
            Write(stream, world);
            return stream.ToArray();
        }

        public static WorldData FromBytes(byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            using var stream = new MemoryStream(data, writable: false);
            return Read(stream);
        }

        public static void Write(Stream destination, WorldData world)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (!destination.CanWrite)
            {
                throw new ArgumentException("The destination stream is not writable.", nameof(destination));
            }

            using var writer = new BinaryWriter(destination, Encoding.UTF8, leaveOpen: true);
            writer.Write(Magic);
            writer.Write(CurrentVersion);
            writer.Write((byte)WorldGrid.HeightStepsPerCell);
            writer.Write((byte)0);
            WriteSettings(writer, world.Settings);

            var chunkCount = checked(world.ChunkCountX * world.ChunkCountY * world.ChunkCountZ);
            writer.Write(chunkCount);
            foreach (var chunk in world.EnumerateChunks())
            {
                writer.Write(chunk.Coordinate.X);
                writer.Write(chunk.Coordinate.Y);
                writer.Write(chunk.Coordinate.Z);
                WriteCellSection(writer, chunk.AsSpan());
            }

            var environmentSectionCount = checked(world.ChunkCountX * world.ChunkCountZ);
            writer.Write(environmentSectionCount);
            for (var chunkZ = 0; chunkZ < world.ChunkCountZ; chunkZ++)
            for (var chunkX = 0; chunkX < world.ChunkCountX; chunkX++)
            {
                writer.Write(chunkX);
                writer.Write(chunkZ);
                WriteEnvironmentSection(writer, world, chunkX, chunkZ);
            }

            WriteWaterFlowSchedule(writer, world.WaterFlowSchedule);
            WriteEntities(writer, world.Entities);
            writer.Write(Footer);
            writer.Flush();
        }

        public static WorldData Read(Stream source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (!source.CanRead)
            {
                throw new ArgumentException("The source stream is not readable.", nameof(source));
            }

            using var reader = new BinaryReader(source, Encoding.UTF8, leaveOpen: true);
            if (reader.ReadUInt32() != Magic)
            {
                throw new InvalidDataException("The stream is not a Mini Civilization world save.");
            }

            var version = reader.ReadUInt16();
            if (version != CurrentVersion)
            {
                throw new NotSupportedException(
                    $"World save version {version} is not supported. Current version is {CurrentVersion}.");
            }

            var heightSteps = reader.ReadByte();
            reader.ReadByte();
            if (heightSteps != WorldGrid.HeightStepsPerCell)
            {
                throw new InvalidDataException(
                    $"The save uses {heightSteps} height steps per Cell, but this build uses {WorldGrid.HeightStepsPerCell}.");
            }

            var world = new WorldData(ReadSettings(reader));
            var expectedChunkCount = checked(world.ChunkCountX * world.ChunkCountY * world.ChunkCountZ);
            var chunkCount = reader.ReadInt32();
            if (chunkCount != expectedChunkCount)
            {
                throw new InvalidDataException(
                    $"The save contains {chunkCount} Cell chunks; {expectedChunkCount} were expected.");
            }

            var loadedChunks = new bool[expectedChunkCount];
            for (var i = 0; i < chunkCount; i++)
            {
                var chunkX = reader.ReadInt32();
                var chunkY = reader.ReadInt32();
                var chunkZ = reader.ReadInt32();
                ValidateChunkCoordinate(world, chunkX, chunkY, chunkZ);
                var chunkIndex = chunkX
                    + world.ChunkCountX * (chunkZ + world.ChunkCountZ * chunkY);
                if (loadedChunks[chunkIndex])
                {
                    throw new InvalidDataException(
                        $"Cell chunk ({chunkX}, {chunkY}, {chunkZ}) occurs more than once.");
                }

                loadedChunks[chunkIndex] = true;
                ReadCellSection(reader, world.GetChunk(chunkX, chunkY, chunkZ));
            }

            var expectedEnvironmentSections = checked(world.ChunkCountX * world.ChunkCountZ);
            var environmentSectionCount = reader.ReadInt32();
            if (environmentSectionCount != expectedEnvironmentSections)
            {
                throw new InvalidDataException(
                    $"The save contains {environmentSectionCount} environment chunks; " +
                    $"{expectedEnvironmentSections} were expected.");
            }

            var loadedEnvironmentSections = new bool[expectedEnvironmentSections];
            for (var i = 0; i < environmentSectionCount; i++)
            {
                var chunkX = reader.ReadInt32();
                var chunkZ = reader.ReadInt32();
                if ((uint)chunkX >= world.ChunkCountX || (uint)chunkZ >= world.ChunkCountZ)
                {
                    throw new InvalidDataException(
                        $"Environment chunk ({chunkX}, {chunkZ}) is outside the world.");
                }

                var sectionIndex = chunkX + world.ChunkCountX * chunkZ;
                if (loadedEnvironmentSections[sectionIndex])
                {
                    throw new InvalidDataException(
                        $"Environment chunk ({chunkX}, {chunkZ}) occurs more than once.");
                }

                loadedEnvironmentSections[sectionIndex] = true;
                ReadEnvironmentSection(reader, world, chunkX, chunkZ);
            }

            if (reader.ReadUInt32() != WaterFlowScheduleMarker)
            {
                throw new InvalidDataException(
                    "The world save does not contain its water flow schedule.");
            }

            ReadWaterFlowSchedule(reader, world.WaterFlowSchedule, world);

            if (reader.ReadUInt32() != EntitiesMarker)
            {
                throw new InvalidDataException(
                    "The world save does not contain its entity data.");
            }

            ReadEntities(reader, world);

            if (reader.ReadUInt32() != Footer)
            {
                throw new InvalidDataException("The world save footer is missing or corrupt.");
            }

            return world;
        }

        private static void WriteWaterFlowSchedule(
            BinaryWriter writer,
            WaterFlowScheduleData schedule)
        {
            writer.Write(WaterFlowScheduleMarker);
            writer.Write(schedule.FrontierCellIndices.Count);
            for (var index = 0;
                 index < schedule.FrontierCellIndices.Count;
                 index++)
            {
                writer.Write(schedule.FrontierCellIndices[index]);
            }
        }

        private static void ReadWaterFlowSchedule(
            BinaryReader reader,
            WaterFlowScheduleData schedule,
            WorldData world)
        {
            var cellCapacity = checked(
                world.Size * world.Size * world.Height);
            var frontierCount = reader.ReadInt32();
            if (frontierCount < 0 || frontierCount > cellCapacity)
            {
                throw new InvalidDataException(
                    "The water flow frontier size is invalid.");
            }

            var frontier = new int[frontierCount];
            var seen = new bool[cellCapacity];
            for (var index = 0; index < frontierCount; index++)
            {
                var cellIndex = reader.ReadInt32();
                if ((uint)cellIndex >= cellCapacity)
                {
                    throw new InvalidDataException(
                        "A water flow frontier Cell is outside the world.");
                }

                if (seen[cellIndex])
                {
                    throw new InvalidDataException(
                        "A water flow frontier Cell occurs more than once.");
                }

                seen[cellIndex] = true;
                frontier[index] = cellIndex;
            }

            schedule.ReplaceFrontier(frontier);
        }

        private static void WriteEntities(
            BinaryWriter writer,
            System.Collections.Generic.IReadOnlyList<EntityData> entities)
        {
            writer.Write(EntitiesMarker);
            writer.Write(entities.Count);
            for (var index = 0; index < entities.Count; index++)
            {
                var entity = entities[index];
                writer.Write(entity.Id.Value);
                writer.Write((byte)entity.TypeKey.Category);
                writer.Write(entity.TypeKey.Value);
                writer.Write(entity.AnchorCell.X);
                writer.Write(entity.AnchorCell.Y);
                writer.Write(entity.AnchorCell.Z);
                writer.Write((byte)entity.Direction);
            }
        }

        private static void ReadEntities(BinaryReader reader, WorldData world)
        {
            var count = reader.ReadInt32();
            if (count < 0)
            {
                throw new InvalidDataException("The world entity count is invalid.");
            }

            for (var index = 0; index < count; index++)
            {
                var id = new EntityId(reader.ReadUInt64());
                var typeKey = new EntityTypeKey(
                    (EntityCategory)reader.ReadByte(),
                    reader.ReadUInt16());
                var anchor = new CellCoordinate(
                    reader.ReadInt32(),
                    reader.ReadInt32(),
                    reader.ReadInt32());
                var direction = (EntityDirection)reader.ReadByte();
                try
                {
                    world.AddEntity(new EntityData(
                        id,
                        typeKey,
                        anchor,
                        direction));
                }
                catch (Exception exception) when (
                    exception is ArgumentOutOfRangeException
                    || exception is InvalidOperationException)
                {
                    throw new InvalidDataException(
                        "The world save contains an invalid entity.",
                        exception);
                }
            }
        }

        private static void WriteCellSection(BinaryWriter writer, ReadOnlySpan<CellData> cells)
        {
            var rawLength = checked(cells.Length * CellByteSize);
            var runLength = CalculateCellRunLengthSize(cells);
            var encoding = runLength < rawLength ? ValueEncoding.RunLength : ValueEncoding.Raw;

            using var encodedStream = new MemoryStream(Math.Min(rawLength, runLength));
            using (var encodedWriter = new BinaryWriter(encodedStream, Encoding.UTF8, leaveOpen: true))
            {
                if (encoding == ValueEncoding.Raw)
                {
                    for (var i = 0; i < cells.Length; i++)
                    {
                        WriteCell(encodedWriter, cells[i]);
                    }
                }
                else
                {
                    WriteCellRuns(encodedWriter, cells);
                }
            }

            WriteSection(writer, cells.Length, encoding, encodedStream.ToArray());
        }

        private static void ReadCellSection(BinaryReader reader, ChunkData chunk)
        {
            var expectedCount = checked(chunk.SizeX * chunk.SizeY * chunk.SizeZ);
            var section = ReadSection(reader, expectedCount, CellByteSize);
            using var stream = new MemoryStream(section.Payload, writable: false);
            using var payloadReader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

            var localX = 0;
            var localY = 0;
            var localZ = 0;
            void Store(CellData cell)
            {
                chunk.SetCellRaw(localX, localY, localZ, cell);
                localX++;
                if (localX < chunk.SizeX)
                {
                    return;
                }

                localX = 0;
                localZ++;
                if (localZ < chunk.SizeZ)
                {
                    return;
                }

                localZ = 0;
                localY++;
            }

            if (section.Encoding == ValueEncoding.Raw)
            {
                for (var i = 0; i < expectedCount; i++)
                {
                    Store(ReadCell(payloadReader));
                }
            }
            else
            {
                var loaded = 0;
                while (loaded < expectedCount)
                {
                    var runLength = ReadVariableUInt(payloadReader);
                    if (runLength == 0 || runLength > expectedCount - loaded)
                    {
                        throw new InvalidDataException("A Cell run exceeds its chunk bounds.");
                    }

                    var cell = ReadCell(payloadReader);
                    for (var runIndex = 0; runIndex < runLength; runIndex++)
                    {
                        Store(cell);
                    }

                    loaded += runLength;
                }
            }

            EnsurePayloadConsumed(stream);
        }

        private static void WriteEnvironmentSection(
            BinaryWriter writer,
            WorldData world,
            int chunkX,
            int chunkZ)
        {
            var count = checked(world.ChunkSizeX * world.ChunkSizeZ);
            var values = new EnvironmentData[count];
            var startX = chunkX * world.ChunkSizeX;
            var startZ = chunkZ * world.ChunkSizeZ;
            var index = 0;
            for (var localZ = 0; localZ < world.ChunkSizeZ; localZ++)
            for (var localX = 0; localX < world.ChunkSizeX; localX++)
            {
                values[index++] = world.GetEnvironment(startX + localX, startZ + localZ);
            }

            var rawLength = checked(count * EnvironmentByteSize);
            var runLength = CalculateEnvironmentRunLengthSize(values);
            var encoding = runLength < rawLength ? ValueEncoding.RunLength : ValueEncoding.Raw;

            using var encodedStream = new MemoryStream(Math.Min(rawLength, runLength));
            using (var encodedWriter = new BinaryWriter(encodedStream, Encoding.UTF8, leaveOpen: true))
            {
                if (encoding == ValueEncoding.Raw)
                {
                    for (var i = 0; i < values.Length; i++)
                    {
                        WriteEnvironment(encodedWriter, values[i]);
                    }
                }
                else
                {
                    WriteEnvironmentRuns(encodedWriter, values);
                }
            }

            WriteSection(writer, count, encoding, encodedStream.ToArray());
        }

        private static void ReadEnvironmentSection(
            BinaryReader reader,
            WorldData world,
            int chunkX,
            int chunkZ)
        {
            var expectedCount = checked(world.ChunkSizeX * world.ChunkSizeZ);
            var section = ReadSection(reader, expectedCount, EnvironmentByteSize);
            using var stream = new MemoryStream(section.Payload, writable: false);
            using var payloadReader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            var startX = chunkX * world.ChunkSizeX;
            var startZ = chunkZ * world.ChunkSizeZ;
            var localIndex = 0;

            void Store(EnvironmentData value)
            {
                var localX = localIndex % world.ChunkSizeX;
                var localZ = localIndex / world.ChunkSizeX;
                world.SetEnvironment(startX + localX, startZ + localZ, value);
                localIndex++;
            }

            if (section.Encoding == ValueEncoding.Raw)
            {
                for (var i = 0; i < expectedCount; i++)
                {
                    Store(ReadEnvironment(payloadReader));
                }
            }
            else
            {
                var loaded = 0;
                while (loaded < expectedCount)
                {
                    var runLength = ReadVariableUInt(payloadReader);
                    if (runLength == 0 || runLength > expectedCount - loaded)
                    {
                        throw new InvalidDataException("An environment run exceeds its chunk bounds.");
                    }

                    var value = ReadEnvironment(payloadReader);
                    for (var runIndex = 0; runIndex < runLength; runIndex++)
                    {
                        Store(value);
                    }

                    loaded += runLength;
                }
            }

            EnsurePayloadConsumed(stream);
        }

        private static void WriteSection(
            BinaryWriter writer,
            int valueCount,
            ValueEncoding encoding,
            byte[] encodedPayload)
        {
            var compressed = Compress(encodedPayload);
            var compression = compressed.Length < encodedPayload.Length
                ? PayloadCompression.Deflate
                : PayloadCompression.None;
            var storedPayload = compression == PayloadCompression.Deflate
                ? compressed
                : encodedPayload;

            writer.Write(valueCount);
            writer.Write((byte)encoding);
            writer.Write((byte)compression);
            writer.Write((ushort)0);
            writer.Write(encodedPayload.Length);
            writer.Write(storedPayload.Length);
            writer.Write(ComputeChecksum(encodedPayload));
            writer.Write(storedPayload);
        }

        private static Section ReadSection(
            BinaryReader reader,
            int expectedValueCount,
            int rawValueSize)
        {
            var valueCount = reader.ReadInt32();
            if (valueCount != expectedValueCount)
            {
                throw new InvalidDataException(
                    $"Section value count {valueCount} does not match the expected {expectedValueCount}.");
            }

            var encoding = (ValueEncoding)reader.ReadByte();
            if (encoding is not ValueEncoding.Raw and not ValueEncoding.RunLength)
            {
                throw new InvalidDataException($"Unknown value encoding {(byte)encoding}.");
            }

            var compression = (PayloadCompression)reader.ReadByte();
            if (compression is not PayloadCompression.None and not PayloadCompression.Deflate)
            {
                throw new InvalidDataException($"Unknown payload compression {(byte)compression}.");
            }

            reader.ReadUInt16();
            var encodedLength = reader.ReadInt32();
            var storedLength = reader.ReadInt32();
            var expectedChecksum = reader.ReadUInt32();
            var maximumLength = checked(expectedValueCount * rawValueSize);
            if (encodedLength < 0
                || encodedLength > maximumLength
                || encodedLength > MaximumSectionBytes
                || storedLength < 0
                || storedLength > MaximumSectionBytes)
            {
                throw new InvalidDataException("Section payload lengths are invalid.");
            }

            var storedPayload = ReadBytesExact(reader, storedLength);
            var payload = compression == PayloadCompression.Deflate
                ? Decompress(storedPayload, encodedLength)
                : storedPayload;
            if (payload.Length != encodedLength)
            {
                throw new InvalidDataException(
                    $"Section decoded to {payload.Length} bytes; {encodedLength} were expected.");
            }

            if (ComputeChecksum(payload) != expectedChecksum)
            {
                throw new InvalidDataException("Section checksum validation failed.");
            }

            return new Section(encoding, payload);
        }

        private static int CalculateCellRunLengthSize(ReadOnlySpan<CellData> cells)
        {
            if (cells.IsEmpty)
            {
                return 0;
            }

            var length = 0;
            var runStart = 0;
            for (var i = 1; i <= cells.Length; i++)
            {
                if (i < cells.Length && cells[i].Equals(cells[runStart]))
                {
                    continue;
                }

                length = checked(length + VariableUIntByteCount(i - runStart) + CellByteSize);
                runStart = i;
            }

            return length;
        }

        private static void WriteCellRuns(BinaryWriter writer, ReadOnlySpan<CellData> cells)
        {
            if (cells.IsEmpty)
            {
                return;
            }

            var runStart = 0;
            for (var i = 1; i <= cells.Length; i++)
            {
                if (i < cells.Length && cells[i].Equals(cells[runStart]))
                {
                    continue;
                }

                WriteVariableUInt(writer, i - runStart);
                WriteCell(writer, cells[runStart]);
                runStart = i;
            }
        }

        private static int CalculateEnvironmentRunLengthSize(EnvironmentData[] values)
        {
            if (values.Length == 0)
            {
                return 0;
            }

            var length = 0;
            var runStart = 0;
            for (var i = 1; i <= values.Length; i++)
            {
                if (i < values.Length && EnvironmentEquals(values[i], values[runStart]))
                {
                    continue;
                }

                length = checked(length + VariableUIntByteCount(i - runStart) + EnvironmentByteSize);
                runStart = i;
            }

            return length;
        }

        private static void WriteEnvironmentRuns(
            BinaryWriter writer,
            EnvironmentData[] values)
        {
            if (values.Length == 0)
            {
                return;
            }

            var runStart = 0;
            for (var i = 1; i <= values.Length; i++)
            {
                if (i < values.Length && EnvironmentEquals(values[i], values[runStart]))
                {
                    continue;
                }

                WriteVariableUInt(writer, i - runStart);
                WriteEnvironment(writer, values[runStart]);
                runStart = i;
            }
        }

        private static void WriteCell(BinaryWriter writer, CellData cell)
        {
            writer.Write((ushort)cell.Terrain.Material);
            writer.Write((ushort)cell.Terrain.Surface);
            writer.Write((ushort)cell.Terrain.Geology);
            writer.Write(cell.Terrain.ResourceId);
            writer.Write(cell.Terrain.SolidHeight);
            writer.Write(cell.Water.Amount);
            writer.Write((byte)cell.Water.Role);
            writer.Write((byte)cell.Water.Type);
            writer.Write((byte)cell.Water.Flow);
        }

        private static CellData ReadCell(BinaryReader reader)
        {
            return new CellData
            {
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
                }
            };
        }

        private static void WriteEnvironment(BinaryWriter writer, EnvironmentData value)
        {
            writer.Write((ushort)value.Biome);
            writer.Write(value.Temperature);
            writer.Write(value.Moisture);
            writer.Write(value.Fertility);
        }

        private static EnvironmentData ReadEnvironment(BinaryReader reader)
        {
            return new EnvironmentData
            {
                Biome = (BiomeType)reader.ReadUInt16(),
                Temperature = reader.ReadByte(),
                Moisture = reader.ReadByte(),
                Fertility = reader.ReadByte()
            };
        }

        private static byte[] Compress(byte[] payload)
        {
            using var output = new MemoryStream();
            using (var deflate = new DeflateStream(output, CompressionLevel.Fastest, leaveOpen: true))
            {
                deflate.Write(payload, 0, payload.Length);
            }

            return output.ToArray();
        }

        private static byte[] Decompress(byte[] payload, int expectedLength)
        {
            using var input = new MemoryStream(payload, writable: false);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream(expectedLength);
            var buffer = new byte[8192];
            while (true)
            {
                var read = deflate.Read(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    break;
                }

                if (output.Length + read > expectedLength)
                {
                    throw new InvalidDataException("Compressed section expands beyond its declared size.");
                }

                output.Write(buffer, 0, read);
            }

            return output.ToArray();
        }

        private static uint ComputeChecksum(byte[] payload)
        {
            const uint offset = 2166136261u;
            const uint prime = 16777619u;
            var hash = offset;
            for (var i = 0; i < payload.Length; i++)
            {
                hash ^= payload[i];
                hash *= prime;
            }

            return hash;
        }

        private static void WriteVariableUInt(BinaryWriter writer, int value)
        {
            var remaining = (uint)value;
            while (remaining >= 0x80)
            {
                writer.Write((byte)(remaining | 0x80));
                remaining >>= 7;
            }

            writer.Write((byte)remaining);
        }

        private static int ReadVariableUInt(BinaryReader reader)
        {
            uint result = 0;
            for (var shift = 0; shift < 35; shift += 7)
            {
                var value = reader.ReadByte();
                result |= (uint)(value & 0x7F) << shift;
                if ((value & 0x80) == 0)
                {
                    if (result > int.MaxValue)
                    {
                        throw new InvalidDataException("Variable-length integer exceeds Int32.");
                    }

                    return (int)result;
                }
            }

            throw new InvalidDataException("Variable-length integer is malformed.");
        }

        private static int VariableUIntByteCount(int value)
        {
            var count = 1;
            var remaining = (uint)value;
            while (remaining >= 0x80)
            {
                count++;
                remaining >>= 7;
            }

            return count;
        }

        private static bool EnvironmentEquals(
            EnvironmentData left,
            EnvironmentData right)
        {
            return left.Biome == right.Biome
                && left.Temperature == right.Temperature
                && left.Moisture == right.Moisture
                && left.Fertility == right.Fertility;
        }

        private static byte[] ReadBytesExact(BinaryReader reader, int count)
        {
            var bytes = reader.ReadBytes(count);
            if (bytes.Length != count)
            {
                throw new EndOfStreamException(
                    $"Expected {count} payload bytes but reached the end of the stream.");
            }

            return bytes;
        }

        private static void EnsurePayloadConsumed(Stream stream)
        {
            if (stream.Position != stream.Length)
            {
                throw new InvalidDataException("Section contains trailing decoded data.");
            }
        }

        private static void WriteSettings(
            BinaryWriter writer,
            WorldSettingsData settings)
        {
            writer.Write(settings.Seed);
            writer.Write(settings.CellSize);
            writer.Write(settings.ChunkCellCountXZ);
            writer.Write(settings.ChunkCellCountY);
            writer.Write(settings.WorldChunkCountXZ);
            writer.Write(settings.WorldChunkCountY);
            writer.Write(settings.RenderChunksPerPatch);
            writer.Write(settings.TerrainScale);
            writer.Write(settings.TerrainLayers);
            writer.Write(settings.TerrainSpacing);
            writer.Write(settings.TerrainDetail);
            writer.Write(settings.BaseHeightUnits);
            writer.Write(settings.HeightVariationUnits);
            writer.Write(settings.EdgeLowering);
            writer.Write(settings.MountainScale);
            writer.Write(settings.MountainHeightUnits);
            writer.Write(settings.MountainCoverage);
            writer.Write(settings.MountainSteepness);
            writer.Write(settings.SeaLevelUnits);
            writer.Write(settings.RiverCount);
            writer.Write(settings.RiverDepthCells);
            writer.Write(settings.MaximumRiverWidthCells);
            writer.Write(settings.MaximumRiverDepthCells);
            writer.Write(settings.LakeCount);
            writer.Write(settings.MinimumInlandLakeDistance);
            writer.Write(settings.MinimumInlandLakeArea);
            writer.Write(settings.MinimumInlandLakeDepthSteps);
            writer.Write(settings.PondMaximumArea);
            writer.Write(settings.WaterFlowRules.SpreadAmountLoss);
            writer.Write(settings.WaterFlowRules.MinimumSpreadAmount);
            writer.Write(settings.WaterFlowRules.DissipationAmountLoss);
            writer.Write(settings.DesertMoistureThreshold);
            writer.Write(settings.WetlandMoistureThreshold);
            writer.Write(settings.SnowTemperatureThreshold);
            writer.Write(settings.WaterMoistureRadius);
        }

        private static WorldSettingsData ReadSettings(BinaryReader reader)
        {
            var seed = reader.ReadInt32();
            var cellSize = reader.ReadSingle();
            var chunkCellCountXZ = ReadPositiveDimension(
                reader,
                "horizontal chunk Cell count");
            var chunkCellCountY = ReadPositiveDimension(
                reader,
                "vertical chunk Cell count");
            var worldChunkCountXZ = ReadPositiveDimension(
                reader,
                "horizontal world chunk count");
            var worldChunkCountY = ReadPositiveDimension(
                reader,
                "vertical world chunk count");
            var renderChunksPerPatch = ReadPositiveDimension(
                reader,
                "render chunks per patch");

            var settings = new WorldSettingsData(
                seed,
                cellSize,
                chunkCellCountXZ,
                chunkCellCountY,
                worldChunkCountXZ,
                worldChunkCountY,
                renderChunksPerPatch,
                reader.ReadSingle(),
                reader.ReadInt32(),
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadInt32(),
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                new WaterFlowRules(
                    reader.ReadByte(),
                    reader.ReadByte(),
                    reader.ReadByte()),
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadInt32());

            var cellCount = checked(
                (long)settings.WorldSize
                * settings.WorldSize
                * settings.WorldHeight);
            if (cellCount > int.MaxValue)
            {
                throw new InvalidDataException(
                    "Saved world is too large for this runtime.");
            }

            return settings;
        }

        private static int ReadPositiveDimension(BinaryReader reader, string name)
        {
            var value = reader.ReadInt32();
            if (value <= 0)
            {
                throw new InvalidDataException($"Saved {name} must be positive.");
            }

            return value;
        }

        private static void ValidateChunkCoordinate(
            WorldData world,
            int chunkX,
            int chunkY,
            int chunkZ)
        {
            if ((uint)chunkX >= world.ChunkCountX
                || (uint)chunkY >= world.ChunkCountY
                || (uint)chunkZ >= world.ChunkCountZ)
            {
                throw new InvalidDataException(
                    $"Cell chunk ({chunkX}, {chunkY}, {chunkZ}) is outside the world.");
            }
        }

        private readonly struct Section
        {
            public readonly ValueEncoding Encoding;
            public readonly byte[] Payload;

            public Section(ValueEncoding encoding, byte[] payload)
            {
                Encoding = encoding;
                Payload = payload;
            }
        }
    }
}
