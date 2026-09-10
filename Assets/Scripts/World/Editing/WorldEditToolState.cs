using System;
using MiniCivilization.World.Definitions;
using MiniCivilization.World.Domain;
using UnityEngine;

namespace MiniCivilization.World.Editing
{
    public enum WorldEditMode : byte
    {
        None = 0,
        Single = 1,
        Area = 2,
        Brush = 3
    }

    public enum WorldEditCellSelectionPolicy : byte
    {
        SurfaceCell = 0,
        EntityPlacementCell = 1
    }

    public enum WorldEditPropertyGroup : byte
    {
        None,
        Terrain,
        Water,
        Road
    }

    public enum TerrainEditOperation : byte
    {
        Raise,
        Lower,
        Add,
        Remove
    }

    public readonly struct WorldEditAction : IEquatable<WorldEditAction>
    {
        public readonly WorldEditPropertyGroup PropertyGroup;
        public readonly TerrainEditOperation TerrainOperation;
        public readonly RoadType RoadType;

        public bool IsSupported =>
            PropertyGroup == WorldEditPropertyGroup.Terrain
            || PropertyGroup == WorldEditPropertyGroup.Road;

        private WorldEditAction(
            WorldEditPropertyGroup propertyGroup,
            TerrainEditOperation terrainOperation,
            RoadType roadType)
        {
            PropertyGroup = propertyGroup;
            TerrainOperation = terrainOperation;
            RoadType = roadType;
        }

        public static WorldEditAction Terrain(TerrainEditOperation operation) =>
            new(
                WorldEditPropertyGroup.Terrain,
                operation,
                default);

        public static WorldEditAction SetRoad(RoadType roadType) =>
            new(
                WorldEditPropertyGroup.Road,
                default,
                roadType);

        public bool Equals(WorldEditAction other) =>
            PropertyGroup == other.PropertyGroup
            && TerrainOperation == other.TerrainOperation
            && RoadType == other.RoadType;

        public override bool Equals(object obj) =>
            obj is WorldEditAction other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(
                (byte)PropertyGroup,
                (byte)TerrainOperation,
                (ushort)RoadType);
    }

    public readonly struct WorldEditToolSnapshot :
        IEquatable<WorldEditToolSnapshot>
    {
        public readonly WorldEditMode Mode;
        public readonly WorldEditAction Action;
        public readonly EntityDefinition EntityDefinition;
        public readonly int BrushSize;

        public WorldEditPropertyGroup PropertyGroup => Action.PropertyGroup;
        public WorldEditCellSelectionPolicy CellSelectionPolicy =>
            IsEntityTool
                ? WorldEditCellSelectionPolicy.EntityPlacementCell
                : WorldEditCellSelectionPolicy.SurfaceCell;
        public bool IsEntityTool => EntityDefinition != null;
        public bool HasActiveTool => Action.IsSupported || IsEntityTool;
        public bool IsReady =>
            HasActiveTool && Mode != WorldEditMode.None;
        public bool CapturesPointer => IsReady;

        public WorldEditToolSnapshot(
            WorldEditMode mode,
            WorldEditAction action,
            EntityDefinition entityDefinition,
            int brushSize = 1)
        {
            Mode = mode;
            Action = action;
            EntityDefinition = entityDefinition;
            BrushSize = Math.Clamp(brushSize, 1, 3);
        }

        public bool Equals(WorldEditToolSnapshot other)
        {
            return Mode == other.Mode
                && Action.Equals(other.Action)
                && ReferenceEquals(EntityDefinition, other.EntityDefinition)
                && BrushSize == other.BrushSize;
        }

        public override bool Equals(object obj) =>
            obj is WorldEditToolSnapshot other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(
                (byte)Mode,
                Action,
                EntityDefinition,
                BrushSize);
    }

    [DisallowMultipleComponent]
    public sealed class WorldEditToolState : MonoBehaviour
    {
        private WorldEditToolSnapshot current = new(WorldEditMode.Area, default, null, 1);
        public WorldEditToolSnapshot Current => current;
        public WorldEditMode Mode => current.Mode;
        public WorldEditAction Action => current.Action;
        public EntityDefinition EntityDefinition => current.EntityDefinition;
        public int BrushSize => current.BrushSize;
        public bool CapturesPointer => current.CapturesPointer;
        public bool IsToolReady => current.IsReady;
        public bool BlocksCellSelection => current.IsReady;
        public event Action<WorldEditToolSnapshot> StateChanged;

        public void SelectAction(WorldEditAction action) =>
            Set(new WorldEditToolSnapshot(current.Mode, action, null, current.BrushSize));

        public void SelectEntity(EntityDefinition definition) =>
            Set(new WorldEditToolSnapshot(current.Mode, default, definition, current.BrushSize));

        public void ClearActiveTool() =>
            Set(new WorldEditToolSnapshot(current.Mode, default, null, current.BrushSize));

        public void SelectMode(WorldEditMode mode)
        {
            if (mode != WorldEditMode.Single && mode != WorldEditMode.Area && mode != WorldEditMode.Brush)
                return;
            Set(new WorldEditToolSnapshot(mode, current.Action, current.EntityDefinition, current.BrushSize));
        }

        public void SelectBrushSize(int size) =>
            Set(new WorldEditToolSnapshot(current.Mode, current.Action, current.EntityDefinition, size));

        private void Set(WorldEditToolSnapshot next)
        {
            if (next.EntityDefinition != null && next.EntityDefinition.TypeKey.Category == EntityCategory.Building)
                next = new WorldEditToolSnapshot(WorldEditMode.Single, next.Action, next.EntityDefinition, next.BrushSize);
            if (current.Equals(next)) return;
            current = next;
            StateChanged?.Invoke(current);
        }
    }
}
