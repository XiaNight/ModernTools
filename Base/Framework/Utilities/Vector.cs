namespace Base.Framework.Utilities;
public struct Vector2
{
    public float x;
    public float y;
    public Vector2(float x, float y)
    {
        this.x = x;
        this.y = y;
    }
    public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.x + b.x, a.y + b.y);
    public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.x - b.x, a.y - b.y);
    public static Vector2 operator *(Vector2 a, float scalar) => new(a.x * scalar, a.y * scalar);
    public static Vector2 operator /(Vector2 a, float scalar) => new(a.x / scalar, a.y / scalar);
    public static Vector2 operator -(Vector2 a) => new(-a.x, -a.y);
    public override string ToString() => $"({x}, {y})";

    public static Vector2 Zero => new(0, 0);
    public static Vector2 One => new(1, 1);
    public static Vector2 Up => new(0, 1);
    public static Vector2 Right => new(1, 0);
}

public struct Vector3
{
    public float x;
    public float y;
    public float z;
    public Vector3(float x, float y, float z)
    {
        this.x = x;
        this.y = y;
        this.z = z;
    }
    public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
    public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
    public static Vector3 operator *(Vector3 a, float scalar) => new(a.x * scalar, a.y * scalar, a.z * scalar);
    public static Vector3 operator /(Vector3 a, float scalar) => new(a.x / scalar, a.y / scalar, a.z / scalar);
    public static Vector3 operator -(Vector3 a) => new(-a.x, -a.y, -a.z);
    public override string ToString() => $"({x}, {y}, {z})";

    public static Vector3 Zero => new(0, 0, 0);
    public static Vector3 One => new(1, 1, 1);
    public static Vector3 Up => new(0, 1, 0);
    public static Vector3 Right => new(1, 0, 0);
    public static Vector3 Forward => new(0, 0, 1);
}

public struct Vector2Int
{
    public int x;
    public int y;
    public Vector2Int(int x, int y)
    {
        this.x = x;
        this.y = y;
    }
    public static Vector2Int operator +(Vector2Int a, Vector2Int b) => new(a.x + b.x, a.y + b.y);
    public static Vector2Int operator -(Vector2Int a, Vector2Int b) => new(a.x - b.x, a.y - b.y);
    public static Vector2Int operator *(Vector2Int a, int scalar) => new(a.x * scalar, a.y * scalar);
    public static Vector2Int operator /(Vector2Int a, int scalar) => new(a.x / scalar, a.y / scalar);
    public override string ToString() => $"({x}, {y})";

    public static Vector2Int Zero => new(0, 0);
    public static Vector2Int One => new(1, 1);
    public static Vector2Int Up => new(0, 1);
    public static Vector2Int Right => new(1, 0);
}