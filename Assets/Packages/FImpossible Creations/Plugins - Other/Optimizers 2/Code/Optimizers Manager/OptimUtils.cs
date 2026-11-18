using System.Collections.Generic;
using UnityEngine;

namespace FIMSpace.FOptimizing
{
#if UNITY_6000_6_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public static class OptimUtils
    {
#if UNITY_2019_4_OR_NEWER
            private static IgnoreOptimizer ign;
#endif
        public static bool ShouldBeIgnored(Component comp)
        {
            if (comp is IOptimizersDontOptimize) return true;
#if UNITY_2019_4_OR_NEWER
            return comp.gameObject.TryGetComponent(out ign);
#else
            return comp.gameObject.GetComponent<IgnoreOptimizer>() != null;
#endif
        }
    }
}
