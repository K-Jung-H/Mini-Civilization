using System.IO;

namespace MiniCivilization.World.Persistence
{
    // File layout and procedural generation compatibility are separate versions.
    internal static class WorldGenerationSaveHeader
    {
        private const uint Magic = 0x57534434;
        private const uint LegacyMagic = 0x57534433;
        internal const int CurrentGenerationVersion = 15;

        public static void Write(BinaryWriter writer)
        {
            writer.Write(Magic);
            writer.Write(CurrentGenerationVersion);
        }

        public static void Read(BinaryReader reader)
        {
            var magic = reader.ReadUInt32();
            if (magic == LegacyMagic)
                throw new InvalidDataException(
                    "This world has no generation version. Create a new world; automatic migration is not supported.");
            if (magic != Magic)
                throw new InvalidDataException("World save header is invalid.");

            var version = reader.ReadInt32();
            if (version != CurrentGenerationVersion)
                throw new InvalidDataException(
                    $"World generation version {version} is incompatible with {CurrentGenerationVersion}. Create a new world.");
        }
    }
}
