using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FIMSpace.FLook
{
    public partial class FLookAnimator_Editor
    {
        // RESOURCES ----------------------------------------

        public static Texture2D _TexLookAnimIcon { get { if (__texLookAnimIcon != null) return __texLookAnimIcon; __texLookAnimIcon = Resources.Load<Texture2D>("Look Animator/LookAnimator_SmallIcon"); return __texLookAnimIcon; } }
        private static Texture2D __texLookAnimIcon = null;
        public static Texture2D _TexBackbonesIcon { get { if (__texBacBonIcon != null) return __texBacBonIcon; __texBacBonIcon = Resources.Load<Texture2D>("Look Animator/BackBones"); return __texBacBonIcon; } }
        private static Texture2D __texBacBonIcon = null;
        public static Texture2D _TexCompensationIcon { get { if (__texCompensIcon != null) return __texCompensIcon; __texCompensIcon = Resources.Load<Texture2D>("Look Animator/Compensation"); return __texCompensIcon; } }
        private static Texture2D __texCompensIcon = null;
        public static Texture2D _TexBirdIcon { get { if (__texBirdIcon != null) return __texBirdIcon; __texBirdIcon = Resources.Load<Texture2D>("Look Animator/BirdMode"); return __texBirdIcon; } }
        private static Texture2D __texBirdIcon = null;
        public static Texture2D _TexEyesIcon { get { if (__texEyeIcon != null) return __texEyeIcon; __texEyeIcon = Resources.Load<Texture2D>("Look Animator/LookEyes"); return __texEyeIcon; } }
        private static Texture2D __texEyeIcon = null;


        private static UnityEngine.Object _manualFile;


        // HELPER VARIABLES ----------------------------------------
        private FLookAnimator Get { get { if (_get == null) _get = target as FLookAnimator; return _get; } }
        private FLookAnimator _get;

        static bool drawDefaultInspector = false;
        private Color unchangedC = new Color(1f, 1f, 1f, 0.65f);
        private Color limitsC = new Color(1f, 1f, 1f, 0.88f);
        private Color c;
        private Color bc;

        List<SkinnedMeshRenderer> skins;
        SkinnedMeshRenderer largestSkin;
        Animator animator;
        Animation animation;
        Behaviour componentAnimator;


        /// <summary>
        /// Searching through component's owner to find clavicle / shoulder and upperarm bones
        /// </summary>
        private void FindCompensationBones(FLookAnimator headLook)
        {
            headLook.FindCompensationBones();
            compensationBonesCount = headLook.CompensationBones.Count;
            EditorUtility.SetDirty(target);
        }


        void CheckForComponents()
        {
            if (skins == null) skins = new List<SkinnedMeshRenderer>();
           
            Transform bTransform = Get.BaseTransform;
            if (bTransform == null) bTransform = Get.transform;

            foreach (var t in bTransform.GetComponentsInChildren<Transform>())
            {
                SkinnedMeshRenderer s = t.GetComponent<SkinnedMeshRenderer>(); if (s) skins.Add(s);
                if (!animator) animator = t.GetComponent<Animator>();
                if (!animator) if (!animation) animation = t.GetComponent<Animation>();
            }

            if ((skins != null && largestSkin != null) && (animator != null || animation != null)) return;

            if (bTransform != Get.transform)
            {
                foreach (var t in Get.transform.GetComponentsInChildren<Transform>())
                {
                    SkinnedMeshRenderer s = t.GetComponent<SkinnedMeshRenderer>(); if (!skins.Contains(s)) if (s) skins.Add(s);
                    if (!animator) animator = t.GetComponent<Animator>();
                    if (!animator) if (!animation) animation = t.GetComponent<Animation>();
                }
            }

            if (skins.Count > 1)
            {
                largestSkin = skins[0];
                for (int i = 1; i < skins.Count; i++)
                    if (skins[i].bones.Length > largestSkin.bones.Length)
                        largestSkin = skins[i];
            }
            else
                if (skins.Count > 0) largestSkin = skins[0];

        }


        /// <summary>
        /// Updating component from custom inspector helper method
        /// </summary>
        private bool UpdateCustomInspector(FLookAnimator Get, bool allowInPlaymode = true)
        {
            if (!allowInPlaymode)
            {
                if (Application.isPlaying) return false;
            }
            else
                Get.UpdateForCustomInspector();

            return true;
        }


        /// <summary>
        /// Searching through component's owner to find head or neck bone
        /// </summary>
        private void FindHeadBone(FLookAnimator headLook)
        {
            headLook.FindHeadBone();
            EditorUtility.SetDirty(target);
        }

        public Vector3 RoundVector(Vector3 v)
        {
            return new Vector3((float)Math.Round(v.x, 1), (float)Math.Round(v.y, 1), (float)Math.Round(v.z, 1));
        }

    }
}