using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.Intrinsics;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Utility;

namespace Content.Shared._Oxyd.Framework;

public class SharedOxydHelpers
{
    public static float[] getIntersectionCheckConstants(Box2Rotated b)
    {
        var list = new float[8];
        list[0] = b.BottomRight.X - b.TopRight.X;
        list[1] = b.BottomRight.Y - b.TopRight.Y;
        list[2] = b.TopRight.X - b.TopLeft.X;
        list[3] = b.TopRight.Y - b.TopLeft.Y;
        list[4] = b.TopLeft.X - b.BottomLeft.X;
        list[5] = b.TopLeft.Y - b.BottomLeft.Y;
        list[6] = b.BottomLeft.X - b.BottomRight.X;
        list[7] = b.BottomLeft.Y - b.BottomRight.Y;
        return list;
    }
    public static bool checkIntersect(Vector2 p, Box2Rotated b)
    {
        var lp1 = b.BottomRight;
        var lp2 = b.TopRight;
        var c1 = (lp1.X - lp2.X)*(p.Y - lp2.Y) - (lp1.Y - lp2.Y)*(p.X - lp2.X);
        var c2 = c1;
        lp1 = lp2;
        lp2 = b.TopLeft;
        c2 = (lp1.X - lp2.X)*(p.Y - lp2.Y) - (lp1.Y - lp2.Y)*(p.X - lp2.X);
        if(float.Sign(c1) != float.Sign(c2))
            return false;
        c1 = c2;
        lp1 = lp2;
        lp2 = b.BottomLeft;
        c2 = (lp1.X - lp2.X)*(p.Y - lp2.Y) - (lp1.Y - lp2.Y)*(p.X - lp2.X);
        if(float.Sign(c1) != float.Sign(c2))
            return false;
        c1 = c2;
        lp1 = lp2;
        lp2 = b.BottomRight;
        c2 = (lp1.X - lp2.X)*(p.Y - lp2.Y) - (lp1.Y - lp2.Y)*(p.X - lp2.X);
        if(float.Sign(c1) != float.Sign(c2))
            return false;
        return true;
    }
    // use in loops where the box2rot is the same
    public static bool checkIntersect(Vector2 p, Box2Rotated b, float[] constants)
    {
        var lp = b.TopRight;
        var c1 = constants[0]*(p.Y - lp.Y) - constants[1]*(p.X - lp.X);
        var c2 = c1;
        lp = b.TopLeft;
        c2 = constants[2]*(p.Y - lp.Y) - constants[3]*(p.X - lp.X);
        if(float.Sign(c1) != float.Sign(c2))
            return false;
        c1 = c2;
        lp = b.BottomLeft;
        c2 = constants[4]*(p.Y - lp.Y) - constants[5]*(p.X - lp.X);
        if(float.Sign(c1) != float.Sign(c2))
            return false;
        c1 = c2;
        lp = b.BottomRight;
        c2 = constants[6]*(p.Y - lp.Y) - constants[7]*(p.X - lp.X);
        if(float.Sign(c1) != float.Sign(c2))
            return false;
        return true;
    }
}
