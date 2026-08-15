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
        Biome,
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

    public enum RoadEditOperation : byte
    {
        Place,
        Remove
    }

    public readonly struct WorldEditAction : IEquatable<WorldEditAction>
    {
        public readonly WorldEditPropertyGroup PropertyGroup;
        public readonly TerrainEditOperation TerrainOperation;
        public readonly BiomeType Biome;
        public readonly RoadEditOperation RoadOperation;

        public bool IsSupported =>
            PropertyGroup == WorldEditPropertyGroup.Terrain
            || PropertyGroup == WorldEditPropertyGroup.Biome
            || PropertyGroup == WorldEditPropertyGroup.Road;

        private WorldEditAction(
            WorldEditPropertyGroup propertyGroup,
            TerrainEditOperation terrainOperation,
            BiomeType biome,
            RoadEditOperation roadOperation)
        {
            PropertyGroup = propertyGroup;
            TerrainOperation = terrainOperation;
            Biome = biome;
            RoadOperation = roadOperation;
        }

        public static WorldEditAction Terrain(TerrainEditOperation operation) =>
            new(
                WorldEditPropertyGroup.Terrain,
                operation,
                BiomeType.None,
                default);

        public static WorldEditAction SetBiome(BiomeType biome) =>
            new(
                WorldEditPropertyGroup.Biome,
                default,
                biome,
                default);

        public static WorldEditAction Road(RoadEditOperation operation) =>
            new(
                WorldEditPropertyGroup.Road,
                default,
                BiomeType.None,
                operation);

        public bool Equals(WorldEditAction other) =>
            PropertyGroup == other.PropertyGroup
            && TerrainOperation == other.TerrainOperation
            && Biome == other.Biome
            && RoadOperation == other.RoadOperation;

        public override bool Equals(object obj) =>
            obj is WorldEditAction other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(
                (byte)PropertyGroup,
                (byte)TerrainOperation,
                (ushort)Biome,
                (byte)RoadOperation);
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
        public bool CapturesPointer => Mode != WorldEditMode.None;
        public bool IsEntityTool => EntityDefinition != null;
        public bool IsReady =>
            CapturesPointer
            && (Action.IsSupported || EntityDefinition != null);

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
        private WorldEditToolbarView toolbarView;
        private WorldEntityCatalogView catalogView;
        private WorldEditToolSnapshot current;
        private bool isSubscribed;
        private bool isSynchronizingSelection;

        public WorldEditToolSnapshot Current => current;
        public WorldEditMode Mode => current.Mode;
        public WorldEditAction Action => current.Action;
        public EntityDefinition EntityDefinition => current.EntityDefinition;
        public int BrushSize => current.BrushSize;
        public bool CapturesPointer => current.CapturesPointer;
        public bool IsToolReady => current.IsReady;
        public bool BlocksCellSelection =>
            toolbarView != null && toolbarView.IsExpanded;

        public event Action<WorldEditToolSnapshot> StateChanged;

        private void OnEnable()
        {
            Subscribe();
            SynchronizeEntityToolAvailability();
            Refresh();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        public void Configure(
            WorldEditToolbarView toolbar,
            WorldEntityCatalogView catalog = null)
        {
            Unsubscribe();
            toolbarView = toolbar;
            catalogView = catalog;
            Subscribe();
            SynchronizeEntityToolAvailability();
            Refresh();
        }

        private void Subscribe()
        {
            if (isSubscribed || toolbarView == null)
            {
                return;
            }

            toolbarView.SelectionChanged += Refresh;
            toolbarView.EditActionSelected += OnEditActionSelected;
            toolbarView.PropertyCategorySelected += OnPropertyCategorySelected;
            if (catalogView != null)
            {
                catalogView.ActiveCategoryChanged += OnActiveCategoryChanged;
                catalogView.DefinitionSelected += OnDefinitionSelected;
            }

            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed)
            {
                return;
            }

            if (toolbarView != null)
            {
                toolbarView.SelectionChanged -= Refresh;
                toolbarView.EditActionSelected -= OnEditActionSelected;
                toolbarView.PropertyCategorySelected -=
                    OnPropertyCategorySelected;
            }

            if (catalogView != null)
            {
                catalogView.ActiveCategoryChanged -= OnActiveCategoryChanged;
                catalogView.DefinitionSelected -= OnDefinitionSelected;
            }

            isSubscribed = false;
        }

        private void OnEditActionSelected(WorldEditAction _)
        {
            ClearEntitySelection();
            toolbarView?.EnsureSelectModeGroupExpanded();
            Refresh();
        }

        private void OnPropertyCategorySelected()
        {
            ClearEntitySelection();
            Refresh();
        }

        private void OnDefinitionSelected(EntityDefinition definition)
        {
            if (isSynchronizingSelection)
            {
                return;
            }

            toolbarView?.SetBuildingDefinitionSelected(
                IsBuildingDefinition(definition));
            if (definition != null)
            {
                isSynchronizingSelection = true;
                toolbarView?.ClearActiveEditAction();
                toolbarView?.EnsureSelectModeGroupExpanded();
                isSynchronizingSelection = false;
            }

            Refresh();
        }

        private void OnActiveCategoryChanged(EntityCategory? category)
        {
            if (category.HasValue)
            {
                isSynchronizingSelection = true;
                toolbarView?.ClearActiveEditAction();
                isSynchronizingSelection = false;
            }

            toolbarView?.SetBuildingDefinitionSelected(
                IsBuildingDefinition(catalogView?.SelectedDefinition));
            Refresh();
        }

        private void ClearEntitySelection()
        {
            if (catalogView == null || catalogView.SelectedDefinition == null)
            {
                toolbarView?.SetBuildingDefinitionSelected(false);
                return;
            }

            isSynchronizingSelection = true;
            catalogView.ClearSelectedDefinition();
            isSynchronizingSelection = false;
            toolbarView?.SetBuildingDefinitionSelected(false);
        }

        private void SynchronizeEntityToolAvailability()
        {
            toolbarView?.SetBuildingDefinitionSelected(
                IsBuildingDefinition(catalogView?.SelectedDefinition));
        }

        private bool IsBuildingDefinition(EntityDefinition definition) =>
            definition != null
            && catalogView?.Catalog != null
            && catalogView.Catalog.TryGetTypeKey(
                definition,
                out var typeKey)
            && typeKey.Category == EntityCategory.Building;

        private void Refresh()
        {
            var next = ReadToolbarState();
            if (current.Equals(next))
            {
                return;
            }

            current = next;
            StateChanged?.Invoke(current);
        }

        private WorldEditToolSnapshot ReadToolbarState()
        {
            if (toolbarView == null)
            {
                return default;
            }

            var mode = toolbarView.GetSelectedModeIndex() switch
            {
                1 => WorldEditMode.Single,
                2 => WorldEditMode.Area,
                3 => WorldEditMode.Brush,
                _ => WorldEditMode.None
            };
            var brushSize = toolbarView.GetSelectedBrushSize();
            if (toolbarView.TryGetSelectedEditAction(out var action))
            {
                return new WorldEditToolSnapshot(
                    mode,
                    action,
                    null,
                    brushSize);
            }

            var definition = toolbarView.IsEntityGroupExpanded
                ? catalogView?.SelectedDefinition
                : null;
            return new WorldEditToolSnapshot(
                mode,
                default,
                definition,
                brushSize);
        }
    }
}
