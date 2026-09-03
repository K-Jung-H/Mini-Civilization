using System;
using System.IO;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Persistence
{
    internal static class WorldRegionFile
    {
        public const int ChunksPerAxis = 32;

        private const uint Magic = 0x31475257;
        private const int EntryCount = ChunksPerAxis * ChunksPerAxis;
        private const int MaximumChunkPayloadLength = 64 * 1024 * 1024;
        private const long MinimumCompactionWasteBytes = 1024 * 1024;
        private const int HeaderSize = 4 + 16 + 4 + 4 + 4;
        private const int EntrySize = 8 + 4 + 4;
        private const int IndexPageDataSize = 8 + EntryCount * EntrySize;
        private const int IndexPageSize = IndexPageDataSize + 4;
        private const long DataOffset = HeaderSize + 2L * IndexPageSize;
        private static readonly uint[] Crc32Table = CreateCrc32Table();

        public static void GetRegionCoordinate(
            ChunkCoordinate coordinate,
            out int regionX,
            out int regionZ,
            out int slot)
        {
            regionX = FloorDivide(coordinate.X, ChunksPerAxis);
            regionZ = FloorDivide(coordinate.Z, ChunksPerAxis);
            var localX = coordinate.X - regionX * ChunksPerAxis;
            var localZ = coordinate.Z - regionZ * ChunksPerAxis;
            slot = localZ * ChunksPerAxis + localX;
        }

        public static bool TryReadChunk(
            string path,
            Guid worldId,
            ChunkCoordinate coordinate,
            out WorldChunkSnapshot snapshot)
        {
            GetRegionCoordinate(
                coordinate,
                out var regionX,
                out var regionZ,
                out var slot);
            using var stream = File.Open(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            var state = ReadState(stream, worldId, regionX, regionZ);
            var entry = state.Entries[slot];
            if (entry.IsEmpty)
            {
                snapshot = null;
                return false;
            }

            var payload = ReadPayload(stream, entry);
            using var payloadStream = new MemoryStream(payload, false);
            snapshot = WorldSaveCodec.ReadChunk(payloadStream);
            if (!snapshot.Coordinate.Equals(coordinate))
            {
                throw new InvalidDataException(
                    "Region Chunk entry does not match its coordinate.");
            }

            return true;
        }

        public static void WriteChunk(
            string path,
            Guid worldId,
            WorldChunkSnapshot snapshot)
        {
            GetRegionCoordinate(
                snapshot.Coordinate,
                out var regionX,
                out var regionZ,
                out var slot);
            var payload = SerializeChunk(snapshot);
            Directory.CreateDirectory(Path.GetDirectoryName(path)
                ?? throw new InvalidOperationException(
                    "Region file has no parent directory."));

            RegionIndexState state;
            using (var stream = File.Open(
                       path,
                       FileMode.OpenOrCreate,
                       FileAccess.ReadWrite,
                       FileShare.None))
            {
                state = stream.Length == 0
                    ? Initialize(stream, worldId, regionX, regionZ)
                    : ReadState(stream, worldId, regionX, regionZ);

                var entries = (RegionEntry[])state.Entries.Clone();
                var offset = stream.Length;
                stream.Position = offset;
                stream.Write(payload, 0, payload.Length);
                stream.Flush();

                entries[slot] = new RegionEntry(
                    offset,
                    payload.Length,
                    ComputeCrc32(payload));
                var nextPage = state.PageIndex == 0 ? 1 : 0;
                var nextSequence = checked(state.Sequence + 1);
                WriteIndexPage(stream, nextPage, nextSequence, entries);
                stream.Flush();
                state = new RegionIndexState(nextPage, nextSequence, entries);
            }

            if (ShouldCompact(path, state.Entries))
            {
                Compact(path, worldId, regionX, regionZ, state);
            }
        }

        public static void ReplaceWorldId(
            string path,
            Guid expectedWorldId,
            Guid newWorldId)
        {
            using var stream = File.Open(
                path,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);
            if (stream.Length < HeaderSize)
            {
                throw new InvalidDataException("Region file header is truncated.");
            }

            stream.Position = 0;
            using (var reader = new BinaryReader(
                       stream,
                       System.Text.Encoding.UTF8,
                       true))
            {
                if (reader.ReadUInt32() != Magic
                    || new Guid(ReadExactBytes(reader, 16)) != expectedWorldId)
                {
                    throw new InvalidDataException(
                        "Region file belongs to a different World Save.");
                }
            }

            stream.Position = sizeof(uint);
            var worldIdBytes = newWorldId.ToByteArray();
            stream.Write(worldIdBytes, 0, worldIdBytes.Length);
            stream.Flush();
        }

        private static RegionIndexState Initialize(
            FileStream stream,
            Guid worldId,
            int regionX,
            int regionZ)
        {
            stream.SetLength(0);
            stream.Position = 0;
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            {
                writer.Write(Magic);
                writer.Write(worldId.ToByteArray());
                writer.Write(regionX);
                writer.Write(regionZ);
                writer.Write(EntryCount);
            }

            var entries = new RegionEntry[EntryCount];
            WriteIndexPage(stream, 0, 0, entries);
            WriteIndexPage(stream, 1, 0, entries);
            stream.Flush();
            return new RegionIndexState(0, 0, entries);
        }

        private static RegionIndexState ReadState(
            FileStream stream,
            Guid expectedWorldId,
            int expectedRegionX,
            int expectedRegionZ)
        {
            if (stream.Length < DataOffset)
            {
                throw new InvalidDataException("Region file header is truncated.");
            }

            stream.Position = 0;
            using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true))
            {
                if (reader.ReadUInt32() != Magic)
                {
                    throw new InvalidDataException("Region file has an invalid header.");
                }

                if (new Guid(ReadExactBytes(reader, 16)) != expectedWorldId)
                {
                    throw new InvalidDataException(
                        "Region file belongs to a different World Save.");
                }

                if (reader.ReadInt32() != expectedRegionX
                    || reader.ReadInt32() != expectedRegionZ
                    || reader.ReadInt32() != EntryCount)
                {
                    throw new InvalidDataException(
                        "Region file coordinate or index size is invalid.");
                }
            }

            var first = TryReadIndexPage(stream, 0, out var firstState);
            var second = TryReadIndexPage(stream, 1, out var secondState);
            if (!first && !second)
            {
                throw new InvalidDataException("Region file has no valid index page.");
            }

            if (!second || (first && firstState.Sequence >= secondState.Sequence))
            {
                return firstState;
            }

            return secondState;
        }

        private static bool TryReadIndexPage(
            FileStream stream,
            int pageIndex,
            out RegionIndexState state)
        {
            state = default;
            var pageOffset = HeaderSize + (long)pageIndex * IndexPageSize;
            if (stream.Length < pageOffset + IndexPageSize)
            {
                return false;
            }

            stream.Position = pageOffset;
            var pageData = ReadExactBytes(stream, IndexPageDataSize);
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
            var storedCrc = reader.ReadUInt32();
            if (storedCrc != ComputeCrc32(pageData))
            {
                return false;
            }

            using var pageStream = new MemoryStream(pageData, false);
            using var pageReader = new BinaryReader(
                pageStream,
                System.Text.Encoding.UTF8,
                true);
            var sequence = pageReader.ReadInt64();
            if (sequence < 0)
            {
                return false;
            }

            var entries = new RegionEntry[EntryCount];
            for (var index = 0; index < entries.Length; index++)
            {
                var entry = new RegionEntry(
                    pageReader.ReadInt64(),
                    pageReader.ReadInt32(),
                    pageReader.ReadUInt32());
                if (!entry.IsValid)
                {
                    return false;
                }

                entries[index] = entry;
            }

            state = new RegionIndexState(pageIndex, sequence, entries);
            return true;
        }

        private static void WriteIndexPage(
            FileStream stream,
            int pageIndex,
            long sequence,
            RegionEntry[] entries)
        {
            var pageData = new byte[IndexPageDataSize];
            using (var pageStream = new MemoryStream(pageData, true))
            using (var writer = new BinaryWriter(
                       pageStream,
                       System.Text.Encoding.UTF8,
                       true))
            {
                writer.Write(sequence);
                for (var index = 0; index < entries.Length; index++)
                {
                    writer.Write(entries[index].Offset);
                    writer.Write(entries[index].Length);
                    writer.Write(entries[index].Checksum);
                }
            }

            stream.Position = HeaderSize + (long)pageIndex * IndexPageSize;
            stream.Write(pageData, 0, pageData.Length);
            using var streamWriter = new BinaryWriter(
                stream,
                System.Text.Encoding.UTF8,
                true);
            streamWriter.Write(ComputeCrc32(pageData));
        }

        private static byte[] SerializeChunk(WorldChunkSnapshot snapshot)
        {
            using var stream = new MemoryStream();
            WorldSaveCodec.WriteChunk(stream, snapshot);
            if (stream.Length == 0 || stream.Length > MaximumChunkPayloadLength)
            {
                throw new InvalidOperationException(
                    "Serialized World Chunk exceeds the Region payload limit.");
            }

            return stream.ToArray();
        }

        private static byte[] ReadPayload(FileStream stream, RegionEntry entry)
        {
            if (entry.Offset < DataOffset
                || entry.Length <= 0
                || entry.Offset > stream.Length - entry.Length)
            {
                throw new InvalidDataException("Region Chunk entry points outside the file.");
            }

            stream.Position = entry.Offset;
            var payload = ReadExactBytes(stream, entry.Length);
            if (ComputeCrc32(payload) != entry.Checksum)
            {
                throw new InvalidDataException("Region Chunk payload checksum is invalid.");
            }

            return payload;
        }

        private static bool ShouldCompact(string path, RegionEntry[] entries)
        {
            var fileLength = new FileInfo(path).Length;
            var activeBytes = 0L;
            for (var index = 0; index < entries.Length; index++)
            {
                activeBytes += entries[index].Length;
            }

            var wasteBytes = fileLength - DataOffset - activeBytes;
            return wasteBytes >= MinimumCompactionWasteBytes
                && wasteBytes >= activeBytes;
        }

        private static void Compact(
            string path,
            Guid worldId,
            int regionX,
            int regionZ,
            RegionIndexState state)
        {
            var temporaryPath = path + ".compact";
            try
            {
                using (var source = File.Open(
                           path,
                           FileMode.Open,
                           FileAccess.Read,
                           FileShare.Read))
                using (var destination = File.Open(
                           temporaryPath,
                           FileMode.Create,
                           FileAccess.ReadWrite,
                           FileShare.None))
                {
                    Initialize(destination, worldId, regionX, regionZ);
                    var compactedEntries = new RegionEntry[EntryCount];
                    for (var index = 0; index < state.Entries.Length; index++)
                    {
                        var entry = state.Entries[index];
                        if (entry.IsEmpty)
                        {
                            continue;
                        }

                        if (entry.Offset < DataOffset
                            || entry.Length <= 0
                            || entry.Offset > source.Length - entry.Length)
                        {
                            throw new InvalidDataException(
                                "Region Chunk entry points outside the file.");
                        }

                        var offset = destination.Length;
                        CopyBytes(source, entry.Offset, destination, entry.Length);
                        compactedEntries[index] = new RegionEntry(
                            offset,
                            entry.Length,
                            entry.Checksum);
                    }

                    WriteIndexPage(
                        destination,
                        0,
                        checked(state.Sequence + 1),
                        compactedEntries);
                    destination.Flush();
                }

                File.Replace(temporaryPath, path, null);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        private static void CopyBytes(
            FileStream source,
            long sourceOffset,
            FileStream destination,
            int length)
        {
            source.Position = sourceOffset;
            destination.Position = destination.Length;
            var buffer = new byte[Math.Min(81920, length)];
            var remaining = length;
            while (remaining > 0)
            {
                var count = source.Read(buffer, 0, Math.Min(buffer.Length, remaining));
                if (count == 0)
                {
                    throw new EndOfStreamException(
                        "Region Chunk payload is truncated.");
                }

                destination.Write(buffer, 0, count);
                remaining -= count;
            }
        }

        private static int FloorDivide(int value, int divisor)
        {
            var quotient = value / divisor;
            if (value % divisor < 0)
            {
                quotient--;
            }

            return quotient;
        }

        private static byte[] ReadExactBytes(Stream stream, int length)
        {
            var bytes = new byte[length];
            var offset = 0;
            while (offset < length)
            {
                var read = stream.Read(bytes, offset, length - offset);
                if (read == 0)
                {
                    throw new EndOfStreamException("Region file is truncated.");
                }

                offset += read;
            }

            return bytes;
        }

        private static byte[] ReadExactBytes(BinaryReader reader, int length) =>
            ReadExactBytes(reader.BaseStream, length);

        private static uint ComputeCrc32(byte[] bytes)
        {
            var crc = 0xffffffffu;
            for (var index = 0; index < bytes.Length; index++)
            {
                crc = Crc32Table[(crc ^ bytes[index]) & 0xff] ^ (crc >> 8);
            }

            return ~crc;
        }

        private static uint[] CreateCrc32Table()
        {
            var table = new uint[256];
            for (uint value = 0; value < table.Length; value++)
            {
                var current = value;
                for (var bit = 0; bit < 8; bit++)
                {
                    current = (current & 1) != 0
                        ? 0xedb88320u ^ (current >> 1)
                        : current >> 1;
                }

                table[value] = current;
            }

            return table;
        }

        private readonly struct RegionIndexState
        {
            public RegionIndexState(
                int pageIndex,
                long sequence,
                RegionEntry[] entries)
            {
                PageIndex = pageIndex;
                Sequence = sequence;
                Entries = entries;
            }

            public int PageIndex { get; }
            public long Sequence { get; }
            public RegionEntry[] Entries { get; }
        }

        private readonly struct RegionEntry
        {
            public RegionEntry(long offset, int length, uint checksum)
            {
                Offset = offset;
                Length = length;
                Checksum = checksum;
            }

            public long Offset { get; }
            public int Length { get; }
            public uint Checksum { get; }
            public bool IsEmpty => Offset == 0 && Length == 0 && Checksum == 0;
            public bool IsValid => IsEmpty || (Offset >= DataOffset
                                               && Length > 0
                                               && Length <= MaximumChunkPayloadLength);
        }
    }
}
