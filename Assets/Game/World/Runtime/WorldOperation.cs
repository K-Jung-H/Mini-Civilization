using System;

namespace MiniCivilization.World.Runtime
{
    public enum WorldOperationKind : byte
    {
        None = 0,
        Generate = 1,
        Load = 2
    }

    public enum WorldOperationStage : byte
    {
        None = 0,
        Terrain = 1,
        WaterFeatures = 2,
        Biome = 3,
        BuildWorldData = 4,
        PrepareRuntime = 5,
        ReadSave = 6,
        Mesh = 7,
        Completed = 8,
        Failed = 9
    }

    public readonly struct WorldOperationProgress
    {
        public WorldOperationProgress(
            WorldOperationKind kind,
            WorldOperationStage stage,
            int completedStageCount,
            int stageCount,
            bool isRunning)
        {
            Kind = kind;
            Stage = stage;
            CompletedStageCount = completedStageCount;
            StageCount = stageCount;
            IsRunning = isRunning;
        }

        public WorldOperationKind Kind { get; }
        public WorldOperationStage Stage { get; }
        public int CompletedStageCount { get; }
        public int StageCount { get; }
        public bool IsRunning { get; }
    }

    internal abstract class WorldOperation : IDisposable
    {
        private int completedStageCount;
        private bool progressChanged;

        protected WorldOperation(WorldOperationKind kind, int stageCount)
        {
            if (stageCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(stageCount));
            }

            Kind = kind;
            StageCount = stageCount;
            Progress = new WorldOperationProgress(
                kind,
                WorldOperationStage.None,
                0,
                stageCount,
                isRunning: false);
        }

        public WorldOperationKind Kind { get; }
        public int StageCount { get; }
        public WorldOperationProgress Progress { get; private set; }
        public WorldRuntime PreparedRuntime { get; protected set; }
        public Exception Failure { get; private set; }
        public bool IsReadyForActivation { get; protected set; }
        public bool IsFailed => Failure != null;
        public bool IsMeshStageStarted => Progress.Stage == WorldOperationStage.Mesh;

        public abstract void Update();

        public bool TryConsumeProgressChange(out WorldOperationProgress value)
        {
            value = Progress;
            if (!progressChanged)
            {
                return false;
            }

            progressChanged = false;
            return true;
        }

        public void BeginMeshStage()
        {
            if (!IsReadyForActivation || IsFailed || IsMeshStageStarted)
            {
                throw new InvalidOperationException(
                    "The operation is not ready to build meshes.");
            }

            BeginStage(WorldOperationStage.Mesh);
        }

        public void Complete()
        {
            if (!IsMeshStageStarted || IsFailed)
            {
                throw new InvalidOperationException(
                    "Only a mesh-stage operation can complete.");
            }

            completedStageCount = StageCount;
            Progress = new WorldOperationProgress(
                Kind,
                WorldOperationStage.Completed,
                completedStageCount,
                StageCount,
                isRunning: false);
            progressChanged = true;
        }

        internal void FailBeforeActivation(Exception exception)
        {
            Fail(exception);
        }

        protected void BeginStage(WorldOperationStage stage)
        {
            Progress = new WorldOperationProgress(
                Kind,
                stage,
                completedStageCount,
                StageCount,
                isRunning: true);
            progressChanged = true;
        }

        protected void CompleteCurrentStage()
        {
            completedStageCount = Math.Min(
                completedStageCount + 1,
                StageCount);
        }

        protected void Fail(Exception exception)
        {
            Failure = exception ?? new InvalidOperationException(
                "The world operation failed without an exception.");
            Progress = new WorldOperationProgress(
                Kind,
                WorldOperationStage.Failed,
                completedStageCount,
                StageCount,
                isRunning: false);
            progressChanged = true;
        }

        public abstract void Dispose();
    }
}
