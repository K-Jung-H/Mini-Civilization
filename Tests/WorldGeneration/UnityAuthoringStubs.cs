// Only authoring attributes/base class are replaced. Terrain math is linked production code.
namespace UnityEngine
{
    public class ScriptableObject { }
    public sealed class SerializeField : System.Attribute { }
    public sealed class CreateAssetMenuAttribute : System.Attribute
    {
        public string fileName;
        public string menuName;
    }
}
namespace UnityEngine
{
    public sealed class HeaderAttribute : System.Attribute { public HeaderAttribute(string value) { } }
    public sealed class MinAttribute : System.Attribute { public MinAttribute(float value) { } }
    public sealed class RangeAttribute : System.Attribute { public RangeAttribute(float min, float max) { } }
}
namespace UnityEngine.Serialization
{
    public sealed class FormerlySerializedAsAttribute : System.Attribute { public FormerlySerializedAsAttribute(string value) { } }
}
