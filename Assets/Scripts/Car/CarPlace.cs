using DwarfSimulator.Player;
using UnityEngine;

namespace DwarfSimulator.Car
{
    public class CarPlace : MonoBehaviour
    {
        [SerializeField] private Transform top;
        [SerializeField] private Transform bottom;
        
        public void PlaceTop(Player.Player player)
        {
            player.gameObject.GetComponent<PlayerMouvement>().Freeze(top.position, top.rotation);
        }
    }
}