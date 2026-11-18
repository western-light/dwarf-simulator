using UnityEngine;

namespace FIMSpace.GroundFitter
{
    public class FGroundFitter_Input : FGroundFitter_InputBase
    {
        protected virtual void Update()
        {

            if (FUVH.IsKeyPressedThisFrame(KeyCode.Space)) TriggerJump();

            Vector3 dir = Vector3.zero;

            if (FUVH.IsKeyPressed(KeyCode.W) || FUVH.IsKeyPressed(KeyCode.A) || FUVH.IsKeyPressed(KeyCode.S) || FUVH.IsKeyPressed(KeyCode.D))
            {
                if (FUVH.IsKeyPressed(KeyCode.LeftShift)) Sprint = true; else Sprint = false;

                if (FUVH.IsKeyPressed(KeyCode.W)) dir.z += 1f;
                if (FUVH.IsKeyPressed(KeyCode.A)) dir.x -= 1f;
                if (FUVH.IsKeyPressed(KeyCode.D)) dir.x += 1f;
                if (FUVH.IsKeyPressed(KeyCode.S)) dir.z -= 1f;

                dir.Normalize();

                RotationOffset = Quaternion.LookRotation(dir).eulerAngles.y;

                MoveVector = Vector3.forward;
            }
            else
            {
                Sprint = false;
                MoveVector = Vector3.zero;
            }

            if (FUVH.IsKeyPressed(KeyCode.X)) MoveVector -= Vector3.forward;
            if (FUVH.IsKeyPressed(KeyCode.Q)) MoveVector += Vector3.left;
            if (FUVH.IsKeyPressed(KeyCode.E)) MoveVector += Vector3.right;

            MoveVector.Normalize();

            controller.Sprint = Sprint;
            controller.MoveVector = MoveVector;
            controller.RotationOffset = RotationOffset;
        }
    }
}