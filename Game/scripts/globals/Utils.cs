using Godot;
using Godot.Collections;

public struct TestMotionResults
{
    public bool hit;
    public float safeMovement;
    public float unsafeMovement;
    public Vector3 newShapePos;
}

public class TestMultiMotionResults
{
    public float safeMovement = 1.0f;
    public float unsafeMovement = 1.0f;
    public Array<Dictionary> hitResults = new Array<Dictionary>();
}

public class Utils : Node
{
    public static bool TestIntersection(PhysicsDirectSpaceState space, Shape collider, Vector3 pos, ref Dictionary hitResult, Array ignoreList = null)
    {
        PhysicsShapeQueryParameters query = new PhysicsShapeQueryParameters();
        query.SetShape(collider);
        if (ignoreList != null) query.Exclude = ignoreList;
        query.Transform = new Transform(Basis.Identity, pos);   // TODO: Proper basis.

        Dictionary res = space.GetRestInfo(query);
        if (hitResult != null) hitResult = res;

        return res.Count > 0;
    }

    public static Array<Dictionary> TestMultiIntersection(PhysicsDirectSpaceState space, Shape collider, Vector3 pos, Array ignoreList = null)
    {
        Array<Dictionary> results = new Array<Dictionary>();
        PhysicsShapeQueryParameters query = new PhysicsShapeQueryParameters();
        query.SetShape(collider);
        if (ignoreList != null) query.Exclude = ignoreList;
        query.Transform = new Transform(Basis.Identity, pos);   // TODO: Proper basis.
        Array intersectRes = space.IntersectShape(query);

        for (int i = 0; i < intersectRes.Count; i++)
        {
            Dictionary hitResults = space.GetRestInfo(query);
            if (hitResults.Count > 0)
            {
                results.Add(hitResults);
                query.Exclude.Add(hitResults["rid"]);
            }
        }

        return results;
    }

    public static bool TestMotion(PhysicsDirectSpaceState space, Shape collider, Vector3 startPos, Vector3 move, ref Array results, Array ignoreList = null)
    {
        PhysicsShapeQueryParameters query = new PhysicsShapeQueryParameters();
        query.SetShape(collider);
        if (ignoreList != null) query.Exclude = ignoreList;
        query.Transform = new Transform(Basis.Identity, startPos);
        Array motionResult = space.CastMotion(query, move);
        if (results != null) { results = motionResult; }
        return ((float)motionResult[0] == 1.0f) && ((float)motionResult[1] == 1.0f);
    }

    public static TestMultiMotionResults TestMultiMotion(PhysicsDirectSpaceState space, Shape collider, Vector3 startPos, Vector3 move, Array ignoreList = null)
    {
        TestMultiMotionResults results = new TestMultiMotionResults();
        Array motionResults = new Array();
        TestMotion(space, collider, startPos, move, ref motionResults, ignoreList);
        if (motionResults.Count == 0) return results;
        results.safeMovement = (float)motionResults[0];
        results.unsafeMovement = (float)motionResults[1];
        results.hitResults = TestMultiIntersection(space, collider, startPos + (move.Normalized() * move.Length() * (float)motionResults[0]), ignoreList);
        return results;
    }

    public static float GetVec3Angle(Vector3 first, Vector3 second)
    {
        // TODO: Is this right?
        return 90 - Mathf.Rad2Deg(first.Dot(second) / (first.Length() * second.Length()));
    }

    public static Vector3 ScaleVector(Vector3 vec, float scale)
    {
        return vec.Normalized() * (vec.Length() * scale);
    }
}
