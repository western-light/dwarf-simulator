using UnityEngine;

namespace FIMSpace.FLook
{
    /// <summary>
    /// FC: In this partial class we implement feature of simple eyes motion
    /// </summary>
    public partial class FLookAnimator
    {
        public bool UseEyes = false;

        [Tooltip("Target on which eyes will look, set to null if target should be the same as for head target")]
        public Transform EyesTarget;
        [Space(4f)]

        [Tooltip("Eyes transforms / bones (origin should be in center of the sphere")]
        public Transform LeftEye;
        public bool InvertLeftEye = false;
        [Tooltip("Eyes transforms / bones (origin should be in center of the sphere")]
        public Transform RightEye;
        public bool InvertRightEye = false;
        [Tooltip("Look clamping reference rotation transform, mostly parent of eye objects. If nothing is assigned then algorithm will use 'Lead Bone' as reference.")]
        public Transform HeadReference;
        public Transform GetHeadReference() { if (HeadReference != null) return HeadReference; else return LeadBone; }

        public Vector3 EyesOffsetRotation;
        public Vector3 LeftEyeOffsetRotation = Vector3.zero;
        public Vector3 RightEyeOffsetRotation = Vector3.zero;

        [Tooltip("How fast eyes should follow target")]
        [Range(0f, 1f)]
        public float EyesSpeed = 0.5f;

        [FPD_Percentage(0f, 1f)]
        public float EyesBlend = 1f;

        [Tooltip("In what angle eyes should go back to deafult position")]
        [Range(0.0f, 180f)]
        public Vector2 EyesXRange = new Vector2(-60f, 60f);
        public Vector2 EyesYRange = new Vector2(-50f, 50f);

        [Tooltip("If your eyes don't have baked keyframes in animation this value should be enabled, otherwise eyes would go crazy")]
        public bool EyesNoKeyframes = true;

        /// <summary>To make implementation of 'Eyes Animator' more responsible</summary>
        public bool CustomEyesLogics = false;

        private float EyesOutOfRangeBlend = 1f;

        // If you are using also look animator, you can simply uncomment this line below and LateUpdate() line for this feature
        //public FLookAnimator UseLookAnimatorTarget = null;

        private Transform[] eyes;
        private Vector3[] eyeForwards;
        private Vector3[] eyeUps;
        private Quaternion[] eyesInitLocalRotations;
        private Quaternion[] eyesLerpRotations;

        private float _eyesBlend;
        private Vector3 headForward;
        private Quaternion eyesHeadReferenceBaseRotation = Quaternion.identity;
        private Vector3 eyesHeadUp;


        public Transform GetEyesTarget()
        {
            if (EyesTarget == null) return GetLookAtTransform(); else return EyesTarget;
        }

        [System.Obsolete("Now please use GetEyesTarget() or GetLookAtTransform() methods")]
        public Transform GetCurrentTarget()
        {
            return GetEyesTarget();
        }


        public Vector3 GetEyesTargetPosition()
        {
            if (EyesTarget == null) return GetLookAtPosition(); else return EyesTarget.position;
        }


        private void InitEyesModule()
        {
            eyes = new Transform[0];

            if (LeftEye != null || RightEye != null)
            {
                if (LeftEye != null && RightEye != null) eyes = new Transform[2] { LeftEye, RightEye }; else if (LeftEye != null) eyes = new Transform[1] { LeftEye }; else eyes = new Transform[1] { RightEye };
            }

            eyeForwards = new Vector3[eyes.Length];
            eyeUps = new Vector3[eyes.Length];
            eyesInitLocalRotations = new Quaternion[eyes.Length];
            eyesLerpRotations = new Quaternion[eyes.Length];

            Vector3 referenceForward = baseTransform.TransformDirection(ModelForwardAxis.normalized);
            Vector3 referenceUp = baseTransform.TransformDirection(ModelUpAxis.normalized);
            Vector3.OrthoNormalize(ref referenceForward, ref referenceUp);

            for (int i = 0; i < eyeForwards.Length; i++)
            {
                Vector3 eyeForward = eyes[i].InverseTransformDirection(referenceForward);
                Vector3 eyeUp = eyes[i].InverseTransformDirection(referenceUp);
                Vector3.OrthoNormalize(ref eyeForward, ref eyeUp);

                eyeForwards[i] = eyeForward;
                eyeUps[i] = eyeUp;
                eyesInitLocalRotations[i] = eyes[i].localRotation;
                eyesLerpRotations[i] = eyes[i].rotation;
            }

            Transform headReference = GetHeadReference();
            Vector3 headLocalForward = headReference.InverseTransformDirection(referenceForward);
            Vector3 headLocalUp = headReference.InverseTransformDirection(referenceUp);
            Vector3.OrthoNormalize(ref headLocalForward, ref headLocalUp);

            eyesHeadReferenceBaseRotation = Quaternion.LookRotation(headLocalForward, headLocalUp);
            eyesHeadUp = headLocalUp;
        }


        private Quaternion GetEyesLookRotation(Vector3 worldDirection)
        {
            Transform headReference = GetHeadReference();
            Quaternion referenceRotation = headReference.rotation * eyesHeadReferenceBaseRotation;

            if (worldDirection.sqrMagnitude < 0.000001f) return referenceRotation;

            Vector3 localDirection = Quaternion.Inverse(referenceRotation) * worldDirection.normalized;
            float horizontalMagnitude = Mathf.Sqrt(localDirection.x * localDirection.x + localDirection.z * localDirection.z);

            float pitch = -Mathf.Atan2(localDirection.y, horizontalMagnitude) * Mathf.Rad2Deg;
            float yaw = Mathf.Atan2(localDirection.x, localDirection.z) * Mathf.Rad2Deg;

            pitch = Mathf.Clamp(pitch, EyesYRange.x, EyesYRange.y);
            yaw = Mathf.Clamp(yaw, EyesXRange.x, EyesXRange.y);

            Quaternion lookRotation = referenceRotation * Quaternion.Euler(pitch, yaw, 0f);
            Vector3 lookForward = lookRotation * Vector3.forward;
            Vector3 headUp = headReference.TransformDirection(eyesHeadUp);
            Vector3 lookUp = Vector3.ProjectOnPlane(headUp, lookForward);

            if (lookUp.sqrMagnitude < 0.000001f)
                lookUp = Vector3.ProjectOnPlane(lookRotation * Vector3.up, lookForward);

            return Quaternion.LookRotation(lookForward, lookUp.normalized);
        }


        private void UpdateEyesLogics()
        {
            if (CustomEyesLogics) return;

            if (EyesNoKeyframes)
                for (int i = 0; i < eyeForwards.Length; i++)
                {
                    eyes[i].localRotation = eyesInitLocalRotations[i];
                }

            Transform eyeTarget = EyesTarget;
            if (eyeTarget == null)
            {
                if (MomentLookTransform != null) eyeTarget = MomentLookTransform; else eyeTarget = ObjectToFollow;
            }

            bool fade = false;
            if (eyeTarget == null) fade = true;
            else
            {
                if (EyesTarget == null)
                    if (LookState != FLookAnimator.EFHeadLookState.ClampedAngle && LookState != EFHeadLookState.Following) fade = true;
            }

            if (fade)
                EyesOutOfRangeBlend = Mathf.Max(0f, EyesOutOfRangeBlend - delta);
            else
                EyesOutOfRangeBlend = Mathf.Min(1f, EyesOutOfRangeBlend + delta);


            _eyesBlend = EyesBlend * EyesOutOfRangeBlend * LookAnimatorAmount;
            if (_eyesBlend <= 0f) return;


            if (eyeTarget != null)
            {
                Vector3 lookStartPosition = GetLookStartMeasurePosition();
                Quaternion lookRotation = GetEyesLookRotation(eyeTarget.position - lookStartPosition);

                for (int i = 0; i < eyes.Length; i++)
                {
                    Quaternion initRot = eyes[i].rotation;

                    float mul = 1f;
                    if (eyes[i] == LeftEye) { if (InvertLeftEye) mul = -1f; } else if (eyes[i] == RightEye) if (InvertRightEye) mul = -1f;
                    Quaternion eyeMapping = Quaternion.Inverse(Quaternion.LookRotation(eyeForwards[i] * mul, eyeUps[i]));
                    Quaternion newEyeRot = lookRotation * eyeMapping;

                    if (EyesOffsetRotation != Vector3.zero) newEyeRot *= Quaternion.Euler(EyesOffsetRotation);
                    if (eyes[i] == LeftEye) { if (LeftEyeOffsetRotation != Vector3.zero) newEyeRot *= Quaternion.Euler(LeftEyeOffsetRotation); }
                    else if (eyes[i] == RightEye) if (RightEyeOffsetRotation != Vector3.zero) newEyeRot *= Quaternion.Euler(RightEyeOffsetRotation);

                    eyesLerpRotations[i] = Quaternion.Slerp(eyesLerpRotations[i], newEyeRot, delta * Mathf.Lerp(2f, 40f, EyesSpeed));

                    eyes[i].rotation = Quaternion.Slerp(initRot, eyesLerpRotations[i], _eyesBlend);
                }
            }

        }

    }
}
