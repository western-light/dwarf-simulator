using DwarfSimulator.Car.Controller;
using DwarfSimulator.Player;
using UnityEngine;

namespace DwarfSimulator.Car
{
    public class CarPlace : MonoBehaviour
    {
        [SerializeField] private Transform top;
        [SerializeField] private Transform bottom;
        private PlayerCarControl _playerCarControl;

        private void Awake()
        {
            if (!TryGetComponent<CarControllerBase>(out _))
            {
                Debug.LogError("CarPlace requires a CarControllerBase on the same GameObject.", this);
                enabled = false;
                return;
            }

            _playerCarControl = GetComponent<PlayerCarControl>();
            if (_playerCarControl == null)
                _playerCarControl = gameObject.AddComponent<PlayerCarControl>();
        }

        private void Start()
        {
            _playerCarControl?.SetControlMode(PlayerCarControl.ControlMode.None);
        }

        public void PlaceTop(Player.Player player)
        {
            PlacePlayer(player.gameObject, top);
            _playerCarControl?.SetControlMode(PlayerCarControl.ControlMode.Steering);
        }

        public void PlaceBottom(Player.Player player)
        {
            PlacePlayer(player.gameObject, bottom);
            _playerCarControl?.SetControlMode(PlayerCarControl.ControlMode.Speed);
        }

        private void PlacePlayer(GameObject player, Transform parent)
        {
            player.GetComponent<Collider>().enabled = false;
            player.GetComponent<Rigidbody>().isKinematic = true;
            player.GetComponent<PlayerMouvement>().Freeze(parent.position, parent.rotation);
            player.transform.SetParent(parent);
        }
    }
}