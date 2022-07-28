using Godot;
using Godot.Collections;

public struct TestMotionResults
{
    public bool hit;
    public float safeMovement;
    public float unsafeMovement;
    public Vector3 newShapePos;
}

public class Utils : Node
{
    public static TestMotionResults TestMotion(PhysicsDirectSpaceState space, Shape collider, Vector3 startPos, Vector3 move, Array ignoreList = null) 
    {
        PhysicsShapeQueryParameters query = new PhysicsShapeQueryParameters();
        query.SetShape(collider);
        query.Exclude = ignoreList;
        query.Transform = new Transform(Basis.Identity, startPos);
        var res = space.CastMotion(query, move);


        TestMotionResults results = new TestMotionResults();
        results.hit = (float)res[0] == 1.0f && (float)res[1] == 1.0f;
        results.safeMovement = (float)res[0];
        results.unsafeMovement = (float)res[1];
        results.newShapePos = (startPos) + (move.Normalized() * (move.Length() * (float)res[0]));
        return results;
    }


    public static bool TestMultiIntersection(PhysicsDirectSpaceState space, Shape collider, Vector3 pos, Array ignoreList = null, Array results = null)
    {
        PhysicsShapeQueryParameters query = new PhysicsShapeQueryParameters();
        query.SetShape(collider);
        query.Exclude = ignoreList;
        query.Transform = new Transform(Basis.Identity, pos);   // TODO: Proper basis.
        Array res = space.IntersectShape(query);

        if (results == null) { return (res.Count > 0) ? true : false; }
        for (int i = 0; i < res.Count; i++)
        {
            Dictionary hitResults = space.GetRestInfo(query);
            if (hitResults.Count > 0)
            {
                results.Add(hitResults);
                query.Exclude.Add(hitResults["rid"]);
            }
        }
        return (res.Count > 0) ? true : false;
    }

    public static bool TestIntersection(PhysicsDirectSpaceState space, Shape collider, Vector3 pos, out Dictionary hitResult, Array ignoreList = null)
    {
        PhysicsShapeQueryParameters query = new PhysicsShapeQueryParameters();
        query.SetShape(collider);
        query.Exclude = ignoreList;
        query.Transform = new Transform(Basis.Identity, pos);   // TODO: Proper basis.

        Dictionary res = space.GetRestInfo(query);
        hitResult = res;

        return res.Count > 0;
    }
}
