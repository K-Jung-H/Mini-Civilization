using UnityEngine;
namespace MiniCivilization.World.Generation.Patterns
{
    public static class ClimateMapColors
    {
        public static Color Temperature(float value) => value <= 0.5f
            ? Color.Lerp(Color.blue, Color.green, value * 2)
            : Color.Lerp(Color.green, Color.red, (value - 0.5f) * 2);
        public static Color Moisture(float value) => Color.Lerp(Color.white, Color.blue, value);
    }
}
