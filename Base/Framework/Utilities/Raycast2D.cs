using System.Numerics;

namespace Base.Mathf
{
    public static class Raycast2D
    {
        // Returns the cast distance t along ray1 (a + t*b).
        // If rays don't intersect in the forward direction, returns +Infinity or -Infinity.
        public static bool RayIntersection(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out float distance)
        {
            float cross = Cross(b, d);

            // Parallel case
            if (Math.Abs(cross) < 1e-6f)
            {
                distance = 0;
                return false;
            }

            Vector2 ac = c - a;
            float t = Cross(ac, d) / cross;
            float u = Cross(ac, b) / cross;

            // Valid intersection
            if (t >= 0f && u >= 0f)
            {
                distance = t;
                return true;
            }

            // None intersect in forward direction
            distance = 0;
            return false;
        }

        // Ray1:  (0,0) + t*b,  t >= 0
        // Ray2:  c + u*d,      u >= 0
        // Returns t, or ±Infinity based on Vector2.Dot(b,d) when no forward intersection.
        public static bool RayIntersectionFromOrigin(Vector2 b, Vector2 c, Vector2 d, out float distance)
        {
            float cross = Cross(b, d);

            // Parallel case
            if (Math.Abs(cross) < 1e-6f)
            {
                distance = 0;
                return false;
            }

            float t = Cross(c, d) / cross;   // (c - 0) × d / (b × d)
            float u = Cross(c, b) / cross;   // (c - 0) × b / (b × d)

            // Valid intersection
            if (t >= 0f && u >= 0f)
            {
                distance = t;
                return true;
            }

            // None intersect in forward direction
            distance = 0;
            return false;
        }

        private static float Cross(Vector2 v1, Vector2 v2)
        {
            return v1.X * v2.Y - v1.Y * v2.X;
        }
    }
}
