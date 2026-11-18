using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FIMSpace.FLook
{
    /// <summary>
    /// FC: In this partial class we store helper methods
    /// </summary>
    public partial class FLookAnimator
    {
        #region Hierarchy window icon
        public string EditorIconPath { get { if( PlayerPrefs.GetInt( "AnimsH", 1 ) == 0 ) return ""; else return "Look Animator/LookAnimator_SmallIcon"; } }
        public void OnDrop( UnityEngine.EventSystems.PointerEventData data ) { }

        #endregion

        /// <summary> Variable helping eyes animator implementation drawing </summary>
        public bool _editor_hideEyes = false;
        public string _editor_displayName = "Look Animator 2";

        public enum EEditorLookCategory { Setup, Tweak, Limit, Features, Corrections }
        public EEditorLookCategory _Editor_Category = EEditorLookCategory.Setup;

#if UNITY_EDITOR
        /// <summary>
        /// Validating changes for custom inspector window from editor script
        /// </summary>
        public void UpdateForCustomInspector()
        {
            OnValidate();
        }
#endif


        /// <summary>
        /// Computing elastic clamp angle for given parameters
        /// </summary>
        private float GetClampedAngle( float current, float limit, float elastic, float sign = 1f )
        {
            if( elastic <= 0f ) return limit;
            else
            {
                float elasticRange = 0f;

                if( elastic > 0f )
                {
                    elasticRange = FEasing.EaseOutCubic( 0f, elastic, ( current * sign - limit * sign ) / ( 180f + limit * sign ) );
                }

                return limit + elasticRange * sign;
            }
        }


        /// <summary>
        /// Computing variables making Quaternion.Look universal for different skeletonal setups
        /// </summary>
        private void ComputeBonesRotationsFixVariables()
        {
            if( baseTransform != null )
            {
                Quaternion preRot = baseTransform.rotation;
                baseTransform.rotation = Quaternion.identity;

                FromAuto = LeadBone.rotation * -Vector3.forward;

                float angl = Quaternion.Angle( Quaternion.identity, LeadBone.rotation );
                Quaternion rotateAxis = ( LeadBone.rotation * Quaternion.Inverse( Quaternion.FromToRotation( FromAuto, ModelForwardAxis ) ) );

                OffsetAuto = Quaternion.AngleAxis( angl, rotateAxis.eulerAngles.normalized ).eulerAngles;

                baseTransform.rotation = preRot;

                RefreshParentalLookReferenceAxis();

                headForward = Quaternion.FromToRotation( LeadBone.InverseTransformDirection( baseTransform.TransformDirection( ModelForwardAxis.normalized ) ), Vector3.forward ) * Vector3.forward;
            }
            else
            {
                Debug.LogWarning( "Base Transform isn't defined, so we can't use auto correction!" );
            }
        }

        private void RefreshParentalLookReferenceAxis()
        {
            parentalReferenceLookForward = Quaternion.Inverse( LeadBone.parent.rotation ) * baseTransform.rotation * ModelForwardAxis.normalized;
            parentalReferenceUp = Quaternion.Inverse( LeadBone.parent.rotation ) * baseTransform.rotation * ModelUpAxis.normalized;
        }

        /// <summary>
        /// Returning forward direction of head bone in main transform space
        /// </summary>
        public Vector3 GetCurrentHeadForwardDirection()
        {
            return ( LeadBone.rotation * Quaternion.FromToRotation( headForward, Vector3.forward ) ) * Vector3.forward;
        }


        /// <summary>
        /// Searching through component's owner to find head or neck bone
        /// </summary>
        public virtual void FindHeadBone()
        {
            if( BaseTransform == null ) baseTransform = transform; else baseTransform = BaseTransform;
            // First let's check if it's humanoid character, then we can get head bone transform from it
            Transform root = transform;
            if( baseTransform ) root = baseTransform;

            Animator animator = root.GetComponentInChildren<Animator>();
            Transform animatorHeadBone = null;
            if( animator )
            {
                if( animator.isHuman )
                    animatorHeadBone = animator.GetBoneTransform( HumanBodyBones.Head );
            }

            List<SkinnedMeshRenderer> sMeshs = new List<SkinnedMeshRenderer>();// = root.GetComponentInChildren<SkinnedMeshRenderer>();

            foreach( var tr in root.GetComponentsInChildren<Transform>() )
            {
                if( tr == null ) continue;
                SkinnedMeshRenderer sMesh = tr.GetComponent<SkinnedMeshRenderer>();
                if( sMesh ) sMeshs.Add( sMesh );
            }

            Transform leadBone = null;
            Transform probablyWrongTransform = null;

            for( int s = 0; s < sMeshs.Count; s++ )
            {
                Transform t;

                for( int i = 0; i < sMeshs[s].bones.Length; i++ )
                {
                    t = sMeshs[s].bones[i];
                    if( t.name.ToLower().Contains( "head" ) )
                    {
                        if( t.parent == root ) continue; // If it's just mesh object from first depths

                        leadBone = t;
                        break;
                    }
                }

                if( !leadBone )
                    for( int i = 0; i < sMeshs[s].bones.Length; i++ )
                    {
                        t = sMeshs[s].bones[i];
                        if( t.name.ToLower().Contains( "neck" ) )
                        {
                            leadBone = t;
                            break;
                        }
                    }
            }


            foreach( Transform t in root.GetComponentsInChildren<Transform>() )
            {
                if( t.name.ToLower().Contains( "head" ) )
                {
                    if( t.GetComponent<SkinnedMeshRenderer>() )
                    {
                        if( t.parent == root ) continue; // If it's just mesh object from first depths
                        probablyWrongTransform = t;
                        continue;
                    }

                    leadBone = t;
                    break;
                }
            }

            if( !leadBone )
                foreach( Transform t in root.GetComponentsInChildren<Transform>() )
                {
                    if( t.name.ToLower().Contains( "neck" ) )
                    {
                        leadBone = t;
                        break;
                    }
                }

            if( leadBone == null && animatorHeadBone != null )
                leadBone = animatorHeadBone;
            else
            if( leadBone != null && animatorHeadBone != null )
            {
                if( animatorHeadBone.name.ToLower().Contains( "head" ) ) leadBone = animatorHeadBone;
                else
                    if( !leadBone.name.ToLower().Contains( "head" ) ) leadBone = animatorHeadBone;
            }

            if( leadBone )
            {
                LeadBone = leadBone;
            }
            else
            {
                if( probablyWrongTransform )
                {
                    LeadBone = probablyWrongTransform;
                    Debug.LogWarning( "[LOOK ANIMATOR] Found " + probablyWrongTransform + " but it's probably wrong transform" );
                }
                else
                {
                    Debug.LogWarning( "[LOOK ANIMATOR] Couldn't find any fitting bone" );
                }
            }
        }



        /// <summary>
        /// Searching through component's owner to find clavicle / shoulder and upperarm bones
        /// </summary>
        public virtual void FindCompensationBones()
        {
            // First let's check if it's humanoid character, then we can get head bone transform from it
            Transform root = transform;
            if( BaseTransform ) root = BaseTransform;

            Animator animator = root.GetComponentInChildren<Animator>();

            List<Transform> compensationBones = new List<Transform>();

            Transform headBone = LeadBone;

            if( animator )
            {
                if( animator.isHuman )
                {
                    Transform b = animator.GetBoneTransform( HumanBodyBones.LeftShoulder );
                    if( b ) compensationBones.Add( b );

                    b = animator.GetBoneTransform( HumanBodyBones.RightShoulder );
                    if( b ) compensationBones.Add( b );

                    b = animator.GetBoneTransform( HumanBodyBones.LeftUpperArm );
                    if( b ) compensationBones.Add( b );

                    b = animator.GetBoneTransform( HumanBodyBones.RightUpperArm );
                    if( b ) compensationBones.Add( b );

                    if( !headBone ) animator.GetBoneTransform( HumanBodyBones.Head );
                }
                else
                {
                    if( animator )
                    {
                        foreach( Transform t in animator.transform.GetComponentsInChildren<Transform>() )
                        {
                            if( t.name.ToLower().Contains( "clav" ) )
                            {
                                if( !compensationBones.Contains( t ) ) compensationBones.Add( t );
                            }
                            else
                            if( t.name.ToLower().Contains( "shoulder" ) )
                            {
                                if( !compensationBones.Contains( t ) ) compensationBones.Add( t );
                            }
                            else
                            if( t.name.ToLower().Contains( "uppera" ) )
                            {
                                if( !compensationBones.Contains( t ) ) compensationBones.Add( t );
                            }
                        }
                    }
                }
            }

            if( compensationBones.Count != 0 )
            {
                for( int i = 0; i < compensationBones.Count; i++ )
                {
                    // Checking if this bone is not already in compensation bones list
                    bool already = false;
                    for( int c = 0; c < CompensationBones.Count; c++ )
                    {
                        if( compensationBones[i] == CompensationBones[c].Transform )
                        {
                            already = true;
                            break;
                        }
                    }

                    if( already ) continue;

                    // Fill nulls if available
                    bool filled = false;
                    for( int c = 0; c < CompensationBones.Count; c++ )
                    {
                        if( CompensationBones[c].Transform == null )
                        {
                            CompensationBones[c] = new FLookAnimator.CompensationBone( compensationBones[i] );
                            filled = true;
                            break;
                        }
                    }

                    if( !filled )
                        CompensationBones.Add( new FLookAnimator.CompensationBone( compensationBones[i] ) );
                }

                for( int c = CompensationBones.Count - 1; c >= 0; c-- )
                {
                    if( CompensationBones[c].Transform == null ) CompensationBones.RemoveAt( c );
                }
            }
        }



    }
}