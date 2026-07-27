using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Persistence
{
    public static class WorldSaveCodec
    {
        private const uint Magic = 0x3157434D; // "MCW1"
        private const uint Footer = 0x444E454D; // "MEND"
        private const uint WaterStateMarker = 0x32544157; // "WAT2"
        private const ushort CurrentVersion = 1;
        private const int CellByteSize = 14;
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
            writer.Write(world.Size);
            writer.Write(world.Height);
            writer.Write(world.ChunkSizeX);
            writer.Write(world.ChunkSizeY);
            writer.Write(world.ChunkSizeZ);
            writer.Write(world.Seed);

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

            WriteWaterState(writer, world.WaterState);
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

            var size = ReadPositiveDimension(reader, "world size");
            var height = ReadPositiveDimension(reader, "world height");
            var chunkSizeX = ReadPositiveDimension(reader, "chunk size X");
            var chunkSizeY = ReadPositiveDimension(reader, "chunk size Y");
            var chunkSizeZ = ReadPositiveDimension(reader, "chunk size Z");
            var seed = reader.ReadInt32();
            ValidateDimensions(size, height, chunkSizeX, chunkSizeY, chunkSizeZ);

            var world = new WorldData(size, height, chunkSizeX, chunkSizeY, chunkSizeZ, seed);
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

            var trailingMarker = reader.ReadUInt32();
            if (trailingMarker == WaterStateMarker)
            {
                ReadWaterState(reader, world.WaterState);
                trailingMarker = reader.ReadUInt32();
            }
            else
            {
                throw new InvalidDataException(
                    "The world save does not contain the current water state section.");
            }

            if (trailingMarker != Footer)
            {
                throw new InvalidDataException("The world save footer is missing or corrupt.");
            }

            world.RebuildAllSurfaceColumns();
            return world;
        }

        private static void WriteWaterState(
            BinaryWriter writer,
            WaterState waterState)
        {
            writer.Write(WaterStateMarker);
            writer.Write(waterState.IsInitialized);
            writer.Write(waterState.MaximumAmount);
            var populatedCount = 0;
            for (var index = 0; index < waterState.CellCount; index++)
            {
                if (waterState.GetAmount(index) != 0
                    || waterState.GetBehavior(index) != WaterCellBehavior.None
                    || waterState.GetSourceGroupId(index) != 0)
                {
                    populatedCount++;
                }
            }

            writer.Write(populatedCount);
            for (var index = 0; index < waterState.CellCount; index++)
            {
                var amount = waterState.GetAmount(index);
                var behavior = waterState.GetBehavior(index);
                var sourceGroupId = waterState.GetSourceGroupId(index);
                if (amount == 0
                    && behavior == WaterCellBehavior.None
                    && sourceGroupId == 0)
                {
                    continue;
                }

                writer.Write(index);
                writer.Write(amount);
                writer.Write((byte)behavior);
                writer.Write(sourceGroupId);
            }

            writer.Write(waterState.SourceGroups.Count);
            for (var groupIndex = 0;
                 groupIndex < waterState.SourceGroups.Count;
                 groupIndex++)
            {
                var group = waterState.SourceGroups[groupIndex];
                writer.Write(group.Id);
                writer.Write((ushort)group.WaterType);
                writer.Write(group.OutputSurfaceTenths);
                writer.Write(group.SourceAmount);
                writer.Write(group.CellIndices.Count);
                for (var cellIndex = 0;
                     cellIndex < group.CellIndices.Count;
                     cellIndex++)
                {
                    writer.Write(group.CellIndices[cellIndex]);
                }
            }
        }

        private static void ReadWaterState(
            BinaryReader reader,
            WaterState waterState)
        {
            var initialized = reader.ReadBoolean();
            waterState.ConfigureMaximumAmount(
                Math.Max((ushort)1, reader.ReadUInt16()));
            var entryCount = reader.ReadInt32();
            if (entryCount < 0 || entryCount > waterState.CellCount)
            {
                throw new InvalidDataException("The water state entry count is invalid.");
            }

            for (var entryIndex = 0; entryIndex < entryCount; entryIndex++)
            {
                var cellIndex = reader.ReadInt32();
                if ((uint)cellIndex >= waterState.CellCount)
                {
                    throw new InvalidDataException("A water state Cell index is outside the world.");
                }

                var amount = reader.ReadUInt16();
                var behavior = (WaterCellBehavior)reader.ReadByte();
                var sourceGroupId = reader.ReadInt32();
                waterState.SetCell(cellIndex, amount, behavior, sourceGroupId);
            }

            var groupCount = reader.ReadInt32();
            if (groupCount < 0 || groupCount > waterState.CellCount)
            {
                throw new InvalidDataException("The water source group count is invalid.");
            }

            var groups = new WaterSourceGroupData[groupCount];
            for (var groupIndex = 0; groupIndex < groupCount; groupIndex++)
            {
                var id = reader.ReadInt32();
                var waterType = (WaterType)reader.ReadUInt16();
                var outputSurfaceTenths = reader.ReadInt16();
                var sourceAmount = reader.ReadUInt16();
                var cellCount = reader.ReadInt32();
                if (cellCount < 0 || cellCount > waterState.CellCount)
                {
                    throw new InvalidDataException("A water source group size is invalid.");
                }

                var cellIndices = new int[cellCount];
                for (var cellIndex = 0; cellIndex < cellCount; cellIndex++)
                {
                    cellIndices[cellIndex] = reader.ReadInt32();
                    if ((uint)cellIndices[cellIndex] >= waterState.CellCount)
                    {
                        throw new InvalidDataException("A source Cell index is outside the world.");
                    }
                }

                groups[groupIndex] = new WaterSourceGroupData(
                    id,
                    waterType,
                    outputSurfaceTenths,
                    sourceAmount,
                    cellIndices);
            }

            waterState.ReplaceSourceGroups(groups);
            if (initialized)
            {
                waterState.MarkInitialized();
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
                chunk.SetCellBulk(localX, localY, localZ, cell);
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
            var values = new ColumnEnvironmentData[count];
            var startX = chunkX * world.ChunkSizeX;
            var startZ = chunkZ * world.ChunkSizeZ;
            var index = 0;
            for (var localZ = 0; localZ < world.ChunkSizeZ; localZ++)
            for (var localX = 0; localX < world.ChunkSizeX; localX++)
            {
                values[index++] = world.GetColumnEnvironment(startX + localX, startZ + localZ);
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

            void Store(ColumnEnvironmentData value)
            {
                var localX = localIndex % world.ChunkSizeX;
                var localZ = localIndex / world.ChunkSizeX;
                world.SetColumnEnvironment(startX + localX, startZ + localZ, value);
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

        private static int CalculateEnvironmentRunLengthSize(ColumnEnvironmentData[] values)
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
            ColumnEnvironmentData[] values)
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
            writer.Write((ushort)cell.Material);
            writer.Write((ushort)cell.Surface);
            writer.Write((ushort)cell.Water);
            writer.Write((ushort)cell.Geology);
            writer.Write(cell.DepositIndex);
            writer.Write(cell.SolidFill);
            writer.Write(cell.WaterFill);
            writer.Write((ushort)cell.Flags);
        }

        private static CellData ReadCell(BinaryReader reader)
        {
            return new CellData
            {
                Material = (CellMaterialType)reader.ReadUInt16(),
                Surface = (SurfaceType)reader.ReadUInt16(),
                Water = (WaterType)reader.ReadUInt16(),
                Geology = (CellMaterialType)reader.ReadUInt16(),
                DepositIndex = reader.ReadUInt16(),
                SolidFill = reader.ReadByte(),
                WaterFill = reader.ReadByte(),
                Flags = (CellFlags)reader.ReadUInt16()
            };
        }

        private static void WriteEnvironment(BinaryWriter writer, ColumnEnvironmentData value)
        {
            writer.Write((ushort)value.Biome);
            writer.Write(value.Temperature);
            writer.Write(value.Moisture);
            writer.Write(value.Fertility);
        }

        private static ColumnEnvironmentData ReadEnvironment(BinaryReader reader)
        {
            return new ColumnEnvironmentData
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
            ColumnEnvironmentData left,
            ColumnEnvironmentData right)
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

        private static int ReadPositiveDimension(BinaryReader reader, string name)
        {
            var value = reader.ReadInt32();
            if (value <= 0)
            {
                throw new InvalidDataException($"Saved {name} must be positive.");
            }

            return value;
        }

        private static void ValidateDimensions(
            int size,
            int height,
            int chunkSizeX,
            int chunkSizeY,
            int chunkSizeZ)
        {
            if (size % chunkSizeX != 0
                || size % chunkSizeZ != 0
                || height % chunkSizeY != 0)
            {
                throw new InvalidDataException(
                    "Saved world dimensions are not divisible by their chunk dimensions.");
            }

            var cellCount = checked((long)size * size * height);
            if (cellCount > int.MaxValue)
            {
                throw new InvalidDataException("Saved world is too large for this runtime.");
            }
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
