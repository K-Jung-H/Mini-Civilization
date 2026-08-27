using System;
using System.Threading.Tasks;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Runtime;

namespace MiniCivilization.World.Generation
{
    internal sealed class WorldGenerationOperation : WorldOperation
    {
        private enum Phase : byte
        {
            NotStarted,
            GenerateTerrain,
            PrepareRuntime,
            Ready
        }

        private readonly WorldBuildInput input;
        private Phase phase;
        private Task<WorldData> generationTask;
        private Task<WorldRuntime> prepareRuntimeTask;

        public WorldGenerationOperation(WorldBuildInput input)
            : base(WorldOperationKind.Generate, stageCount: 3)
        {
            this.input = input ?? throw new ArgumentNullException(nameof(input));
        }

        public override void Update()
        {
            if (IsFailed || IsReadyForActivation)
            {
                return;
            }

            try
            {
                switch (phase)
                {
                    case Phase.NotStarted:
                        BeginStage(WorldOperationStage.Terrain);
                        generationTask = Task.Run(
                            () => WorldGenerationPipeline.Build(input));
                        phase = Phase.GenerateTerrain;
                        break;
                    case Phase.GenerateTerrain:
                        FinishGeneration();
                        break;
                    case Phase.PrepareRuntime:
                        FinishRuntimePreparation();
                        break;
                }
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }

        public override void Dispose()
        {
        }

        private void FinishGeneration()
        {
            if (!generationTask.IsCompleted)
            {
                return;
            }

            var world = generationTask.GetAwaiter().GetResult();
            CompleteCurrentStage();
            BeginStage(WorldOperationStage.PrepareRuntime);
            prepareRuntimeTask = Task.Run(
                () => WorldRuntime.CreatePrepared(world));
            phase = Phase.PrepareRuntime;
        }

        private void FinishRuntimePreparation()
        {
            if (!prepareRuntimeTask.IsCompleted)
            {
                return;
            }

            PreparedRuntime = prepareRuntimeTask.GetAwaiter().GetResult();
            CompleteCurrentStage();
            IsReadyForActivation = true;
            phase = Phase.Ready;
        }
    }
}
