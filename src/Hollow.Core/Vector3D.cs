using System;
using System.Globalization;

namespace Hollow.Core;

/// <summary>A 3D vector in double precision. Used for galactic (light-years) and system (metres) positions.</summary>
public readonly struct Vector3D : IEquatable<Vector3D>
{
    public readonly double X;
    public readonly double Y;
    public readonly double Z;

    public Vector3D(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static Vector3D Zero => default;
    public static Vector3D UnitX => new(1, 0, 0);
    public static Vector3D UnitY => new(0, 1, 0);
    public static Vector3D UnitZ => new(0, 0, 1);

    public double LengthSquared => X * X + Y * Y + Z * Z;
    public double Length => Math.Sqrt(LengthSquared);

    public Vector3D Normalized
    {
        get
        {
            double l = Length;
            return l > 0 ? this / l : Zero;
        }
    }

    public static Vector3D operator +(Vector3D a, Vector3D b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vector3D operator -(Vector3D a, Vector3D b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vector3D operator -(Vector3D a) => new(-a.X, -a.Y, -a.Z);
    public static Vector3D operator *(Vector3D a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vector3D operator *(double s, Vector3D a) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vector3D operator /(Vector3D a, double s) => new(a.X / s, a.Y / s, a.Z / s);
    public static bool operator ==(Vector3D a, Vector3D b) => a.Equals(b);
    public static bool operator !=(Vector3D a, Vector3D b) => !a.Equals(b);

    public static double Dot(Vector3D a, Vector3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static Vector3D Cross(Vector3D a, Vector3D b) =>
        new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    public static double Distance(Vector3D a, Vector3D b) => (a - b).Length;

    public static Vector3D Lerp(Vector3D a, Vector3D b, double t) => a + (b - a) * t;

    public bool Equals(Vector3D other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

    public override bool Equals(object? obj) => obj is Vector3D v && Equals(v);

    public override int GetHashCode() => HashCode.Combine(X, Y, Z);

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"({X:0.###}, {Y:0.###}, {Z:0.###})");
}
