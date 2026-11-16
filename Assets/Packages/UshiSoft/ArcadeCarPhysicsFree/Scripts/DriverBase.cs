using UnityEngine;

namespace UshiSoft.UACPF
{
    [RequireComponent(typeof(CarControllerBase))]
    public class DriverBase : MonoBehaviour
    {
        protected CarControllerBase _carController;

        private bool stopping;

        public CarControllerBase CarController => _carController;

        public bool Stopping
        {
            get => stopping;
            set => stopping = value;
        }

        protected virtual void Awake()
        {
            _carController = GetComponent<CarControllerBase>();
        }

        private void Update()
        {
            if (stopping)
            {
                Stop();
            }
            else
            {
                Drive();
            }
        }

        protected virtual void Drive()
        {
        }

        protected virtual void Stop()
        {
            _carController.SteerInput = 0f;
            _carController.ThrottleInput = 0f;
            _carController.BrakeInput = 1f;
        }
    }
}