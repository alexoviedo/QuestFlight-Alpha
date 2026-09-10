using NUnit.Framework;
using QuestFlightLab.Environment;
using UnityEngine;

public sealed class TreeDrawCullingTests
{
    [Test]
    public void OffscreenBoundsAreRejectedButBoundaryIsRetained()
    {
        var plane=new[]{new Plane(Vector3.right,0)};
        var bounds=new Bounds(Vector3.zero,Vector3.one*2);
        Assert.IsFalse(AirportCloseTreeLodRenderer.IsTreeBoundsInsidePlanes(Matrix4x4.Translate(new Vector3(-10,0,0)),bounds,plane));
        Assert.IsTrue(AirportCloseTreeLodRenderer.IsTreeBoundsInsidePlanes(Matrix4x4.Translate(new Vector3(-1,0,0)),bounds,plane));
    }

    [Test]
    public void OffsetRotatedNonuniformMeshesCannotLoseVisibleCorners()
    {
        var bounds=new Bounds(new Vector3(-.1f,.67f,-.2f),new Vector3(1.1f,1.34f,1.42f));
        for(int yaw=0;yaw<360;yaw+=13)
        for(int x=-30;x<=30;x+=2)
        {
            var matrix=Matrix4x4.TRS(new Vector3(x,-10,0),Quaternion.Euler(0,yaw,0),new Vector3(13,22,9));
            var plane=new Plane(new Vector3(1,.2f,.3f).normalized,0);
            bool visibleCorner=false;
            for(int corner=0;corner<8;corner++)
            {
                Vector3 local=bounds.center+Vector3.Scale(bounds.extents,new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1));
                visibleCorner|=plane.GetDistanceToPoint(matrix.MultiplyPoint3x4(local))>=0;
            }
            if(visibleCorner)Assert.IsTrue(AirportCloseTreeLodRenderer.IsTreeBoundsInsidePlanes(matrix,bounds,new[]{plane}));
        }
    }

    [Test]
    public void EitherEyeVisibilityMustRetainInstance()
    {
        var mesh=new Bounds(Vector3.zero,Vector3.one*.01f);
        var matrix=Matrix4x4.identity;
        var left=new[]{new Plane(Vector3.right,-1)};
        var right=new[]{new Plane(Vector3.right,1)};
        Assert.IsFalse(AirportCloseTreeLodRenderer.IsTreeBoundsInsidePlanes(matrix,mesh,left));
        Assert.IsTrue(AirportCloseTreeLodRenderer.IsTreeBoundsInsidePlanes(matrix,mesh,left) ||
            AirportCloseTreeLodRenderer.IsTreeBoundsInsidePlanes(matrix,mesh,right));
    }
}
