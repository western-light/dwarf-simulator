using UnityEngine;

namespace UshiSoft.UACPF
{
    [RequireComponent(typeof(Camera))]
    public class CameraBase : MonoBehaviour
    {
        [SerializeField] protected CarControllerBase _targetCar;

        public virtual CarControllerBase TargetCar
        {
            get => _targetCar;
            set => _targetCar = value;
        }
    }
}