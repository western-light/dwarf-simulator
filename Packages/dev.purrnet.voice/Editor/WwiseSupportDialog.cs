#if UNITY_EDITOR
using UnityEditor;

namespace PurrVoice.Editor
{
    static class WwiseSupportDialog
    {
        internal static void Show()
        {
            bool enable = EditorUtility.DisplayDialog(
                "Enable Wwise Support",
                "This will add the PURRVOICE_WWISE scripting define symbol.\n\n" +
                "Make sure the Wwise Unity integration is installed in your project, " +
                "otherwise the Wwise integration scripts will not compile.\n\n" +
                "Enable Wwise support?",
                "Enable",
                "Cancel");

            if (enable)
            {
                IntegrationDefineManager.SetWwiseEnabled(true);
            }
        }
    }
}
#endif
