using UnityEngine;

namespace DwarfSimulator.Player
{
    public class PlayerNameBillboard : MonoBehaviour
    {
        
        private Camera localCamera;

        private void LateUpdate()
        {
            if (localCamera == null)
            {
                localCamera = Camera.main;
                return;
            }

            transform.rotation = localCamera.transform.rotation;
        }
    }
}
