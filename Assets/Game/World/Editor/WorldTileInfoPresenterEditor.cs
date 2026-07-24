using MiniCivilization.World.Interaction;
using UnityEditor;

namespace MiniCivilization.World.Editor
{
    [CustomEditor(typeof(WorldTileInfoPresenter))]
    public sealed class WorldTileInfoPresenterEditor : UnityEditor.Editor
    {
        public override bool RequiresConstantRepaint() => true;
    }
}
