using System;
using System.IO;
using System.Threading.Tasks;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Runtime;

namespace MiniCivilization.World.Persistence
{
    internal sealed class WorldLoadOperation : WorldOperation
    {
        private enum Phase : byte
        {
            NotStarted,
            ReadSave,
            PrepareRuntime,
            Ready
        }

        private readonly string path;
        private Phase phase;
        private Task<WorldData> readTask;
        private Task<WorldRuntime> prepareRuntimeTask;

        public WorldLoadOperation(string path)
            : base(WorldOperationKind.Load, stageCount: 3)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("World save path is empty.", nameof(path));
            }

            this.path = System.IO.Path.GetFullPath(path);
        }

        public string Path => path;

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
                        BeginStage(WorldOperationStage.ReadSave);
                        readTask = Task.Run(ReadWorld);
                        phase = Phase.ReadSave;
                        break;
                    case Phase.ReadSave:
                        FinishRead();
                        break;
                    case Phase.PrepareRuntime:
                        FinishPrepareRuntime();
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

        private WorldData ReadWorld()
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            return WorldSaveCodec.Read(stream);
        }

        private void FinishRead()
        {
            if (!readTask.IsCompleted)
            {
                return;
            }

            var world = readTask.GetAwaiter().GetResult();
            CompleteCurrentStage();
            BeginStage(WorldOperationStage.PrepareRuntime);
            prepareRuntimeTask = Task.Run(() => WorldRuntime.CreatePrepared(world));
            phase = Phase.PrepareRuntime;
        }

        private void FinishPrepareRuntime()
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
