using MiniCivilization.World.Authoring;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Generation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MiniCivilization.World.Tests
{
    public sealed class WorldGenerationTests
    {
        private WorldGenerationSettings settings;

        [SetUp]
        public void SetUp()
        {
            settings = ScriptableObject.CreateInstance<WorldGenerationSettings>();
            settings.ConfigureDimensionsAndSeed(16, 8, 8, 4, 24680);
            var serialized = new SerializedObject(settings);
            serialized.FindProperty("baseTerrainHeightCells").intValue = 4;
            serialized.FindProperty("terrainAmplitudeCells").intValue = 2;
            serialized.FindProperty("seaLevelCell").intValue = 3;
            serialized.FindProperty("riverCount").intValue = 1;
            serialized.FindProperty("lakeCount").intValue = 1;
            serialized.FindProperty("lakeRadius").intValue = 2;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(settings);
        }

        [Test]
        public void SameSeed_ProducesSameCellHash()
        {
            var first = WorldGenerator.Generate(settings);
            var second = WorldGenerator.Generate(settings);
            Assert.That(WorldGenerator.ComputeStableHash(first.World), Is.EqualTo(WorldGenerator.ComputeStableHash(second.World)));
        }

        [Test]
        public void GeneratedCells_RespectQuantizedCapacity()
        {
            var result = WorldGenerator.Generate(settings);
            foreach (var chunk in result.World.EnumerateChunks())
            foreach (var cell in chunk.AsSpan())
            {
                Assert.That(cell.SolidFill, Is.InRange((byte)0, (byte)WorldGrid.HeightStepsPerCell));
                Assert.That(cell.WaterFill, Is.InRange((byte)0, (byte)WorldGrid.HeightStepsPerCell));
                Assert.That(cell.SolidFill + cell.WaterFill, Is.LessThanOrEqualTo(WorldGrid.HeightStepsPerCell));
            }
        }

        [Test]
        public void GeneratedWater_IsSupportedAndClassified()
        {
            var result = WorldGenerator.Generate(settings);
            Assert.That(result.WaterBodies.Count, Is.GreaterThan(0));

            for (var y = 1; y < result.World.Height; y++)
            for (var z = 0; z < result.World.Size; z++)
            for (var x = 0; x < result.World.Size; x++)
            {
                var cell = result.World.GetCell(x, y, z);
                if (!cell.HasWater)
                {
                    continue;
                }

                var below = result.World.GetCell(x, y - 1, z);
                Assert.That(cell.SolidFill > 0 || below.SolidFill + below.WaterFill == WorldGrid.HeightStepsPerCell, Is.True,
                    $"Unsupported water at ({x}, {y}, {z}).");
            }
        }
    }
}
