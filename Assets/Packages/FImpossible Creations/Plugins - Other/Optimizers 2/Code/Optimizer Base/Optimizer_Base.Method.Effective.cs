using UnityEngine;

namespace FIMSpace.FOptimizing
{
    public abstract partial class Optimizer_Base
    {
        private void InitEffectiveOptimizer()
        {
            if (!AddToContainer) OptimizersManager.Instance.RegisterNotContainedEffectiveOptimizer(this, true);

            InitCullingGroups(GetDistanceMeasures(), GetDetectionRadiusRaw(), OptimizersManager.MainCamera);
            InitDynamicOptimizer(false);
        }

        private void EffectiveLODUpdate()
        {
            Vector3 position = UseMultiShape ? PreviousPosition : GetReferencePosition();
            float dist = ( position - mainVisibilitySphere.position ).magnitude;
            if( dist > moveTreshold ) RefreshEffectiveCullingGroups();
        }

        protected virtual void RefreshEffectiveCullingGroups()
        {
            if( !UseMultiShape )
            {
                mainVisibilitySphere.position = GetReferencePosition();

                if( OwnerContainer != null )
                    OwnerContainer.CullingSpheres[ContainerSphereId].position = mainVisibilitySphere.position;
                else
                    visibilitySpheres[0].position = mainVisibilitySphere.position;
            }
            else
            {
                RefreshEffectiveCullingGroupsMultiShape();
            }
        }
    }
}
