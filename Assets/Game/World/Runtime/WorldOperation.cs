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
        ChunkData = 8,
        Completed = 9,
        Failed = 10
    }

    public readonly struct WorldOperationProgress
    {
        public WorldOperationProgress(
            WorldOperationKind kind,
            WorldOperationStage stage,
            int completedStageCount,
            int stageCount,
            int completedWorkCount,
            int workCount,
            bool isRunning)
        {
            Kind = kind;
            Stage = stage;
            CompletedStageCount = completedStageCount;
            StageCount = stageCount;
            CompletedWorkCount = completedWorkCount;
            WorkCount = workCount;
            IsRunning = isRunning;
        }

        public WorldOperationKind Kind { get; }
        public WorldOperationStage Stage { get; }
        public int CompletedStageCount { get; }
        public int StageCount { get; }
        public int CompletedWorkCount { get; }
        public int WorkCount { get; }
        public bool IsRunning { get; }
    }

    internal abstract class WorldOperation : IDisposable
    {
        private int completedStageCount;
        private bool progressChanged;
        private bool presentationStageStarted;

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
                0,
                0,
                isRunning: false);
        }

        public WorldOperationKind Kind { get; }
        public int StageCount { get; }
        public WorldOperationProgress Progress { get; private set; }
        public WorldRuntime PreparedRuntime { get; protected set; }
        public Exception Failure { get; private set; }
        public bool IsReadyForActivation { get; protected set; }
        public bool IsActivated { get; private set; }
        public bool IsFailed => Failure != null;
        public bool IsPresentationStageStarted => presentationStageStarted;

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

        public void BeginPresentationStage(WorldOperationStage stage)
        {
            if (!IsReadyForActivation || IsFailed || IsPresentationStageStarted)
            {
                throw new InvalidOperationException(
                    "The operation is not ready for presentation.");
            }

            if (stage != WorldOperationStage.Mesh
                && stage != WorldOperationStage.ChunkData)
            {
                throw new ArgumentOutOfRangeException(nameof(stage));
            }

            presentationStageStarted = true;
            BeginStage(stage);
        }

        public void ReportChunkDataProgress(
            int completedWorkCount,
            int workCount)
        {
            if (!IsPresentationStageStarted || IsFailed)
            {
                throw new InvalidOperationException(
                    "Only an active presentation-stage operation can report Chunk data generation.");
            }

            if (workCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(workCount));
            }

            if (completedWorkCount < 0 || completedWorkCount > workCount)
            {
                throw new ArgumentOutOfRangeException(nameof(completedWorkCount));
            }

            if (Progress.Stage == WorldOperationStage.ChunkData
                && Progress.CompletedWorkCount == completedWorkCount
                && Progress.WorkCount == workCount)
            {
                return;
            }

            Progress = new WorldOperationProgress(
                Kind,
                WorldOperationStage.ChunkData,
                completedStageCount,
                StageCount,
                completedWorkCount,
                workCount,
                isRunning: true);
            progressChanged = true;
        }

        public void Complete()
        {
            if (!IsPresentationStageStarted || IsFailed)
            {
                throw new InvalidOperationException(
                    "Only a presentation-stage operation can complete.");
            }

            completedStageCount = StageCount;
            Progress = new WorldOperationProgress(
                Kind,
                WorldOperationStage.Completed,
                completedStageCount,
                StageCount,
                0,
                0,
                isRunning: false);
            progressChanged = true;
        }

        public void MarkActivated()
        {
            if (!IsPresentationStageStarted || IsFailed || IsActivated)
            {
                throw new InvalidOperationException(
                    "Only a ready presentation-stage operation can activate its runtime.");
            }

            IsActivated = true;
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
                0,
                0,
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
                0,
                0,
                isRunning: false);
            progressChanged = true;
        }

        public abstract void Dispose();
    }
}
